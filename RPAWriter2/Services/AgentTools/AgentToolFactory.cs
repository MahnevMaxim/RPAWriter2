using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services.AgentTools
{
    /// <summary>
    /// Builds the agent tool registry by wrapping the existing services and UI state
    /// (current spec, KB path, robots root, template name, solution directory).
    /// </summary>
    public sealed class AgentToolFactory
    {
        private readonly IAiProvider _ai;
        private readonly Func<SpecModel?> _getSpec;
        private readonly Func<string?> _getKbPath;
        private readonly Func<string?> _getRobotsRootPath;
        private readonly Func<string?> _getRobotTemplateName;
        private readonly Func<string?> _getSolutionDir;
        private readonly Func<DTE2?> _getDte;
        private readonly IBuildFeedbackService _buildFeedback;
        private readonly RobotTemplateService _robotTemplate;
        private readonly ShellCommandService _shell = new ShellCommandService();

        public AgentToolFactory(
            IAiProvider ai,
            Func<SpecModel?> getSpec,
            Func<string?> getKbPath,
            Func<string?> getRobotsRootPath,
            Func<string?> getRobotTemplateName,
            Func<string?> getSolutionDir,
            Func<DTE2?> getDte,
            IBuildFeedbackService buildFeedback,
            RobotTemplateService robotTemplate)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _getSpec = getSpec ?? throw new ArgumentNullException(nameof(getSpec));
            _getKbPath = getKbPath ?? throw new ArgumentNullException(nameof(getKbPath));
            _getRobotsRootPath = getRobotsRootPath ?? throw new ArgumentNullException(nameof(getRobotsRootPath));
            _getRobotTemplateName = getRobotTemplateName ?? throw new ArgumentNullException(nameof(getRobotTemplateName));
            _getSolutionDir = getSolutionDir ?? throw new ArgumentNullException(nameof(getSolutionDir));
            _getDte = getDte ?? throw new ArgumentNullException(nameof(getDte));
            _buildFeedback = buildFeedback ?? throw new ArgumentNullException(nameof(buildFeedback));
            _robotTemplate = robotTemplate ?? throw new ArgumentNullException(nameof(robotTemplate));
        }
        public AgentToolRegistry CreateRegistry()
        {
            var registry = new AgentToolRegistry();

            // ---- Read-only tools ----
            registry.Register(new AgentTool("read_spec", "Return the currently imported specification (name, steps, inputs, outputs, tasks, raw text).", "{}", mutates: false, ReadSpecAsync));
            registry.Register(new AgentTool("read_file", "Read a text file FROM DISK (saved content only) by absolute or solution-relative path. Does not reflect unsaved editor changes; to read the open editor tab use open_file then read_open_document.", "{\"path\":\"string\"}", false, ReadFileAsync));
            registry.Register(new AgentTool("read_robot_sources", "List the robot source files (.xaml/.cs) under a directory with their contents.", "{\"directory\":\"string\"}", false, ReadRobotSourcesAsync));
            registry.Register(new AgentTool("analyze_spec", "Analyze the imported spec into a summary and tasks.", "{}", false, AnalyzeSpecAsync));
            registry.Register(new AgentTool("decompose_tasks", "Decompose a text into atomic tasks.", "{\"text\":\"string\"}", false, DecomposeTasksAsync));
            registry.Register(new AgentTool("reconstruct_spec", "Reconstruct a spec from robot sources under a directory.", "{\"robotName\":\"string\",\"directory\":\"string\"}", false, ReconstructSpecAsync));
            registry.Register(new AgentTool("map_task_to_solution", "Locate the solution for a task in robot sources.", "{\"task\":{\"action\":\"string\",\"target\":\"string\",\"description\":\"string\"},\"directory\":\"string\"}", false, MapTaskToSolutionAsync));
            registry.Register(new AgentTool("find_similar", "Find similar tasks in the knowledge base.", "{\"action\":\"string\",\"target\":\"string\",\"description\":\"string\",\"topK\":\"number\"}", false, FindSimilarAsync));
            registry.Register(new AgentTool("list_kb", "List the solutions in the knowledge base.", "{}", false, ListKbAsync));
            registry.Register(new AgentTool("read_errors", "Read compiler errors from the Visual Studio Error List.", "{}", false, ReadErrorsAsync));
            registry.Register(new AgentTool("read_output", "Read the Visual Studio Output window text.", "{}", false, ReadOutputAsync));
            registry.Register(new AgentTool("open_file", "Open a file in Visual Studio and activate its tab. Use before read_open_document when the file's tab is not open yet. Does not read the file content from disk.", "{\"path\":\"string\"}", false, OpenFileAsync));
            registry.Register(new AgentTool("read_open_document", "Read the content currently shown in an open editor tab (includes unsaved changes). Provide \"path\" to read a specific open tab; omit it to read the active document. Never reads from disk. Prefer this over read_file for files being edited.", "{\"path\":\"string\"}", false, ReadOpenDocumentAsync, ReadOpenDocumentDisplayAsync));
            registry.Register(new AgentTool("analyze_logs", "Analyze log files at the given paths.", "{\"paths\":[\"string\"]}", false, AnalyzeLogsAsync));
            registry.Register(new AgentTool("read_memory", "Read the project memory bank (memory-bank/*.md) to restore long-term context.", "{}", false, ReadMemoryAsync));

            // ---- Mutating tools (Act only) ----
            registry.Register(new AgentTool("generate_robot", "Generate a robot (XAML/C#) from the imported spec and save the artifacts next to the spec.", "{}", mutates: true, GenerateRobotAsync));
            registry.Register(new AgentTool("write_file", "Write a text file (create or overwrite) by absolute or solution-relative path.", "{\"path\":\"string\",\"content\":\"string\"}", true, WriteFileAsync));
            registry.Register(new AgentTool("apply_fix", "Replace the content of an existing file with corrected content. Edits through the open Visual Studio editor tab (single Undo step) and saves it; falls back to a disk write when the file cannot be opened in the editor.", "{\"path\":\"string\",\"content\":\"string\"}", true, ApplyFixAsync));
            registry.Register(new AgentTool("edit_open_document", "Replace the text of an open editor tab directly in the Visual Studio editor buffer (not a raw disk write), then save it. Provide \"path\" to target a specific file (opens it if needed), or omit \"path\" to edit the active document. Applied as one Undo step.", "{\"path\":\"string\",\"content\":\"string\"}", true, EditOpenDocumentAsync, EditOpenDocumentDisplayAsync));
            registry.Register(new AgentTool("create_robot", "Create a robot solution (RPA-<number>) from the configured template.", "{\"number\":\"string\"}", true, CreateRobotAsync));
            registry.Register(new AgentTool("build", "Run a Release build of the open solution.", "{}", true, BuildAsync));
            registry.Register(new AgentTool("convert_uipath", "Convert a UiPath XAML file to Workflow Foundation and write the artifacts.", "{\"path\":\"string\"}", true, ConvertUiPathAsync));
            registry.Register(new AgentTool("kb_import", "Import the knowledge base from the robots directory.", "{\"mode\":\"override|continue\"}", true, KbImportAsync));
            registry.Register(new AgentTool("kb_add", "Add a record to the knowledge base.", "{\"name\":\"string\",\"task\":\"string\",\"xamlFragment\":\"string\",\"csharpCode\":\"string\"}", true, KbAddAsync));
            registry.Register(new AgentTool("update_memory", "Write a project memory bank file (activeContext.md, progress.md, etc.).", "{\"file\":\"string\",\"content\":\"string\"}", true, UpdateMemoryAsync));

            // ---- Read-only navigation / inspection tools (permission-gated) ----
            registry.Register(new AgentTool("list_directory", "List files and subdirectories in a directory (absolute or solution-relative path).", "{\"path\":\"string\",\"recursive\":\"boolean\"}", false, ListDirectoryAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("search_files", "Search text files under a directory for a literal or regex pattern; returns matching file paths with line snippets.", "{\"directory\":\"string\",\"pattern\":\"string\",\"useRegex\":\"boolean\"}", false, SearchFilesAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("git_status", "Show the git working-tree status of the open solution's repository.", "{}", false, GitStatusAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("git_diff", "Show the uncommitted git diff of the open solution's repository (optionally for one path).", "{\"path\":\"string\"}", false, GitDiffAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("web_fetch", "Fetch the content of a web URL (HTTP GET) and return the response text.", "{\"url\":\"string\"}", false, WebFetchAsync, permission: AgentPermissions.WebFetch));

            // ---- Mutating tools (Act only, permission-gated) ----
            registry.Register(new AgentTool("run_command", "Run an arbitrary PowerShell command in the solution directory and return its output. Use for dotnet build/test, git and environment inspection.", "{\"command\":\"string\"}", true, RunCommandAsync, permission: AgentPermissions.ExecuteCommand));
            registry.Register(new AgentTool("download_file", "Download a file from a URL and save it to disk.", "{\"url\":\"string\",\"path\":\"string\"}", true, DownloadFileAsync, permission: AgentPermissions.WebFetch));

            return registry;
        }

        private Task<string> ReadSpecAsync(JObject args, CancellationToken ct)
        {
            var spec = _getSpec();
            if (spec == null) return Task.FromResult("No spec is imported. Import a spec first.");

            var sb = new StringBuilder();
            sb.AppendLine("Name: " + (spec.Name ?? ""));
            if (!string.IsNullOrWhiteSpace(spec.Description)) sb.AppendLine("Description: " + spec.Description);
            if (spec.Steps != null && spec.Steps.Count > 0)
            {
                sb.AppendLine("Steps:");
                foreach (var s in spec.Steps) sb.AppendLine("- " + s);
            }
            if (spec.Inputs != null && spec.Inputs.Count > 0)
            {
                sb.AppendLine("Inputs:");
                foreach (var s in spec.Inputs) sb.AppendLine("- " + s);
            }
            if (spec.Outputs != null && spec.Outputs.Count > 0)
            {
                sb.AppendLine("Outputs:");
                foreach (var s in spec.Outputs) sb.AppendLine("- " + s);
            }
            sb.AppendLine("Tasks:");
            foreach (var t in spec.Tasks ?? new List<TaskDescriptor>())
                sb.AppendLine("- " + (t?.Action ?? "") + " → " + (t?.Target ?? "") + " [" + (t?.ActivityType ?? "") + "]");
            sb.AppendLine();
            sb.AppendLine("Raw text:");
            sb.AppendLine(spec.RawText ?? "");
            return Task.FromResult(sb.ToString());
        }

        private Task<string> ReadFileAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p)) return Task.FromResult("Missing \"path\" argument.");
            if (!File.Exists(p)) return Task.FromResult("File not found: " + p);
            try { return Task.FromResult(Truncate(File.ReadAllText(p))); }
            catch (Exception ex) { return Task.FromResult("Failed to read file: " + ex.Message); }
        }

        private async Task<string> OpenFileAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p)) return "Missing \"path\" argument.";

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _getDte();
            if (dte == null) return "Visual Studio is not available.";

            try
            {
                var doc = await VsDocumentService.OpenOrActivateAsync(dte, p).ConfigureAwait(true);
                return doc == null
                    ? "Could not open the file in Visual Studio: " + p
                    : "Opened in Visual Studio: " + p;
            }
            catch (Exception ex)
            {
                return "Could not open the file in Visual Studio: " + ex.Message;
            }
        }

        private async Task<string> ReadOpenDocumentAsync(JObject args, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var resolved = ResolveOpenDocument(args);
            var doc = resolved.Doc;
            if (doc == null) return resolved.Error ?? "The document is not open in the editor.";

            await VsDocumentService.WaitForDocumentReadyAsync(doc).ConfigureAwait(true);
            var text = VsDocumentService.ReadDocumentText(doc);
            if (text == null) return "The document is not ready or not a text document: " + (resolved.Path ?? doc.Name);
            return Truncate(text);
        }

        private async Task<string> ReadOpenDocumentDisplayAsync(JObject args, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var resolved = ResolveOpenDocument(args);
            var doc = resolved.Doc;
            if (doc == null) return resolved.Error ?? "Document not open.";
            return resolved.Path ?? doc.Name;
        }

        private (Document? Doc, string? Path, string? Error) ResolveOpenDocument(JObject args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var rawPath = args["path"]?.Value<string>() ?? "";
            var p = string.IsNullOrWhiteSpace(rawPath) ? null : ResolvePath(rawPath);

            var dte = _getDte();
            if (dte == null) return (null, null, "Visual Studio is not available.");

            Document? doc;
            if (p != null)
            {
                doc = VsDocumentService.FindOpenDocument(dte, p);
                if (doc == null) return (null, p, "The file is not open in the editor: " + p);
            }
            else
            {
                doc = dte.ActiveDocument;
                if (doc == null) return (null, null, "No document is active in the editor.");
            }

            var fullPath = doc.FullName;
            if (string.IsNullOrWhiteSpace(fullPath)) fullPath = p;
            return (doc, fullPath, null);
        }

        private Task<string> ReadRobotSourcesAsync(JObject args, CancellationToken ct)
        {
            var dir = ResolvePath(args["directory"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Task.FromResult("Directory not found: " + dir);

            var sb = new StringBuilder();
            foreach (var f in EnumerateSources(dir))
            {
                try
                {
                    sb.AppendLine("FILE: " + MakeRelative(dir, f));
                    sb.AppendLine(Truncate(File.ReadAllText(f)));
                    sb.AppendLine();
                }
                catch { /* skip unreadable files */ }
            }
            return Task.FromResult(sb.Length == 0 ? "No source files found under " + dir : sb.ToString());
        }

        private async Task<string> AnalyzeSpecAsync(JObject args, CancellationToken ct)
        {
            var spec = _getSpec();
            if (spec == null) return "No spec is imported.";
            var analysis = await _ai.AnalyzeSpecAsync(spec, ct).ConfigureAwait(false);
            return JsonConvert.SerializeObject(analysis, Formatting.Indented);
        }

        private async Task<string> DecomposeTasksAsync(JObject args, CancellationToken ct)
        {
            var text = args["text"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return "Missing \"text\" argument.";
            var tasks = await _ai.DecomposeTasksAsync(text, ct).ConfigureAwait(false);
            return JsonConvert.SerializeObject(tasks, Formatting.Indented);
        }

        private async Task<string> ReconstructSpecAsync(JObject args, CancellationToken ct)
        {
            var name = args["robotName"]?.Value<string>() ?? "Robot";
            var dir = ResolvePath(args["directory"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return "Directory not found: " + dir;

            var files = ReadRobotSourceFiles(dir);
            var spec = await _ai.ReconstructSpecAsync(name, files, ct, RobotPlatform.Mwf).ConfigureAwait(false);
            return JsonConvert.SerializeObject(spec, Formatting.Indented);
        }

        private async Task<string> MapTaskToSolutionAsync(JObject args, CancellationToken ct)
        {
            var dir = ResolvePath(args["directory"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return "Directory not found: " + dir;

            var taskObj = args["task"] as JObject;
            var task = new TaskDescriptor
            {
                Action = taskObj?["action"]?.Value<string>() ?? "",
                Target = taskObj?["target"]?.Value<string>() ?? "",
                Description = taskObj?["description"]?.Value<string>() ?? ""
            };

            var files = ReadRobotSourceFiles(dir);
            var solution = await _ai.MapTaskToSolutionAsync(task, files, ct, RobotPlatform.Mwf).ConfigureAwait(false);
            return JsonConvert.SerializeObject(solution, Formatting.Indented);
        }

        private async Task<string> FindSimilarAsync(JObject args, CancellationToken ct)
        {
            var kbPath = _getKbPath();
            if (string.IsNullOrWhiteSpace(kbPath)) return "Knowledge base directory is not configured.";

            var task = new TaskDescriptor
            {
                Action = args["action"]?.Value<string>() ?? "",
                Target = args["target"]?.Value<string>() ?? "",
                Description = args["description"]?.Value<string>() ?? ""
            };
            var topK = Math.Max(1, args["topK"]?.Value<int>() ?? 3);

            var kb = new FileKnowledgeBase(kbPath);
            var matches = await kb.FindSimilarTasksAsync(task, topK, ct).ConfigureAwait(false);
            if (matches == null || matches.Count == 0) return "No similar tasks found.";

            var sb = new StringBuilder();
            foreach (var m in matches)
                sb.AppendLine("[" + m.Score.ToString("P0") + "] " + (m.Solution?.Name ?? "") + " — " + string.Join("; ", m.Reasons ?? new List<string>()));
            return sb.ToString();
        }

        private async Task<string> ListKbAsync(JObject args, CancellationToken ct)
        {
            var kbPath = _getKbPath();
            if (string.IsNullOrWhiteSpace(kbPath)) return "Knowledge base directory is not configured.";

            var kb = new FileKnowledgeBase(kbPath);
            var records = await kb.GetAllAsync(ct).ConfigureAwait(false);
            if (records == null || records.Count == 0) return "Knowledge base is empty.";

            var sb = new StringBuilder();
            foreach (var r in records)
                sb.AppendLine("- " + (r.Name ?? "<unnamed>") + " (tasks: " + (r.Tasks?.Count ?? 0) + ")");
            return sb.ToString();
        }

        private async Task<string> ReadErrorsAsync(JObject args, CancellationToken ct)
        {
            var errors = await _buildFeedback.GetCompilationErrorsAsync(ct).ConfigureAwait(false);
            var all = (errors ?? new List<BuildError>()).Where(e => e != null).ToList();
            if (all.Count == 0)
                return await BuildNoErrorsSummaryAsync(ct).ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine("Error List (" + all.Count + " entries):");
            foreach (var e in all)
            {
                var severity = string.IsNullOrWhiteSpace(e.Severity) ? "Error" : e.Severity;
                sb.Append("[").Append(severity).Append("] ");
                if (!string.IsNullOrWhiteSpace(e.Code)) sb.Append(e.Code).Append(": ");
                sb.Append(e.Message ?? string.Empty);

                var location = FormatToolLocation(e);
                if (!string.IsNullOrEmpty(location)) sb.Append(" (").Append(location).Append(")");
                if (!string.IsNullOrWhiteSpace(e.Project)) sb.Append(" [Project: ").Append(e.Project).Append("]");
                var level = FormatToolLevel(e);
                if (!string.IsNullOrEmpty(level)) sb.Append(" [").Append(level).Append("]");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string FormatToolLocation(BuildError e)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.File)) parts.Add(e.File);
            if (e.Line > 0) parts.Add("line " + e.Line);
            if (e.Column > 0) parts.Add("col " + e.Column);
            return string.Join(", ", parts);
        }

        private static string FormatToolLevel(BuildError e)
        {
            if (!string.IsNullOrWhiteSpace(e.ErrorLevelName))
                return e.ErrorLevelName + " = " + e.ErrorLevel;
            if (e.ErrorLevel != 0)
                return "level " + e.ErrorLevel;
            return string.Empty;
        }

        private async Task<string> BuildNoErrorsSummaryAsync(CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine("The Visual Studio Error List is empty.");

            string output;
            try
            {
                output = await _buildFeedback.GetOutputWindowTextAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                output = "Output window unavailable: " + ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                sb.AppendLine();
                sb.AppendLine("Build output:");
                sb.AppendLine(Truncate(output));
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> ReadOutputAsync(JObject args, CancellationToken ct)
        {
            var text = await _buildFeedback.GetOutputWindowTextAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? "Output window is empty." : Truncate(text);
        }

        private Task<string> AnalyzeLogsAsync(JObject args, CancellationToken ct)
        {
            var paths = new List<string>();
            var arr = args["paths"] as JArray;
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var s = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s)) paths.Add(s);
                }
            }
            if (paths.Count == 0) return Task.FromResult("Missing \"paths\" argument.");

            var lines = new List<string>();
            foreach (var raw in paths)
            {
                var full = ResolvePath(raw);
                if (!File.Exists(full)) { lines.Add("(missing file: " + full + ")"); continue; }
                try { lines.AddRange(File.ReadAllLines(full)); }
                catch (Exception ex) { lines.Add("(read error " + full + ": " + ex.Message + ")"); }
            }

            var analysis = new LogAnalyzer().Analyze(lines);
            var sb = new StringBuilder();
            sb.AppendLine(analysis.Summary);
            foreach (var e in analysis.Errors.Take(50)) sb.AppendLine("ERR: " + (e.Message ?? ""));
            foreach (var w in analysis.Warnings.Take(50)) sb.AppendLine("WARN: " + (w.Message ?? ""));
            return Task.FromResult(sb.ToString());
        }

        private Task<string> ReadMemoryAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root))
                return Task.FromResult("No open solution directory — memory bank unavailable.");

            try
            {
                MemoryBankService.EnsureInitialized(root);
                var text = MemoryBankService.ReadAll(root);
                return Task.FromResult(string.IsNullOrWhiteSpace(text) ? "(memory bank is empty)" : Truncate(text, 40000));
            }
            catch (Exception ex)
            {
                return Task.FromResult("Failed to read memory bank: " + ex.Message);
            }
        }

        private Task<string> UpdateMemoryAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root))
                return Task.FromResult("No open solution directory — memory bank unavailable.");

            var file = args["file"]?.Value<string>() ?? "";
            var content = args["content"]?.Value<string>() ?? "";
            if (!MemoryBankService.IsAllowed(file))
                return Task.FromResult("Invalid memory bank file. Use one of: " + string.Join(", ", MemoryBankFiles.Core));

            try
            {
                MemoryBankService.EnsureInitialized(root);
                MemoryBankService.WriteFile(root, file, content);
                return Task.FromResult("Memory bank updated: " + file);
            }
            catch (Exception ex)
            {
                return Task.FromResult("Failed to update memory bank: " + ex.Message);
            }
        }

        private Task<string> ListDirectoryAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p))
                return Task.FromResult("Directory not found: " + p);

            var recursive = args["recursive"]?.Value<bool>() ?? false;
            var sb = new StringBuilder();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(p))
                    sb.AppendLine("[dir] " + MakeRelative(p, dir));
                foreach (var file in Directory.EnumerateFiles(p, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    sb.AppendLine("[file] " + MakeRelative(p, file));
            }
            catch (Exception ex)
            {
                return Task.FromResult("Failed to list directory: " + ex.Message);
            }
            return Task.FromResult(sb.Length == 0 ? "(empty directory)" : Truncate(sb.ToString()));
        }

        private Task<string> SearchFilesAsync(JObject args, CancellationToken ct)
        {
            var dir = ResolvePath(args["directory"]?.Value<string>() ?? "");
            var pattern = args["pattern"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Task.FromResult("Directory not found: " + dir);
            if (string.IsNullOrWhiteSpace(pattern))
                return Task.FromResult("Missing \"pattern\" argument.");

            var useRegex = args["useRegex"]?.Value<bool>() ?? false;
            Regex? regex = null;
            if (useRegex)
            {
                try { regex = new Regex(pattern, RegexOptions.IgnoreCase); }
                catch (Exception ex) { return Task.FromResult("Invalid regex: " + ex.Message); }
            }

            var sb = new StringBuilder();
            try
            {
                foreach (var f in EnumerateTextFiles(dir))
                {
                    var lines = File.ReadAllLines(f);
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var match = regex != null ? regex.IsMatch(line) : line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (match)
                            sb.AppendLine(MakeRelative(dir, f) + ":" + (i + 1) + ": " + Truncate(line, 200));
                    }
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult("Search failed: " + ex.Message);
            }
            return Task.FromResult(sb.Length == 0 ? "No matches." : Truncate(sb.ToString()));
        }

        private async Task<string> RunCommandAsync(JObject args, CancellationToken ct)
        {
            var command = args["command"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(command)) return "Missing \"command\" argument.";
            var result = await _shell.RunAsync(command, _getSolutionDir() ?? "", ct).ConfigureAwait(false);
            return result.Success ? result.Output : "Command failed (exit " + result.ExitCode + "):\n" + result.Output;
        }

        private async Task<string> GitStatusAsync(JObject args, CancellationToken ct)
        {
            var dir = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(dir)) return "No open solution directory.";
            var result = await _shell.RunAsync("git status --porcelain --untracked-files=all", dir, ct).ConfigureAwait(false);
            if (!result.Success) return "git status failed: " + result.Output;
            return string.IsNullOrWhiteSpace(result.Output) || result.Output == "(no output)" ? "(clean)" : result.Output;
        }

        private async Task<string> GitDiffAsync(JObject args, CancellationToken ct)
        {
            var dir = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(dir)) return "No open solution directory.";
            var path = args["path"]?.Value<string>() ?? "";
            var command = string.IsNullOrWhiteSpace(path) ? "git diff" : "git diff -- " + path;
            var result = await _shell.RunAsync(command, dir, ct).ConfigureAwait(false);
            if (!result.Success) return "git diff failed: " + result.Output;
            return string.IsNullOrWhiteSpace(result.Output) || result.Output == "(no output)" ? "(no diff)" : result.Output;
        }

        private async Task<string> WebFetchAsync(JObject args, CancellationToken ct)
        {
            var url = args["url"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return "Missing \"url\" argument.";
            using (var wc = new WebClient())
            using (ct.Register(wc.CancelAsync))
            {
                try
                {
                    var text = await wc.DownloadStringTaskAsync(url).ConfigureAwait(false);
                    return Truncate(text);
                }
                catch (Exception ex)
                {
                    return "Web fetch failed: " + ex.Message;
                }
            }
        }

        private async Task<string> DownloadFileAsync(JObject args, CancellationToken ct)
        {
            var url = args["url"]?.Value<string>() ?? "";
            var path = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(url)) return "Missing \"url\" argument.";
            if (string.IsNullOrWhiteSpace(path)) return "Missing \"path\" argument.";
            using (var wc = new WebClient())
            using (ct.Register(wc.CancelAsync))
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                    await wc.DownloadFileTaskAsync(url, path).ConfigureAwait(false);
                    return "Downloaded to " + path;
                }
                catch (Exception ex)
                {
                    return "Download failed: " + ex.Message;
                }
            }
        }

        private static IEnumerable<string> EnumerateTextFiles(string dir)
        {
            try
            {
                var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".xaml", ".cs", ".txt", ".md", ".json", ".xml", ".config", ".csproj", ".sln", ".slnx", ".props", ".targets", ".vsct", ".yaml", ".yml"
                };
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => textExtensions.Contains(Path.GetExtension(f)))
                    .Take(500)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (Path.IsPathRooted(path)) return path;
            var dir = _getSolutionDir();
            return string.IsNullOrWhiteSpace(dir) ? Path.GetFullPath(path) : Path.Combine(dir, path);
        }

        private static string MakeRelative(string root, string full)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(full);
            if (fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(rootFull.Length);
            return Path.GetFileName(fullPath);
        }

        private static IEnumerable<string> EnumerateSources(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Take(200)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private List<RobotSourceFile> ReadRobotSourceFiles(string dir)
        {
            var result = new List<RobotSourceFile>();
            foreach (var f in EnumerateSources(dir))
            {
                try { result.Add(new RobotSourceFile { RelativePath = MakeRelative(dir, f), Content = File.ReadAllText(f) }); }
                catch { /* skip unreadable files */ }
            }
            return result;
        }

        private static string Truncate(string text, int max = 12000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "\n…(truncated)";
        }

        private async Task<string> GenerateRobotAsync(JObject args, CancellationToken ct)
        {
            var spec = _getSpec();
            if (spec == null) return "No spec is imported.";
            if (string.IsNullOrWhiteSpace(spec.SourcePath)) return "The imported spec has no source path.";

            var kbPath = _getKbPath();
            if (string.IsNullOrWhiteSpace(kbPath)) return "Knowledge base directory is not configured.";

            var kb = new FileKnowledgeBase(kbPath);
            var pipeline = new RpaPipelineService(new OpenXmlDocumentReader(), _ai, kb, new RobotGenerator());
            var result = await pipeline.GenerateRobotFromSpecAsync(spec.SourcePath, ct).ConfigureAwait(false);

            var dir = Path.GetDirectoryName(spec.SourcePath);
            var baseName = Path.GetFileNameWithoutExtension(spec.SourcePath);
            var xamlPath = Path.Combine(dir, baseName + ".xaml");
            var csPath = Path.Combine(dir, baseName + ".Actions.cs");
            File.WriteAllText(xamlPath, result.Xaml);
            File.WriteAllText(csPath, result.CSharpCode);
            return "Robot generated and saved: " + xamlPath + ", " + csPath;
        }

        private Task<string> WriteFileAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p)) return Task.FromResult("Missing \"path\" argument.");
            var content = args["content"]?.Value<string>() ?? "";
            try
            {
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, content);
                return Task.FromResult("File written: " + p);
            }
            catch (Exception ex) { return Task.FromResult("Failed to write file: " + ex.Message); }
        }

        private async Task<string> ApplyFixAsync(JObject args, CancellationToken ct)
        {
            var raw = args["path"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return "Missing \"path\" argument.";
            var content = args["content"]?.Value<string>() ?? "";
            return await EditDocumentViaDteAsync(ResolvePath(raw), content, ct).ConfigureAwait(true);
        }

        private async Task<string> EditOpenDocumentAsync(JObject args, CancellationToken ct)
        {
            var raw = args["path"]?.Value<string>() ?? "";
            var p = string.IsNullOrWhiteSpace(raw) ? null : ResolvePath(raw);
            var content = args["content"]?.Value<string>() ?? "";
            return await EditDocumentViaDteAsync(p, content, ct).ConfigureAwait(true);
        }

        private Task<string> EditOpenDocumentDisplayAsync(JObject args, CancellationToken ct)
        {
            var raw = args["path"]?.Value<string>() ?? "";
            return Task.FromResult(string.IsNullOrWhiteSpace(raw) ? "active document" : ResolvePath(raw));
        }

        /// <summary>
        /// Edits a file through the Visual Studio editor buffer (DTE) rather than a raw
        /// disk write: open/activate the tab, replace the buffer text, then save. This keeps
        /// the editor in sync, gives a single Undo step, and avoids the "file modified
        /// outside the editor" reload prompt. <paramref name="path"/> may be null to edit
        /// the currently active document.
        /// </summary>
        private async Task<string> EditDocumentViaDteAsync(string? path, string content, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _getDte();
            if (dte == null) return "Visual Studio is not available.";

            Document? doc;
            string? resolved;

            if (string.IsNullOrWhiteSpace(path))
            {
                doc = dte.ActiveDocument;
                if (doc == null) return "No document is active in the editor and no \"path\" was provided.";
                resolved = doc.FullName;
            }
            else
            {
                resolved = ResolvePath(path!);
                doc = await VsDocumentService.OpenOrActivateAsync(dte, resolved).ConfigureAwait(true);
                if (doc == null) return "Could not open the file in Visual Studio: " + resolved;
            }

            var ready = await VsDocumentService.WaitForDocumentReadyAsync(doc).ConfigureAwait(true);
            if (!ready) return "The document editor buffer is not ready: " + (resolved ?? doc.Name);

            var oldText = VsDocumentService.ReadDocumentText(doc) ?? string.Empty;
            var diff = TextDiff.Diff(oldText, content ?? string.Empty);

            var error = VsDocumentService.ReplaceDocumentText(dte, doc, content ?? string.Empty);
            if (error != null) return error;

            try
            {
                doc.Save();
            }
            catch (Exception ex)
            {
                return "Edited in the editor but failed to save to disk: " + ex.Message;
            }

            return "Edited in editor and saved: " + (resolved ?? doc.Name) + Environment.NewLine + diff.Summary.TrimEnd();
        }

        private async Task<string> CreateRobotAsync(JObject args, CancellationToken ct)
        {
            var number = args["number"]?.Value<string>() ?? "";
            if (string.IsNullOrWhiteSpace(number) || !number.All(char.IsDigit))
                return "Robot number must be digits only.";

            var root = _getRobotsRootPath();
            if (string.IsNullOrWhiteSpace(root)) return "Robot solutions directory is not configured.";
            var template = _getRobotTemplateName() ?? "";

            var result = await _robotTemplate.CreateRobotAsync("RPA-" + number, root!, template, null, ct).ConfigureAwait(false);
            return result.Success ? "Robot created: " + result.SolutionPath : "Failed: " + result.Message;
        }

        private async Task<string> BuildAsync(JObject args, CancellationToken ct)
        {
            await _buildFeedback.BuildReleaseAsync(ct).ConfigureAwait(false);
            return "Release build started.";
        }

        private Task<string> ConvertUiPathAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return Task.FromResult("File not found: " + p);
            try
            {
                var xaml = File.ReadAllText(p);
                var result = new UiPathToWfConverter(new RobotGenerator()).Convert(xaml);
                var dir = Path.GetDirectoryName(p);
                var baseName = Path.GetFileNameWithoutExtension(p);
                var xamlPath = Path.Combine(dir, baseName + ".wf.xaml");
                var csPath = Path.Combine(dir, baseName + ".wf.cs");
                File.WriteAllText(xamlPath, result.Xaml);
                File.WriteAllText(csPath, result.CSharpCode);
                return Task.FromResult("Converted: " + xamlPath + ", " + csPath + " (warnings: " + result.Warnings.Count + ")");
            }
            catch (Exception ex) { return Task.FromResult("Conversion failed: " + ex.Message); }
        }

        private async Task<string> KbImportAsync(JObject args, CancellationToken ct)
        {
            var kbPath = _getKbPath();
            if (string.IsNullOrWhiteSpace(kbPath)) return "Knowledge base directory is not configured.";
            var robotsRoot = _getRobotsRootPath();
            if (string.IsNullOrWhiteSpace(robotsRoot) || !Directory.Exists(robotsRoot))
                return "Robot solutions directory is not configured or does not exist: " + robotsRoot;

            var modeText = (args["mode"]?.Value<string>() ?? "override").Trim();
            var mode = string.Equals(modeText, "continue", StringComparison.OrdinalIgnoreCase)
                ? KnowledgeBaseFormationMode.Continue
                : KnowledgeBaseFormationMode.Override;

            var service = new RobotKnowledgeBaseImportService(_ai);
            var result = await service.ImportFromRobotsAsync(robotsRoot, kbPath, null, null, ct, null, mode).ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine("Robots processed: " + result.RobotsProcessed);
            sb.AppendLine("Spec files written: " + result.SpecFilesWritten);
            sb.AppendLine("Task files written: " + result.TaskFilesWritten);
            sb.AppendLine("Tasks added to index: " + result.TasksAdded);
            foreach (var w in result.Warnings) sb.AppendLine("Warning: " + w);
            return sb.ToString();
        }

        private async Task<string> KbAddAsync(JObject args, CancellationToken ct)
        {
            var kbPath = _getKbPath();
            if (string.IsNullOrWhiteSpace(kbPath)) return "Knowledge base directory is not configured.";

            var name = args["name"]?.Value<string>() ?? "";
            var taskText = args["task"]?.Value<string>() ?? "";
            var record = new SolutionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "Imported task" : name,
                XamlFragment = args["xamlFragment"]?.Value<string>() ?? "",
                CSharpCode = args["csharpCode"]?.Value<string>() ?? "",
                CreatedUtc = DateTime.UtcNow,
                Tasks = new List<TaskDescriptor> { new TaskDescriptor { Description = taskText, Action = taskText } }
            };

            var kb = new FileKnowledgeBase(kbPath);
            await kb.AddAsync(record, ct).ConfigureAwait(false);
            return "Added to knowledge base: " + record.Id + " (" + record.Name + ")";
        }
    }
}

