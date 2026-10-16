using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter2.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Input;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RPAWriter.Core.Models;

namespace RPAWriter2
{
    /// <summary>
    /// Single extension window: agent chat and settings.
    /// </summary>
    public partial class ToolWindow1Control : UserControl
    {
        private SettingsData _settings;
        private IAiProvider _ai;
        private SpecModel _currentSpec;
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
        private ITokenUsageProvider? _tokenUsage;
        private IMessageNotifier? _messageNotifier;
        private IRetryNotifier? _retryNotifier;
        private DTE2? _dte;
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private CancellationTokenSource? _chatCts;
        private CancellationTokenSource? _kbImportCts;
        private volatile bool _kbImportPaused;
        private readonly DispatcherTimer _chatStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _chatStartedAt;
        private const string SpecLockedMessage = "The spec file is open in another application (e.g. Word/Excel). Close it and try again.";

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            ChatList.ItemsSource = _messages;
            LoadSettings();
            RecreateAiProvider();
            _buildFeedback = new BuildFeedbackService();
            _robotTemplate = new RobotTemplateService();
            HookSolutionEvents();
            UpdateCreateRobotVisibility();
            AppendChat("Agent ready. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
        }

        private void LoadSettings()
        {
            _settings = SettingsStore.Load();
            BaseUrlBox.Text = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://api.openai.com/v1" : _settings.BaseUrl;
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            ModelBox.Text = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4o-mini" : _settings.Model;
            KbPathBox.Text = _settings.KnowledgeBasePath ?? "";
            RobotsRootPathBox.Text = _settings.RobotsRootPath ?? "";
            RobotTemplateNameBox.Text = _settings.RobotTemplateName ?? "";
            RulesPathBox.Text = _settings.RulesPath ?? "";
            ContextWindowBox.Text = (_settings.ContextWindowSize > 0 ? _settings.ContextWindowSize : 409600).ToString();
        }

        private void RecreateAiProvider()
        {
            UnhookTokenUsage();
            UnhookMessageNotifier();
            UnhookRetryNotifier();
            _ai = AiProviderFactory.Create(_settings);
            HookTokenUsage();
            HookMessageNotifier();
            HookRetryNotifier();
            UpdateTokenUsageDisplay();
        }

        private string KbPath()
        {
            return (KbPathBox?.Text ?? "").Trim();
        }

        private string? RequireKbPath()
        {
            var p = KbPath();
            if (string.IsNullOrWhiteSpace(p))
            {
                AppendChat("Specify the knowledge base directory in the \"Settings\" tab first.");
                SetOutput("Knowledge base directory is not configured. Set the path in the \"Settings\" tab.");
                MainTabs.SelectedItem = SettingsTab;
                return null;
            }
            return p;
        }

        private string? RequireKbPathForKnowledgeBase()
        {
            var p = KbPath();
            if (string.IsNullOrWhiteSpace(p))
            {
                SetKbStatus("Knowledge base directory is not configured. Set the path in the \"Settings\" tab.");
                return null;
            }
            return p;
        }

        private void AppendChat(string text)
        {
            AppendChatMessage(new ChatMessage { Role = "system", Text = text });
        }

        private void AppendChatMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendChatMessage(message)));
                return;
            }
            _messages.Add(message);
            ChatScroll.ScrollToEnd();
        }

        private void SetOutput(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetOutput(text)));
                return;
            }
            OutputBox.Text = text;
        }

        private void SetKbStatus(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbStatus(status)));
                return;
            }
            KbStatusText.Text = status;
        }

        private void SetKbProgress(double value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbProgress(value)));
                return;
            }
            KbProgressBar.Value = value;
            KbProgressBar.Visibility = Visibility.Visible;
        }

        private void HookTokenUsage()
        {
            _tokenUsage = _ai as ITokenUsageProvider;
            if (_tokenUsage != null)
                _tokenUsage.TokenUsageUpdated += OnTokenUsageUpdated;
        }

        private void UnhookTokenUsage()
        {
            if (_tokenUsage != null)
                _tokenUsage.TokenUsageUpdated -= OnTokenUsageUpdated;
            _tokenUsage = null;
        }

        private void HookMessageNotifier()
        {
            _messageNotifier = _ai as IMessageNotifier;
            if (_messageNotifier != null)
                _messageNotifier.MessageSent += OnAiMessageSent;
        }

        private void UnhookMessageNotifier()
        {
            if (_messageNotifier != null)
                _messageNotifier.MessageSent -= OnAiMessageSent;
            _messageNotifier = null;
        }

        private void HookRetryNotifier()
        {
            _retryNotifier = _ai as IRetryNotifier;
            if (_retryNotifier != null)
                _retryNotifier.RetryAttempt += OnRetryAttempt;
        }

        private void UnhookRetryNotifier()
        {
            if (_retryNotifier != null)
                _retryNotifier.RetryAttempt -= OnRetryAttempt;
            _retryNotifier = null;
        }

        private void OnAiMessageSent(object? sender, AiMessageEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAiMessageSent(sender, e)));
                return;
            }
            AppendKbMessage("→ [" + (e.Role ?? "") + "] " + (e.Content ?? ""));
        }

        private void OnRetryAttempt(object? sender, RetryAttemptEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnRetryAttempt(sender, e)));
                return;
            }

            var status = e.StatusCode.HasValue
                ? "HTTP " + e.StatusCode.Value + (string.IsNullOrEmpty(e.ReasonPhrase) ? "" : " (" + e.ReasonPhrase + ")")
                : "no response";

            string message;
            if (e.WillRetry)
            {
                message = "Retry #" + e.Attempt + " → " + status;
                if (!string.IsNullOrWhiteSpace(e.Body))
                    message += ": " + e.Body;
                var total = e.Elapsed + e.Remaining;
                message += " (elapsed " + FormatKbDuration(e.Elapsed) + " / " + FormatKbDuration(total) +
                           ", next attempt in " + FormatKbDuration(e.WaitBeforeRetry) + ")";
            }
            else
            {
                message = "Retry budget exhausted after " + e.Attempt + " attempt(s): " + status;
                if (!string.IsNullOrWhiteSpace(e.Body))
                    message += ": " + e.Body;
            }

            AppendKbMessage(message);
            SetKbStatus(message);
        }

        private static string FormatKbDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds + "s";
            if (t.TotalSeconds >= 1) return (int)t.TotalSeconds + "s";
            return (int)t.TotalMilliseconds + "ms";
        }

        private void AppendKbMessage(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendKbMessage(text)));
                return;
            }
            KbMessagesBox.AppendText(text + Environment.NewLine);
            if (KbAutoScrollCheckBox.IsChecked == true)
                KbMessagesBox.ScrollToEnd();
        }

        private void OnRobotImportMessage(object? sender, string message)
        {
            AppendKbMessage(message);
        }

        private void OnTokenUsageUpdated(object? sender, TokenUsage usage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnTokenUsageUpdated(sender, usage)));
                return;
            }
            UpdateTokenUsageDisplay();
        }

        private void UpdateTokenUsageDisplay()
        {
            var provider = _tokenUsage;
            if (provider == null)
            {
                TokenUsageText.Text = "Context window: —";
                return;
            }
            var usage = provider.TotalTokenUsage;
            TokenUsageText.Text = "Context window: " + usage.TotalTokens.ToString("N0") +
                                  " / " + provider.ContextWindowSize.ToString("N0") + " tokens";
        }

        private void ShowError(Exception ex)
        {
            AppendChat("Error: " + ex.Message);
            AppendChat(ex.ToString());
            SetOutput(ex.ToString());
        }

        private void ShowNotice(string message)
        {
            NoticeText.Text = message;
            NoticeBar.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        private void HookSolutionEvents()
        {
            try
            {
                _dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (_dte == null) return;
                var solEvents = _dte.Events.SolutionEvents;
                solEvents.Opened += () => UpdateCreateRobotVisibility();
                solEvents.AfterClosing += () => UpdateCreateRobotVisibility();
                solEvents.ProjectAdded += p => UpdateCreateRobotVisibility();
                solEvents.ProjectRemoved += p => UpdateCreateRobotVisibility();
            }
            catch
            {
                // Solution events unavailable — visibility will be refreshed on load / lazily.
            }
        }

        private void UpdateCreateRobotVisibility()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateCreateRobotVisibility));
                return;
            }
            bool visible;
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                visible = dte?.Solution == null || !dte.Solution.IsOpen;
            }
            catch
            {
                visible = true;
            }
            CreateRobotPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            RestorePackagesPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowImportProgress(string status)
        {
            ImportStatusText.Text = status;
            ImportProgressPanel.Visibility = Visibility.Visible;
        }

        private void SetImportStatus(string status)
        {
            ImportStatusText.Text = status;
        }

        private void HideImportProgress()
        {
            ImportProgressPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Returns the open solution's directory, or null when no solution is open.</summary>
        private string? GetOpenSolutionDirectory()
        {
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;
                var slnPath = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(slnPath) ? null : Path.GetDirectoryName(slnPath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Copies the spec file, unchanged, into the docs folder at the solution root.</summary>
        private static void SaveImportedSpec(string sourcePath, string solutionDir)
        {
            var docsDir = Path.Combine(solutionDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.Copy(sourcePath, Path.Combine(docsDir, Path.GetFileName(sourcePath)), overwrite: true);
        }

#pragma warning disable VSTHRD100 // WPF event handlers are async void
        private async void OnSend(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Enter — send; Ctrl+Enter — newline (handled by the TextBox itself)
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async void OnImportSpec(object sender, RoutedEventArgs e)
        {
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Spec import is only available when a solution is open.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Select spec",
                Filter = "Specs (*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md)|*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            if (FileLockDetector.IsFileLocked(dlg.FileName))
            {
                ShowNotice(SpecLockedMessage);
                return;
            }

            ShowImportProgress("Reading file…");
            try
            {
                var docsDir = Path.Combine(solutionDir, "docs");
                Directory.CreateDirectory(docsDir);

                // Unchanged copy of the original spec file.
                SaveImportedSpec(dlg.FileName, solutionDir);

                var spec = new OpenXmlDocumentReader().Read(dlg.FileName);

                // 1. Structured spec.md (headings, lists, tables).
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "spec.md"), spec.MarkdownText, "spec.md");

                // 2. Decompose the spec into tasks.
                SetImportStatus("Parsing tasks (AI)…");
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText);
                spec.Tasks = tasks.ToList();

                // 3. Structured tasks.md.
                var tasksMarkdown = MarkdownContent.TasksToMarkdown(spec);
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "tasks.md"), tasksMarkdown, "tasks.md");

                _currentSpec = spec;
                HideNotice();

                var sb = new StringBuilder();
                sb.AppendLine("Spec: " + spec.Name);
                sb.AppendLine();
                sb.AppendLine(spec.RawText);
                sb.AppendLine();
                sb.AppendLine("Tasks: " + spec.Tasks.Count);
                foreach (var t in spec.Tasks)
                    sb.AppendLine("- " + (t.Action ?? "") + " → " + (t.Target ?? "") + " [" + (t.ActivityType ?? "") + "]");
                SetOutput(sb.ToString());

                AppendChat("Imported spec: " + spec.Name + " (tasks: " + spec.Tasks.Count + ")");
            }
            catch (Exception ex) when (FileLockDetector.IsFileLocked(ex))
            {
                ShowNotice(SpecLockedMessage);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                HideImportProgress();
            }
        }

        /// <summary>
        /// Saves an md file. If it already exists — offers full overwrite,
        /// partial fix via diff, or leaving it unchanged.
        /// </summary>
        private void WriteMarkdownWithConfirm(string path, string content, string displayName)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, content);
                AppendChat("Created " + displayName + ".");
                return;
            }

            var choice = MessageBox.Show(
                "File " + displayName + " already exists.\n\n" +
                "Yes — overwrite it entirely.\n" +
                "No — find discrepancies and propose a partial fix.\n" +
                "Cancel — leave it unchanged.",
                "File already exists",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    File.WriteAllText(path, content);
                    AppendChat(displayName + " overwritten.");
                    break;

                case MessageBoxResult.No:
                    var existing = File.ReadAllText(path);
                    var diff = TextDiff.Diff(existing, content);
                    var summary = diff.HasChanges ? diff.Summary : "No discrepancies found.";
                    SetOutput(summary);
                    if (diff.HasChanges)
                    {
                        var apply = MessageBox.Show(
                            summary + "\n\nApply the fixes (overwrite)?",
                            "Partial fix — " + displayName,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (apply == MessageBoxResult.Yes)
                        {
                            File.WriteAllText(path, content);
                            AppendChat("Fixes applied to " + displayName + ".");
                        }
                        else
                        {
                            AppendChat(displayName + " left unchanged.");
                        }
                    }
                    else
                    {
                        AppendChat(displayName + " has not changed.");
                    }
                    break;

                default: // Cancel
                    AppendChat(displayName + " left unchanged.");
                    break;
            }
        }

        private async void OnFindSimilar(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Import a spec first."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var matches = new List<SimilarTaskMatch>();
                foreach (var task in _currentSpec.Tasks)
                    matches.AddRange(await kb.FindSimilarTasksAsync(task, 3));

                var sb = new StringBuilder();
                foreach (var m in matches)
                    sb.AppendLine("[" + m.Score.ToString("P0") + "] " + m.Solution.Name + " — " + string.Join("; ", m.Reasons));
                SetOutput(sb.Length == 0 ? "No similar tasks found." : sb.ToString());
                AppendChat("Similar tasks found: " + matches.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnGenerateRobot(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Import a spec first."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var pipeline = new RpaPipelineService(new OpenXmlDocumentReader(), _ai, kb, new RobotGenerator());
                var result = await pipeline.GenerateRobotFromSpecAsync(_currentSpec.SourcePath);

                var dir = Path.GetDirectoryName(_currentSpec.SourcePath);
                var baseName = Path.GetFileNameWithoutExtension(_currentSpec.SourcePath);
                var xamlPath = Path.Combine(dir, baseName + ".xaml");
                var csPath = Path.Combine(dir, baseName + ".Actions.cs");
                File.WriteAllText(xamlPath, result.Xaml);
                File.WriteAllText(csPath, result.CSharpCode);

                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendChat("Robot generated and saved next to the spec: " + xamlPath + ", " + csPath);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnFormKnowledgeBase(object sender, RoutedEventArgs e)
        {
            await RunKnowledgeBaseImport(resume: false);
        }

        private async void OnRetryKnowledgeBaseImport(object sender, RoutedEventArgs e)
        {
            await RunKnowledgeBaseImport(resume: true);
        }

        private async Task RunKnowledgeBaseImport(bool resume)
        {
            var kbPath = RequireKbPathForKnowledgeBase();
            if (kbPath == null) return;

            var robotsRoot = (RobotsRootPathBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(robotsRoot))
            {
                SetKbStatus("Specify the robot solutions directory in the \"Settings\" tab first.");
                return;
            }
            if (!Directory.Exists(robotsRoot))
            {
                SetKbStatus("Robot solutions directory does not exist: " + robotsRoot);
                return;
            }

            KbRetryButton.Visibility = Visibility.Collapsed;
            _kbImportPaused = false;
            _kbImportCts = new CancellationTokenSource();
            SetKbImportRunning(true);
            SetKbStatus(resume ? "Resuming import…" : "Importing robots…");
            KbProgressBar.Value = 0;
            KbProgressBar.Visibility = Visibility.Visible;
            var service = new RobotKnowledgeBaseImportService(_ai);
            service.MessageReported += OnRobotImportMessage;
            try
            {
                var progress = new Progress<string>(SetKbStatus);
                var progressValue = new Progress<double>(SetKbProgress);
                var result = await service.ImportFromRobotsAsync(
                    robotsRoot, kbPath, progress, progressValue, _kbImportCts.Token, () => _kbImportPaused, resume);

                var sb = new StringBuilder();
                sb.AppendLine("Robots processed: " + result.RobotsProcessed);
                sb.AppendLine("Spec files written: " + result.SpecFilesWritten);
                sb.AppendLine("Task files written: " + result.TaskFilesWritten);
                sb.AppendLine("Tasks added to index: " + result.TasksAdded);
                foreach (var warning in result.Warnings)
                    sb.AppendLine("Warning: " + warning);
                SetOutput(sb.ToString());
                AppendChat("Knowledge base imported from robots: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).");
                SetKbStatus("Import finished: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).");
            }
            catch (OperationCanceledException)
            {
                AppendKbMessage("Import stopped by user.");
                SetKbStatus("Import stopped.");
            }
            catch (Exception ex)
            {
                ShowError(ex);
                SetKbStatus("Import failed.\n\n" + ex.ToString());
                KbRetryButton.Visibility = Visibility.Visible;
            }
            finally
            {
                service.MessageReported -= OnRobotImportMessage;
                KbProgressBar.Visibility = Visibility.Collapsed;
                SetKbImportRunning(false);
                _kbImportCts.Dispose();
                _kbImportCts = null;
            }
        }

        private void SetKbImportRunning(bool running)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbImportRunning(running)));
                return;
            }
            KbImportButton.IsEnabled = !running;
            KbPauseButton.IsEnabled = running;
            KbStopButton.IsEnabled = running;
            if (!running)
            {
                KbPauseButton.Content = "Pause";
                _kbImportPaused = false;
            }
        }

        private void OnPauseKnowledgeBaseImport(object sender, RoutedEventArgs e)
        {
            if (_kbImportCts == null) return;
            _kbImportPaused = !_kbImportPaused;
            if (_kbImportPaused)
            {
                KbPauseButton.Content = "Resume";
                SetKbStatus("Paused — click Resume to continue.");
            }
            else
            {
                KbPauseButton.Content = "Pause";
                SetKbStatus("Importing robots…");
            }
        }

        private void OnStopKnowledgeBaseImport(object sender, RoutedEventArgs e)
        {
            _kbImportCts?.Cancel();
            KbStopButton.IsEnabled = false;
            SetKbStatus("Stopping…");
        }

        private async void OnBuildRelease(object sender, RoutedEventArgs e)
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                {
                    ShowNotice("No open solution. Open a solution and retry.");
                    return;
                }
                await _buildFeedback.BuildReleaseAsync();
                AppendChat("Release build started.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Log files",
                Filter = "Logs (*.log;*.txt;*.json)|*.log;*.txt;*.json|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = new List<string>();
                foreach (var f in dlg.FileNames) lines.AddRange(File.ReadAllLines(f));
                var analysis = new LogAnalyzer().Analyze(lines);
                SetOutput(analysis.Summary);
                foreach (var err in analysis.Errors.Take(20)) AppendChat("ERR: " + err.Message);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnConvertUiPath(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "UiPath robot",
                Filter = "UiPath workflow (*.xaml)|*.xaml|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var xaml = File.ReadAllText(dlg.FileName);
                var result = new UiPathToWfConverter(new RobotGenerator()).Convert(xaml);
                var dir = Path.GetDirectoryName(dlg.FileName);
                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.xaml"), result.Xaml);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.cs"), result.CSharpCode);
                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendChat("Conversion completed. Warnings: " + result.Warnings.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }
        private async void OnRestorePackages(object sender, RoutedEventArgs e)
        {
            try
            {
                await _robotTemplate.RunPackageManagerCommandAsync(
                    "Update-Package -Reinstall",
                    new Progress<string>(AppendChat));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnCreateRobot(object sender, RoutedEventArgs e)
        {
            var number = (RobotNumberBox?.Text ?? "").Trim();
            if (number.Length == 0 || !number.All(char.IsDigit))
            {
                AppendChat("Specify the robot number (digits only).");
                return;
            }

            var solutionName = "RPA-" + number;
            try
            {
                var result = await _robotTemplate.CreateRobotAsync(
                    solutionName, _settings.RobotsRootPath, _settings.RobotTemplateName,
                    progress: new Progress<string>(AppendChat));
                if (result.Success)
                    AppendChat("Robot created: " + result.SolutionPath);
                else
                    AppendChat("Warning: " + result.Message);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void OnRobotNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(c => !char.IsDigit(c));
        }

        private async void OnRefreshKnowledgeBase(object sender, RoutedEventArgs e)
        {
            var kbPath = RequireKbPathForKnowledgeBase();
            if (kbPath == null) return;
            try
            {
                var kb = new FileKnowledgeBase(kbPath);
                var records = await kb.GetAllAsync();
                var items = records.Select(r => (r.Name ?? "<unnamed>") + " — tasks: " + (r.Tasks?.Count ?? 0)).ToList();
                KbListBox.ItemsSource = items;
                KbStatusText.Text = "Solutions in the knowledge base: " + records.Count;
            }
            catch (Exception ex) { ShowError(ex); }
        }

#pragma warning restore VSTHRD100

        private void StartChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StartChatStatus));
                return;
            }
            _chatStartedAt = DateTime.UtcNow;
            _chatStatusTimer.Tick -= OnChatStatusTick;
            _chatStatusTimer.Tick += OnChatStatusTick;
            _chatStatusTimer.Start();
            OnChatStatusTick(null, EventArgs.Empty);
        }

        private void OnChatStatusTick(object? sender, EventArgs e)
        {
            var elapsed = (int)(DateTime.UtcNow - _chatStartedAt).TotalSeconds;
            var phase = elapsed < 2 ? "Sending request…" : "Thinking…";
            AgentStatusText.Text = phase + " (" + elapsed + " s)";
            AgentStatusText.Visibility = Visibility.Visible;
            AgentProgress.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;
        }

        private void StopChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StopChatStatus));
                return;
            }
            _chatStatusTimer.Stop();
            _chatStatusTimer.Tick -= OnChatStatusTick;
            AgentStatusText.Text = "";
            AgentStatusText.Visibility = Visibility.Collapsed;
            AgentProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = true;
        }

        private void ShowChatStatusError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowChatStatusError(message)));
                return;
            }
            _chatStatusTimer.Stop();
            _chatStatusTimer.Tick -= OnChatStatusTick;
            AgentStatusText.Text = "Error: " + message;
            AgentStatusText.Visibility = Visibility.Visible;
            AgentProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = true;
        }

        private void OnCancelSend(object sender, RoutedEventArgs e)
        {
            _chatCts?.Cancel();
        }

        private async Task SendMessageAsync()
        {
            if (_chatCts != null) return; // already awaiting the agent's reply

            var text = InputBox.Text.Trim();
            if (text.Length == 0) return;
            InputBox.Clear();
            AppendChatMessage(new ChatMessage { Role = "user", Text = text });

            var cts = new CancellationTokenSource();
            _chatCts = cts;
            SendButton.IsEnabled = false;
            StartChatStatus();
            try
            {
                var response = await _ai.ChatAsync(text, cts.Token);
                AppendChatMessage(new ChatMessage { Role = "agent", Text = response.Answer, Reasoning = response.Reasoning });
                StopChatStatus();
            }
            catch (OperationCanceledException)
            {
                AppendChat("Request cancelled.");
                StopChatStatus();
            }
            catch (Exception ex)
            {
                AppendChat("Error: " + ex.Message);
                ShowChatStatusError(ex.Message);
            }
            finally
            {
                _chatCts = null;
                cts.Dispose();
            }
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.KnowledgeBasePath = KbPathBox.Text.Trim();
            _settings.RobotsRootPath = RobotsRootPathBox.Text.Trim();
            _settings.RobotTemplateName = RobotTemplateNameBox.Text.Trim();
            _settings.RulesPath = RulesPathBox.Text.Trim();
            _settings.ContextWindowSize = ParseContextWindowSize();
            SettingsStore.Save(_settings);
            RecreateAiProvider();
            AppendChat("Settings saved. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
        }

        private int ParseContextWindowSize()
        {
            if (int.TryParse((ContextWindowBox?.Text ?? "").Trim(), out var value) && value > 0)
                return value;
            return 409600;
        }
    }
}