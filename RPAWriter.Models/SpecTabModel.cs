using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// Spec tab model: owns the imported specification and every scenario on the Spec tab
    /// (import, clear, algorithm, robot generation, similarity search, security audit,
    /// markdown-to-docx conversion).
    /// <para>
    /// The model is UI-free by contract (<c>documentation/13-model-layer.md</c>): it is constructed,
    /// exercised and asserted without a view model and without a view. Filesystem access, dialogs
    /// and the VS document opener are injected as contracts. Progress and errors are reported
    /// through <see cref="BaseModel.Notify"/> and <see cref="SpecMessages"/>, never through chat.
    /// </para>
    /// </summary>
    public sealed class SpecTabModel : BaseModel
    {
        private const string SpecLockedMessage =
            "The spec file is open in another application (e.g. Word/Excel). Close it and try again.";

        private readonly Func<IAiProvider> _aiProvider;
        private readonly ISolutionPathProvider _solutionPathProvider;
        private readonly IFilePicker _filePicker;
        private readonly IFolderPicker _folderPicker;
        private readonly IFileWriter _fileWriter;
        private readonly IFileOverwritePrompter _overwritePrompter;
        private readonly Func<string, string, bool> _confirm;
        private readonly IDocumentOpener _documentOpener;
        private readonly IDocumentReader _documentReader;
        private readonly IRobotGenerator _robotGenerator;
        private readonly Func<string, IKnowledgeBase> _knowledgeBaseFactory;
        private readonly Func<SettingsData> _settingsProvider;
        private readonly SynchronizationContext _syncContext;

        private CancellationTokenSource _securityAuditCts;
        private SpecModel _currentSpec;
        private bool _isImporting;
        private string _importStatus = "";
        private bool _hasOutput;

        /// <summary>Creates the model with all external dependencies injected.</summary>
        public SpecTabModel(
            Func<IAiProvider> aiProvider,
            ISolutionPathProvider solutionPathProvider,
            IFilePicker filePicker,
            IFolderPicker folderPicker,
            IFileWriter fileWriter,
            IFileOverwritePrompter overwritePrompter,
            Func<string, string, bool> confirm,
            IDocumentOpener documentOpener,
            IDocumentReader documentReader,
            IRobotGenerator robotGenerator,
            Func<string, IKnowledgeBase> knowledgeBaseFactory,
            Func<SettingsData> settingsProvider,
            SynchronizationContext syncContext = null)
        {
            _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
            _solutionPathProvider = solutionPathProvider ?? throw new ArgumentNullException(nameof(solutionPathProvider));
            _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
            _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
            _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
            _overwritePrompter = overwritePrompter ?? throw new ArgumentNullException(nameof(overwritePrompter));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _documentOpener = documentOpener ?? throw new ArgumentNullException(nameof(documentOpener));
            _documentReader = documentReader ?? throw new ArgumentNullException(nameof(documentReader));
            _robotGenerator = robotGenerator ?? throw new ArgumentNullException(nameof(robotGenerator));
            _knowledgeBaseFactory = knowledgeBaseFactory ?? throw new ArgumentNullException(nameof(knowledgeBaseFactory));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _syncContext = syncContext;
        }

        #region State

        /// <summary>Transcript of the Spec tab (progress, results, errors). Rendered by the spec list control.</summary>
        public ObservableCollection<ChatMessage> SpecMessages { get; } = new ObservableCollection<ChatMessage>();

        /// <summary>The currently imported spec, or null before the first import.</summary>
        public SpecModel CurrentSpec
        {
            get => _currentSpec;
            private set => SetProperty(ref _currentSpec, value);
        }

        /// <summary>True while an import/algorithm run is in progress (drives the progress panel).</summary>
        public bool IsImporting
        {
            get => _isImporting;
            private set => SetProperty(ref _isImporting, value);
        }

        /// <summary>Status line shown inside the import progress panel.</summary>
        public string ImportStatus
        {
            get => _importStatus;
            private set => SetProperty(ref _importStatus, value);
        }

        /// <summary>True once at least one spec message exists (drives the "Copy all" button).</summary>
        public bool HasOutput
        {
            get => _hasOutput;
            private set => SetProperty(ref _hasOutput, value);
        }

        #endregion

        #region Commands

        /// <summary>Command target: import a spec document and decompose it into tasks.</summary>
        public void ImportSpecCommand() => _ = ImportSpecAsync();

        /// <summary>Awaitable form of <see cref="ImportSpecCommand"/> (used by tests).</summary>
        public async Task ImportSpecAsync()
        {
            var solutionDir = _solutionPathProvider.GetSolutionDirectory();
            if (solutionDir == null)
            {
                Report("Spec import is only available when a solution is open.", NotifyLevel.Warning);
                return;
            }

            var filePath = _filePicker.PickFile(
                "Select spec",
                "Specs (*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md)|*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md|All files (*.*)|*.*");
            if (filePath == null)
                return;

            if (FileLockDetector.IsFileLocked(filePath))
            {
                Report(SpecLockedMessage, NotifyLevel.Error);
                return;
            }

            IsImporting = true;
            ImportStatus = "Reading file…";
            try
            {
                var docsDir = Path.Combine(solutionDir, "docs");
                _fileWriter.CreateDirectory(docsDir);

                // Unchanged copy of the original spec file.
                _fileWriter.CopyFile(filePath, Path.Combine(docsDir, Path.GetFileName(filePath)), overwrite: true);

                var mediaDir = Path.Combine(docsDir, "media");
                var spec = _documentReader.Read(filePath, mediaDir);

                // 1. Structured spec.md (headings, lists, tables).
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "spec.md"), spec.MarkdownText, "spec.md");

                // 2. Decompose the spec into tasks.
                ImportStatus = "Parsing tasks (AI)…";
                var parsing = AppendSpecStreaming();
                parsing.Reasoning = "Decomposing the specification into tasks…";
                var tasks = await _aiProvider().DecomposeTasksAsync(spec.RawText);
                spec.Tasks = tasks.ToList();
                parsing.IsThinking = false;
                parsing.Text = "Parsed " + tasks.Count + " tasks.";

                // 3. Structured tasks.md.
                var tasksMarkdown = MarkdownContent.TasksToMarkdown(spec);
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "tasks.md"), tasksMarkdown, "tasks.md");

                CurrentSpec = spec;

                var sb = new StringBuilder();
                sb.AppendLine("Spec: " + spec.Name);
                sb.AppendLine();
                sb.AppendLine(spec.RawText);
                sb.AppendLine();
                sb.AppendLine("Tasks: " + spec.Tasks.Count);
                foreach (var t in spec.Tasks)
                    sb.AppendLine("- " + (t.Action ?? "") + " → " + (t.Target ?? "") + " [" + (t.ActivityType ?? "") + "]");
                AppendSpecText(sb.ToString());

                AppendSpecText("Imported spec: " + spec.Name + " (tasks: " + spec.Tasks.Count + ")");
            }
            catch (Exception ex) when (FileLockDetector.IsFileLocked(ex))
            {
                Report(SpecLockedMessage, NotifyLevel.Error);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
            finally
            {
                IsImporting = false;
            }
        }

        /// <summary>Command target: delete the solution docs folder contents after confirmation.</summary>
        public void ClearSpecCommand()
        {
            var solutionDir = _solutionPathProvider.GetSolutionDirectory();
            if (solutionDir == null)
            {
                Report("Clear docs is only available when a solution is open.", NotifyLevel.Warning);
                return;
            }

            var docsDir = Path.Combine(solutionDir, "docs");
            if (!_fileWriter.DirectoryExists(docsDir))
            {
                Report("No docs folder to clear.", NotifyLevel.Info);
                return;
            }

            if (!_confirm(
                "Clear docs",
                "Delete all files and folders in the docs folder?\n\n" + docsDir + "\n\nThis cannot be undone."))
            {
                return;
            }

            try
            {
                var removed = _fileWriter.EnumerateFiles(docsDir).Count;
                _fileWriter.DeleteDirectory(docsDir, recursive: true);
                CurrentSpec = null;
                Report("Cleared " + removed + " file(s) from docs.", NotifyLevel.Success);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        /// <summary>Command target: generate the algorithm (three drafts + reconciliation).</summary>
        public void GenerateAlgorithmCommand() => _ = GenerateAlgorithmAsync();

        /// <summary>Awaitable form of <see cref="GenerateAlgorithmCommand"/> (used by tests).</summary>
        public async Task GenerateAlgorithmAsync()
        {
            var solutionDir = _solutionPathProvider.GetSolutionDirectory();
            if (solutionDir == null)
            {
                Report("Generate algorithm is only available when a solution is open.", NotifyLevel.Warning);
                return;
            }

            var algorithmGenerator = _aiProvider() as IAlgorithmGenerator;
            if (algorithmGenerator == null)
            {
                Report("The current AI provider does not support algorithm generation.", NotifyLevel.Warning);
                return;
            }

            var docsDir = Path.Combine(solutionDir, "docs");
            _fileWriter.CreateDirectory(docsDir);

            var specMarkdown = ResolveSpecMarkdown(docsDir);
            if (specMarkdown == null)
            {
                Report("Import a spec first or ensure docs/spec.md exists.", NotifyLevel.Warning);
                return;
            }

            IsImporting = true;
            ImportStatus = "Generating algorithm…";
            try
            {
                var drafts = new List<string>();
                for (var i = 0; i < 3; i++)
                {
                    ImportStatus = "Generating algorithm draft " + (i + 1) + "/3…";
                    var streaming = AppendSpecStreaming();
                    streaming.Reasoning = "Generating algorithm draft " + (i + 1) + "/3…";
                    var draft = await algorithmGenerator.GenerateAlgorithmDraftAsync(specMarkdown);
                    streaming.IsThinking = false;
                    streaming.Text = "Draft " + (i + 1) + " generated.";
                    drafts.Add(draft);
                    _fileWriter.WriteAllText(Path.Combine(docsDir, "alg-" + (i + 1) + ".md"), draft);
                    AppendSpecText("Wrote alg-" + (i + 1) + ".md");
                }

                ImportStatus = "Reconciling drafts…";
                var consolidating = AppendSpecStreaming();
                consolidating.Reasoning = "Reconciling drafts…";
                var consolidated = await algorithmGenerator.ConsolidateAlgorithmAsync(specMarkdown, drafts);
                consolidating.IsThinking = false;
                consolidating.Text = "Algorithm consolidated.";

                _fileWriter.WriteAllText(Path.Combine(docsDir, "alg.md"), consolidated.AlgorithmMarkdown ?? "");
                _fileWriter.WriteAllText(Path.Combine(docsDir, "questions.md"), consolidated.QuestionsMarkdown ?? "");
                AppendSpecText("Wrote alg.md and questions.md");
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
            finally
            {
                IsImporting = false;
            }
        }

        /// <summary>Command target: generate a robot next to the imported spec.</summary>
        public void GenerateRobotCommand() => _ = GenerateRobotAsync();

        /// <summary>Awaitable form of <see cref="GenerateRobotCommand"/> (used by tests).</summary>
        public async Task GenerateRobotAsync()
        {
            if (_currentSpec == null)
            {
                Report("Import a spec first.", NotifyLevel.Warning);
                return;
            }

            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null)
                    return;

                var kb = _knowledgeBaseFactory(kbPath);
                var pipeline = new RpaPipelineService(_documentReader, _aiProvider(), kb, _robotGenerator);
                var result = await pipeline.GenerateRobotFromSpecAsync(_currentSpec.SourcePath);

                var dir = Path.GetDirectoryName(_currentSpec.SourcePath);
                var baseName = Path.GetFileNameWithoutExtension(_currentSpec.SourcePath);
                var xamlPath = Path.Combine(dir, baseName + ".xaml");
                var csPath = Path.Combine(dir, baseName + ".Actions.cs");
                _fileWriter.WriteAllText(xamlPath, result.Xaml);
                _fileWriter.WriteAllText(csPath, result.CSharpCode);

                AppendSpecText(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendSpecText("Robot generated and saved next to the spec: " + xamlPath + ", " + csPath);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        /// <summary>Command target: find tasks in the knowledge base similar to the imported spec.</summary>
        public void FindSimilarCommand() => _ = FindSimilarAsync();

        /// <summary>Awaitable form of <see cref="FindSimilarCommand"/> (used by tests).</summary>
        public async Task FindSimilarAsync()
        {
            if (_currentSpec == null)
            {
                Report("Import a spec first.", NotifyLevel.Warning);
                return;
            }

            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null)
                    return;

                var kb = _knowledgeBaseFactory(kbPath);
                var matches = new List<SimilarTaskMatch>();
                foreach (var task in _currentSpec.Tasks)
                    matches.AddRange(await kb.FindSimilarTasksAsync(task, 3));

                var sb = new StringBuilder();
                foreach (var m in matches)
                    sb.AppendLine("[" + m.Score.ToString("P0") + "] " + m.Solution.Name + " — " + string.Join("; ", m.Reasons));
                AppendSpecText(sb.Length == 0 ? "No similar tasks found." : sb.ToString());
                AppendSpecText("Similar tasks found: " + matches.Count);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        /// <summary>Command target: run a read-only security audit over the sibling robot solutions.</summary>
        public void SecurityAuditCommand() => _ = SecurityAuditAsync();

        /// <summary>Awaitable form of <see cref="SecurityAuditCommand"/> (used by tests).</summary>
        public async Task SecurityAuditAsync()
        {
            if (_securityAuditCts != null)
            {
                Report("Security audit is already running.", NotifyLevel.Warning);
                return;
            }

            var ai = _aiProvider();
            if (ai == null)
            {
                Report("The AI provider is not available. Configure it in the Settings tab first.", NotifyLevel.Warning);
                return;
            }

            var baseDirectory = _solutionPathProvider.GetSolutionDirectory();
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = PickSecurityAuditDirectory();
                if (baseDirectory == null)
                    return;
            }

            var solutions = RobotSecurityAuditService.FindRobotSolutionsInSiblingFolders(baseDirectory);
            if (solutions.Count == 0)
            {
                Report(
                    "No robot solutions (RPA-<number>.sln/.slnx) found in " + baseDirectory +
                    " or its sibling RPA-* folders. Select the directory that contains them.",
                    NotifyLevel.Warning);
                var picked = PickSecurityAuditDirectory();
                if (picked == null)
                    return;
                baseDirectory = picked;
                solutions = RobotSecurityAuditService.FindRobotSolutionsInSiblingFolders(baseDirectory);
                if (solutions.Count == 0)
                {
                    Report(
                        "No robot solutions (RPA-<number>.sln/.slnx) found in " + baseDirectory +
                        " or its sibling RPA-* folders.",
                        NotifyLevel.Error);
                    return;
                }
            }

            var targetDirectory = _solutionPathProvider.GetSolutionDirectory() ?? baseDirectory;
            var docsDirectory = Path.Combine(targetDirectory, "docs");
            var reportPath = Path.Combine(docsDirectory, "security_audit.md");

            _securityAuditCts = new CancellationTokenSource();
            AppendSpecText("Auditing " + solutions.Count + " robot solution(s)...");

            var mode = string.Equals(_settingsProvider()?.SecurityAuditMode, "CredentialsOnly", StringComparison.OrdinalIgnoreCase)
                ? SecurityAuditMode.CredentialsOnly
                : SecurityAuditMode.Full;

            var service = new RobotSecurityAuditService(ai);
            service.MessageReported += OnSecurityAuditMessage;
            try
            {
                await service.AuditAsync(solutions, reportPath, cancellationToken: _securityAuditCts.Token, mode: mode);
                Report("Security audit completed. Report: " + reportPath, NotifyLevel.Success);

                if (_fileWriter.FileExists(reportPath))
                {
                    try
                    {
                        await _documentOpener.OpenAsync(reportPath);
                    }
                    catch
                    {
                        // Opening the report is best-effort; the audit itself already succeeded.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AppendSpecText("Security audit stopped by user.");
            }
            catch (Exception ex)
            {
                ReportError(ex);
                AppendSpecText("Security audit failed.\n\n" + ex);
            }
            finally
            {
                service.MessageReported -= OnSecurityAuditMessage;
                _securityAuditCts.Dispose();
                _securityAuditCts = null;
            }
        }

        /// <summary>Command target: convert a markdown file to docx.</summary>
        public void ConvertMarkdownToDocxCommand()
        {
            var filePath = _filePicker.PickFile("Markdown file", "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*");
            if (filePath == null)
                return;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                var baseName = Path.GetFileNameWithoutExtension(filePath);
                var outputPath = Path.Combine(dir, baseName + ".docx");
                var result = new MarkdownToDocxConverter().Convert(filePath, outputPath);
                AppendSpecText(
                    "Converted " + Path.GetFileName(filePath) + " → " + Path.GetFileName(outputPath) +
                    " (paragraphs: " + result.Paragraphs + ", images: " + result.Images + ", tables: " + result.Tables + ").");
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        #endregion

        #region Helpers

        private void RunOnUiThread(Action action)
        {
            if (_syncContext == null)
            {
                action();
                return;
            }

            _syncContext.Post(_ => action(), null);
        }

        private void Report(string message, NotifyLevel level)
            => RunOnUiThread(() => NotifyMessage(new Notify { Message = message, NotifyLevel = level }));

        private void AppendSpecMessage(ChatMessage message)
        {
            RunOnUiThread(() =>
            {
                SpecMessages.Add(message);
                HasOutput = true;
            });
        }

        private void AppendSpecText(string text)
            => AppendSpecMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });

        private ChatMessage AppendSpecStreaming()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.SystemNotice, IsThinking = true, Text = "Thinking…" };
            AppendSpecMessage(message);
            return message;
        }

        private void ReportError(Exception ex)
        {
            AppendSpecText(ex.ToString());
            Report(ex.Message, NotifyLevel.Error);
        }

        private string RequireKbPath()
        {
            var path = (_settingsProvider()?.KnowledgeBasePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                AppendSpecText("Knowledge base directory is not configured. Set the path in the \"Settings\" tab.");
                Report("Specify the knowledge base directory in the \"Settings\" tab first.", NotifyLevel.Warning);
                return null;
            }

            return path;
        }

        private string ResolveSpecMarkdown(string docsDir)
        {
            if (_currentSpec != null && !string.IsNullOrWhiteSpace(_currentSpec.MarkdownText))
                return _currentSpec.MarkdownText;

            var specPath = Path.Combine(docsDir, "spec.md");
            return _fileWriter.FileExists(specPath) ? _fileWriter.ReadAllText(specPath) : null;
        }

        private void WriteMarkdownWithConfirm(string path, string content, string displayName)
        {
            if (!_fileWriter.FileExists(path))
            {
                _fileWriter.WriteAllText(path, content);
                AppendSpecText("Created " + displayName + ".");
                return;
            }

            switch (_overwritePrompter.AskOverwrite(displayName))
            {
                case FileOverwriteChoice.Overwrite:
                    _fileWriter.WriteAllText(path, content);
                    AppendSpecText(displayName + " overwritten.");
                    break;

                case FileOverwriteChoice.ApplyFixes:
                    var existing = _fileWriter.ReadAllText(path);
                    var diff = TextDiff.Diff(existing, content);
                    var summary = diff.HasChanges ? diff.Summary : "No discrepancies found.";
                    AppendSpecText(summary);
                    if (diff.HasChanges)
                    {
                        var apply = _confirm("Partial fix — " + displayName, summary + "\n\nApply the fixes (overwrite)?");
                        if (apply)
                        {
                            _fileWriter.WriteAllText(path, content);
                            AppendSpecText("Fixes applied to " + displayName + ".");
                        }
                        else
                        {
                            AppendSpecText(displayName + " left unchanged.");
                        }
                    }
                    else
                    {
                        AppendSpecText(displayName + " has not changed.");
                    }
                    break;

                default:
                    AppendSpecText(displayName + " left unchanged.");
                    break;
            }
        }

        private string PickSecurityAuditDirectory()
        {
            var configured = (_settingsProvider()?.RobotsRootPath ?? "").Trim();
            var initial = !string.IsNullOrWhiteSpace(configured) && _fileWriter.DirectoryExists(configured)
                ? configured
                : "";
            return _folderPicker.PickFolder(
                "Select the directory that contains robot solutions (RPA-<number>.sln/.slnx).",
                initial);
        }

        private void OnSecurityAuditMessage(object sender, string message) => AppendSpecText(message);

        #endregion
    }
}
