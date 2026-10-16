using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            registry.Register(new AgentTool("read_open_document", "Read the content currently shown in an open editor tab (includes unsaved changes). Provide \"path\" to read a specific open tab; omit it to read the active document. Never reads from disk. Prefer this over read_file for files being edited.", "{\"path\":\"string\"}", false, ReadOpenDocumentAsync));
            registry.Register(new AgentTool("analyze_logs", "Analyze log files at the given paths.", "{\"paths\":[\"string\"]}", false, AnalyzeLogsAsync));

            // ---- Mutating tools (Act only) ----
            registry.Register(new AgentTool("generate_robot", "Generate a robot (XAML/C#) from the imported spec and save the artifacts next to the spec.", "{}", mutates: true, GenerateRobotAsync));
            registry.Register(new AgentTool("write_file", "Write a text file (create or overwrite) by absolute or solution-relative path.", "{\"path\":\"string\",\"content\":\"string\"}", true, WriteFileAsync));
            registry.Register(new AgentTool("apply_fix", "Overwrite an existing file with corrected content.", "{\"path\":\"string\",\"content\":\"string\"}", true, WriteFileAsync));
            registry.Register(new AgentTool("create_robot", "Create a robot solution (RPA-<number>) from the configured template.", "{\"number\":\"string\"}", true, CreateRobotAsync));
            registry.Register(new AgentTool("build", "Run a Release build of the open solution.", "{}", true, BuildAsync));
            registry.Register(new AgentTool("convert_uipath", "Convert a UiPath XAML file to Workflow Foundation and write the artifacts.", "{\"path\":\"string\"}", true, ConvertUiPathAsync));
            registry.Register(new AgentTool("kb_import", "Import the knowledge base from the robots directory.", "{\"mode\":\"override|continue\"}", true, KbImportAsync));
            registry.Register(new AgentTool("kb_add", "Add a record to the knowledge base.", "{\"name\":\"string\",\"task\":\"string\",\"xamlFragment\":\"string\",\"csharpCode\":\"string\"}", true, KbAddAsync));

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
                var doc = VsDocumentService.OpenOrActivate(dte, p);
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
            var rawPath = args["path"]?.Value<string>() ?? "";
            var p = string.IsNullOrWhiteSpace(rawPath) ? null : ResolvePath(rawPath);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _getDte();
            if (dte == null) return "Visual Studio is not available.";

            Document? doc;
            if (p != null)
            {
                doc = VsDocumentService.FindOpenDocument(dte, p);
                if (doc == null) return "The file is not open in the editor: " + p;
            }
            else
            {
                doc = dte.ActiveDocument;
                if (doc == null) return "No document is active in the editor.";
            }

            var text = VsDocumentService.ReadDocumentText(doc);
            if (text == null) return "The document is not a text document: " + (p ?? doc.Name);
            return Truncate(text);
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
            var all = errors ?? new List<BuildError>();
            var red = all
                .Where(e => e != null && string.Equals(e.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (red.Count == 0)
                return await BuildNoErrorsSummaryAsync(all, ct).ConfigureAwait(false);

            var sb = new StringBuilder();
            foreach (var e in red)
            {
                var loc = e.File ?? "";
                if (e.Line > 0) loc = string.IsNullOrEmpty(loc) ? "line " + e.Line : loc + ":" + e.Line;
                sb.AppendLine("[Error] " + (string.IsNullOrWhiteSpace(e.Code) ? "" : e.Code + ": ") +
                              (e.Message ?? "") + (string.IsNullOrEmpty(loc) ? "" : " (" + loc + ")"));
            }
            return sb.ToString();
        }

        private async Task<string> BuildNoErrorsSummaryAsync(IReadOnlyList<BuildError> errors, CancellationToken ct)
        {
            var warnings = errors.Count(e => e != null && string.Equals(e.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
            var messages = errors.Count(e => e != null && string.Equals(e.Severity, "Message", StringComparison.OrdinalIgnoreCase));

            var sb = new StringBuilder();
            sb.AppendLine("No compilation errors found.");
            if (warnings > 0 || messages > 0)
                sb.AppendLine("Error List contains " + warnings + " warning(s) and " + messages + " message(s), but no Error-level entries.");

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

