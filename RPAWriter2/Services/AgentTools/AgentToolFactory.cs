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
        private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> _askQuestionAsync;
        private readonly Func<string?> _getSkillsPath;
        private readonly ShellCommandService _shell = new ShellCommandService();
        private readonly MemoryBankMode _memoryBankMode;
        private readonly Func<string, string, CancellationToken, Task<string>> _spawnSubAgentAsync;
        private readonly bool _enableSubagents;
        private readonly int _subagentMaxDepth;
        private readonly AsyncLocal<int> _subagentDepth = new AsyncLocal<int>();

        public AgentToolFactory(
            IAiProvider ai,
            Func<SpecModel?> getSpec,
            Func<string?> getKbPath,
            Func<string?> getRobotsRootPath,
            Func<string?> getRobotTemplateName,
            Func<string?> getSolutionDir,
            Func<DTE2?> getDte,
            IBuildFeedbackService buildFeedback,
            RobotTemplateService robotTemplate,
            Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> askQuestionAsync,
            Func<string?> getSkillsPath,
            MemoryBankMode memoryBankMode,
            Func<string, string, CancellationToken, Task<string>> spawnSubAgentAsync,
            bool enableSubagents,
            int subagentMaxDepth)
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
            _askQuestionAsync = askQuestionAsync ?? throw new ArgumentNullException(nameof(askQuestionAsync));
            _getSkillsPath = getSkillsPath ?? throw new ArgumentNullException(nameof(getSkillsPath));
            _memoryBankMode = memoryBankMode;
            _spawnSubAgentAsync = spawnSubAgentAsync ?? throw new ArgumentNullException(nameof(spawnSubAgentAsync));
            _enableSubagents = enableSubagents;
            _subagentMaxDepth = subagentMaxDepth > 0 ? subagentMaxDepth : 2;
        }
        public AgentToolRegistry CreateRegistry()
        {
            var registry = new AgentToolRegistry();

            // ---- Read-only tools ----
            registry.Register(new AgentTool("read_spec", "Return the currently imported specification (name, steps, inputs, outputs, tasks, raw text).", "{}", mutates: false, ReadSpecAsync));
            // Temporarily excluded in favor of read_files (buffer-vs-disk is handled inside read_files):
            // registry.Register(new AgentTool("read_file", "Read a text file FROM DISK (saved content only) by absolute or solution-relative path. Does not reflect unsaved editor changes; to read the open editor tab use open_file then read_open_document.", "{\"path\":\"string\"}", false, ReadFileAsync));
            registry.Register(new AgentTool("read_files", "Read the content of text or image files at the provided absolute paths, or return only an inclusive one-based line range when start_line/end_line are provided on the same file entry as its path. When you already know multiple files you need, read them together in one call, and call this tool in the same response as other independent tool calls. Each read returns at most 2000 lines / ~47k characters; longer files report their total line count, page through them with start_line/end_line on that file's entry. Binary files that are not image and large files are not supported. Returns file contents or error messages for each path.", "{\"files\":[{\"path\":\"string\",\"start_line?\":\"integer\",\"end_line?\":\"integer\"}]}", false, ReadFilesAsync, ReadFilesDisplayAsync));
            registry.Register(new AgentTool("read_robot_sources", "List the robot source files (.xaml/.cs) under a directory with their contents.", "{\"directory\":\"string\"}", false, ReadRobotSourcesAsync));
            registry.Register(new AgentTool("analyze_spec", "Analyze the imported spec into a summary and tasks.", "{}", false, AnalyzeSpecAsync));
            registry.Register(new AgentTool("decompose_tasks", "Decompose a text into atomic tasks.", "{\"text\":\"string\"}", false, DecomposeTasksAsync));
            registry.Register(new AgentTool("reconstruct_spec", "Reconstruct a spec from robot sources under a directory.", "{\"robotName\":\"string\",\"directory\":\"string\"}", false, ReconstructSpecAsync));
            registry.Register(new AgentTool("map_task_to_solution", "Locate the solution for a task in robot sources.", "{\"task\":{\"action\":\"string\",\"target\":\"string\",\"description\":\"string\"},\"directory\":\"string\"}", false, MapTaskToSolutionAsync));
            registry.Register(new AgentTool("find_similar", "Find similar tasks in the knowledge base.", "{\"action\":\"string\",\"target\":\"string\",\"description\":\"string\",\"topK\":\"number\"}", false, FindSimilarAsync));
            registry.Register(new AgentTool("list_kb", "List the solutions in the knowledge base.", "{}", false, ListKbAsync));
            registry.Register(new AgentTool("read_errors", "Read compiler errors from the Visual Studio Error List.", "{}", false, ReadErrorsAsync));
            registry.Register(new AgentTool("read_output", "Read the Visual Studio Output window text.", "{}", false, ReadOutputAsync));
            // Temporarily excluded in favor of read_files:
            // registry.Register(new AgentTool("open_file", "Open a file in Visual Studio and activate its tab. Use before read_open_document when the file's tab is not open yet. Does not read the file content from disk.", "{\"path\":\"string\"}", false, OpenFileAsync));
            // registry.Register(new AgentTool("read_open_document", "Read the content currently shown in an open editor tab (includes unsaved changes). Provide \"path\" to read a specific open tab; omit it to read the active document. Never reads from disk. Prefer this over read_file for files being edited.", "{\"path\":\"string\"}", false, ReadOpenDocumentAsync, ReadOpenDocumentDisplayAsync));
            registry.Register(new AgentTool("analyze_logs", "Analyze log files at the given paths.", "{\"paths\":[\"string\"]}", false, AnalyzeLogsAsync));
            if (_memoryBankMode == MemoryBankMode.Tool)
            {
                registry.Register(new AgentTool("read_memory", "Read the project memory bank (memory-bank/*.md) to restore long-term context.", "{}", false, ReadMemoryAsync));
            }
            registry.Register(new AgentTool("ask_question", "Ask user a question for clarifying or gathering information needed to complete the task. For example, ask the user clarifying questions about a key implementation decision. You should only ask one question. Provide an array of 2-5 options for the user to choose from. Never include an option to toggle to Act mode.", "{\"question\":\"string\",\"options\":[\"string\"]}", mutates: false, AskQuestionAsync));
            if (_enableSubagents)
            {
                registry.Register(new AgentTool(
                    "spawn_agent",
                    "Spawn a sub-agent with custom instructions for a specialized task. The sub-agent runs with a fresh context window and the same tools available to you, then returns a final report. Use this to delegate focused, self-contained work (research, analysis, codebase exploration) so your own context stays clean. Provide system_prompt (the sub-agent's role/behavior) and task (what it should do). Waits for the sub-agent to finish and returns its result before your next step.",
                    "{\"system_prompt\":\"string\",\"task\":\"string\"}",
                    mutates: false,
                    SpawnAgentAsync,
                    SpawnAgentDisplayAsync,
                    permission: AgentPermissions.Subagents));
            }

            // ---- Mutating tools (Act only) ----
            registry.Register(new AgentTool("generate_robot", "Generate a robot (XAML/C#) from the imported spec and save the artifacts next to the spec.", "{}", mutates: true, GenerateRobotAsync));
            registry.Register(new AgentTool("editor", """An editor for controlled filesystem edits on the text file at the provided path. Provide `insert_line` to insert `new_text` at a specific line number. Otherwise, the tool replaces `old_text` with `new_text`, or creates the file with `new_text` if file does not exist. Use this tool for making small, precise edits to existing files or creating new files over shell commands. If several edits to different files or non-overlapping regions are already known, emit multiple editor tool calls in the same response instead of serializing them across turns.""", "{\"path\":\"string\",\"old_text?\":\"string\",\"new_text\":\"string\",\"insert_line?\":\"integer\"}", true, EditorAsync, EditorDisplayAsync, permission: AgentPermissions.EditFiles));
            registry.Register(new AgentTool("create_robot", "Create a robot solution (RPA-<number>) from the configured template.", "{\"number\":\"string\"}", true, CreateRobotAsync));
            registry.Register(new AgentTool("build", "Run a Release build of the open solution.", "{}", true, BuildAsync));
            registry.Register(new AgentTool("convert_uipath", "Convert a UiPath XAML file to Workflow Foundation and write the artifacts.", "{\"path\":\"string\"}", true, ConvertUiPathAsync));
            registry.Register(new AgentTool("kb_import", "Import the knowledge base from the robots directory.", "{\"mode\":\"override|continue\"}", true, KbImportAsync));
            registry.Register(new AgentTool("kb_add", "Add a record to the knowledge base.", "{\"name\":\"string\",\"task\":\"string\",\"xamlFragment\":\"string\",\"csharpCode\":\"string\"}", true, KbAddAsync));
            if (_memoryBankMode == MemoryBankMode.Tool)
            {
                registry.Register(new AgentTool("update_memory", "Write a project memory bank file (activeContext.md, progress.md, etc.).", "{\"file\":\"string\",\"content\":\"string\"}", true, UpdateMemoryAsync));
            }

            // ---- Read-only navigation / inspection tools (permission-gated) ----
            registry.Register(new AgentTool(
                "list_files",
                """
                Request to list files and directories within the specified directory. If recursive is true, it will list all files and directories recursively. If recursive is false or not provided, it will only list the top-level contents. Do not use this tool to confirm the existence of files you may have created, as the user will let you know if the files were created successfully or not.

                Parameters:
                - path: (required) The path of the directory to list contents for (relative to the current workspace directory)
                - recursive: (required) Whether to list files recursively. Use true for recursive listing, false for top-level only.

                Example: Listing all files in the current directory (top-level only)
                { "path": ".", "recursive": false }

                Example: Listing all files recursively in src directory
                { "path": "src", "recursive": true }
                """,
                "{\"path\":\"string\",\"recursive\":\"boolean\"}",
                mutates: false,
                ListFilesAsync,
                permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("search_codebase", "Perform regex pattern searches across the codebase. Supports multiple parallel searches. When several search patterns could be useful and do not depend on each other, run them together in one call, and call this tool in the same response as other independent tool calls. Use for finding code patterns, function definitions, class names, imports, etc. Output beyond ~48k characters per query is middle-truncated; narrow patterns beat broad ones.", "{\"queries\":[\"string\"]}", false, SearchCodebaseAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("git_status", "Show the git working-tree status of the open solution's repository.", "{}", false, GitStatusAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("git_diff", "Show the uncommitted git diff of the open solution's repository (optionally for one path).", "{\"path\":\"string\"}", false, GitDiffAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("fetch_web_content", """Fetch content from URLs and analyze them using the provided prompts. Use for retrieving documentation, API references, or any web content. Each request includes a URL and a prompt describing what information to extract. Fetch independent URLs together in one call, and call this tool in the same response as other independent tool calls.""", "{\"requests\":[{\"url\":\"string\",\"prompt\":\"string\"}]}", false, FetchWebContentAsync, permission: AgentPermissions.WebFetch));
            registry.Register(new AgentTool("skills", """Execute a skill within the main conversation. When users ask you to perform tasks, check if any available skills match. When users reference a slash command, invoke it with this tool. Input: `skill` (required) and optional `args`. Example: `skill: "pdf"`, `skill: "commit", args: "-m \"Fix bug\""`, `skill: "review-pr", args: "123"`, `skill: "ms-office-suite:pdf"`. When a skill matches the user's request, invoking this tool is a blocking requirement before any other response. Never mention a skill without invoking this tool.""", "{\"skill\":\"string\",\"args?\":\"string\"}", false, SkillsAsync, permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool("submit_and_exit", """Submit the final answer and exit the conversation. For example, submit a summary of the investigation and confirm the issue is resolved. You should only submit once all necessary steps are completed AND you have verified the result by execution. Before calling this, re-read the task, then confirm from your own tool output that every required file exists at the specified path and that its contents match the expected format, data types, and values. If the task provides tests, run them and confirm they pass; if it does not, run your own solution end to end and read the output back as evidence. Do not submit on the assumption that your solution works — submit because you have observed evidence that it does. Provide a summary of the investigation and confirm the issue is resolved.""", "{\"summary\":\"string\",\"verified\":\"boolean\"}", false, SubmitAndExitAsync, completesRun: true));

            // ---- Mutating tools (Act only, permission-gated) ----
            registry.Register(new AgentTool("run_commands", """Run non-interactive shell commands from the root of the workspace in Windows environment. Use for listing files, checking git status, running builds, executing tests, etc. Commands must be non-interactive. Commands that require follow-up input like pagers should be skipped or used with supported flags/env (e.g. git --no-pager, --non-interactive) to bypass the interaction steps. Output beyond ~47k characters is middle-truncated (start and end preserved); filter output when you need specific sections. Commands run through Windows PowerShell (powershell.exe); quote paths and arguments for powershell.exe and use ';' to sequence commands. Write commands directly; do not wrap them in another powershell.exe -Command invocation. Only start another shell when you intentionally need a different shell or a separate process. Include multiple commands in the same call when they are independent and safe to run concurrently. When independent reads, searches, or edits are also needed, call those tools in the same response.""", "{\"commands\":[\"string\"]}", true, RunCommandsAsync, permission: AgentPermissions.ExecuteCommand));
            registry.Register(new AgentTool("download_file", "Download a file from a URL and save it to disk.", "{\"url\":\"string\",\"path\":\"string\"}", true, DownloadFileAsync, permission: AgentPermissions.WebFetch));

            return registry;
        }

        private async Task<string> AskQuestionAsync(JObject args, CancellationToken ct)
        {
            var question = (args["question"]?.Value<string>() ?? "").Trim();
            var options = new List<string>();
            if (args["options"] is JArray array)
            {
                foreach (var item in array)
                {
                    var text = item?.Value<string>() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                        options.Add(text.Trim());
                }
            }

            if (string.IsNullOrEmpty(question))
                return "Missing or empty \"question\" argument.";
            if (options.Count < 2 || options.Count > 5)
                return "The \"options\" argument must contain between 2 and 5 non-empty options.";

            try
            {
                return await _askQuestionAsync(question, options, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "ask_question failed: " + ex.Message;
            }
        }

        private async Task<string> SpawnAgentAsync(JObject args, CancellationToken ct)
        {
            var systemPrompt = (args["system_prompt"]?.Value<string>() ?? string.Empty).Trim();
            var task = (args["task"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(systemPrompt))
                return "Missing or empty \"system_prompt\" argument.";
            if (string.IsNullOrEmpty(task))
                return "Missing or empty \"task\" argument.";

            var depth = _subagentDepth.Value;
            if (depth >= _subagentMaxDepth)
                return "spawn_agent refused: maximum sub-agent nesting depth reached (" + _subagentMaxDepth + ").";

            _subagentDepth.Value = depth + 1;
            try
            {
                return await _spawnSubAgentAsync(systemPrompt, task, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "spawn_agent failed: " + ex.Message;
            }
            finally
            {
                _subagentDepth.Value = depth;
            }
        }

        private Task<string> SpawnAgentDisplayAsync(JObject args, CancellationToken ct)
        {
            var task = (args["task"]?.Value<string>() ?? string.Empty).Trim();
            var shortTask = task.Length > 60 ? task.Substring(0, 60) + "…" : task;
            return Task.FromResult(string.IsNullOrEmpty(shortTask) ? "sub-agent" : shortTask);
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

        private const int ReadFilesMaxLines = 2000;
        private const int ReadFilesMaxChars = 48000; // ~47k characters, matching Cline
        private const int SearchMaxCharsPerQuery = 48000; // ~48k characters per query, matching Cline

        /// <summary>
        /// Cline-compatible file reader. Reads one or more files by absolute (or solution-relative)
        /// path, optionally limited to an inclusive one-based line range. When a file is currently
        /// open in Visual Studio, its content is read from the editor buffer (including unsaved
        /// changes); otherwise it is read from disk. Disk is also the fallback when the buffer of an
        /// open document cannot be read (e.g. a non-text document).
        /// </summary>
        private async Task<string> ReadFilesAsync(JObject args, CancellationToken ct)
        {
            var entries = ParseReadFilesEntries(args["files"]);
            if (entries.Count == 0)
                return "Missing or invalid \"files\" argument. Expected an array of {path, start_line?, end_line?} objects (or a string / array of strings).";

            // Detecting an open editor tab and reading its buffer must happen on the VS UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _getDte();

            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                var p = ResolvePath(entry.Path ?? "");
                if (string.IsNullOrWhiteSpace(p))
                {
                    sb.AppendLine("Error reading a files entry: missing \"path\".");
                    continue;
                }

                string text;
                string source;

                var doc = dte != null ? VsDocumentService.FindOpenDocument(dte, p) : null;
                if (doc != null)
                {
                    await VsDocumentService.WaitForDocumentReadyAsync(doc).ConfigureAwait(true);
                    var buffer = VsDocumentService.ReadDocumentText(doc);
                    if (buffer != null)
                    {
                        text = buffer;
                        source = "editor buffer";
                    }
                    else
                    {
                        var diskText = ReadFromDiskOrNull(p, out var diskError);
                        if (diskError != null)
                        {
                            sb.AppendLine("Error reading " + p + ": " + diskError);
                            continue;
                        }
                        text = diskText!;
                        source = "disk (buffer unreadable, fallback)";
                    }
                }
                else
                {
                    var diskText = ReadFromDiskOrNull(p, out var diskError);
                    if (diskError != null)
                    {
                        sb.AppendLine("Error reading " + p + ": " + diskError);
                        continue;
                    }
                    text = diskText!;
                    source = "disk";
                }

                sb.Append(FormatReadFilesResult(p, source, text, entry.StartLine, entry.EndLine));
            }

            return sb.ToString();
        }

        private Task<string> ReadFilesDisplayAsync(JObject args, CancellationToken ct)
        {
            var paths = ParseReadFilesEntries(args["files"])
                .Where(e => !string.IsNullOrWhiteSpace(e.Path))
                .Select(e => ResolvePath(e.Path))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            return Task.FromResult(paths.Count == 0 ? string.Empty : string.Join(", ", paths));
        }

        private static List<ReadFilesEntry> ParseReadFilesEntries(JToken files)
        {
            var result = new List<ReadFilesEntry>();

            if (files == null)
                return result;

            if (files.Type == JTokenType.String)
            {
                var s = files.Value<string>();
                if (!string.IsNullOrWhiteSpace(s))
                    result.Add(new ReadFilesEntry { Path = s });
                return result;
            }

            if (files.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)files)
                {
                    if (item.Type == JTokenType.String)
                    {
                        var s = item.Value<string>();
                        if (!string.IsNullOrWhiteSpace(s))
                            result.Add(new ReadFilesEntry { Path = s });
                    }
                    else if (item.Type == JTokenType.Object)
                    {
                        var obj = (JObject)item;
                        result.Add(new ReadFilesEntry
                        {
                            Path = obj["path"]?.Value<string>() ?? "",
                            StartLine = obj["start_line"]?.Value<int>() ?? 0,
                            EndLine = obj["end_line"]?.Value<int>() ?? 0
                        });
                    }
                }
                return result;
            }

            // Defensive: a single object {path, start_line?, end_line?}.
            if (files.Type == JTokenType.Object)
            {
                var obj = (JObject)files;
                result.Add(new ReadFilesEntry
                {
                    Path = obj["path"]?.Value<string>() ?? "",
                    StartLine = obj["start_line"]?.Value<int>() ?? 0,
                    EndLine = obj["end_line"]?.Value<int>() ?? 0
                });
            }

            return result;
        }

        private sealed class ReadFilesEntry
        {
            public string Path { get; set; } = "";
            public int StartLine { get; set; }
            public int EndLine { get; set; }
        }

        /// <summary>Reads a text file from disk, or returns an error describing why it could not be read.</summary>
        private static string? ReadFromDiskOrNull(string path, out string? error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = "file not found";
                return null;
            }
            if (IsBinaryFile(path))
            {
                error = "binary files that are not images are not supported";
                return null;
            }
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Heuristic: a file containing a NUL byte in its first 8KB is treated as binary.</summary>
        private static bool IsBinaryFile(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var buffer = new byte[8192];
                    var read = fs.Read(buffer, 0, buffer.Length);
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] == 0)
                            return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatReadFilesResult(string path, string source, string text, int startLine, int endLine)
        {
            var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            var allLines = normalized.Split('\n');
            if (allLines.Length > 1 && allLines[allLines.Length - 1].Length == 0)
                allLines = allLines.Take(allLines.Length - 1).ToArray();
            var totalLines = allLines.Length;

            var hasRange = startLine > 0 || endLine > 0;
            var start = startLine > 0 ? startLine : 1;
            var end = endLine > 0 ? Math.Min(endLine, totalLines) : totalLines;
            if (start < 1) start = 1;
            if (start > totalLines) start = Math.Max(1, totalLines);
            if (end < start) end = start;

            var selected = allLines.Skip(start - 1).Take(end - start + 1).ToArray();
            var content = string.Join("\n", selected);

            var truncated = selected.Length > ReadFilesMaxLines;
            if (truncated)
                content = string.Join("\n", selected.Take(ReadFilesMaxLines));
            if (content.Length > ReadFilesMaxChars)
            {
                content = content.Substring(0, ReadFilesMaxChars);
                truncated = true;
            }

            var sb = new StringBuilder();
            sb.Append("FILE: ").Append(path).Append(" (").Append(source).Append(')');
            if (hasRange)
                sb.Append(" [lines ").Append(start).Append('-').Append(end).Append(']');
            sb.AppendLine();
            sb.Append(content);

            if (truncated)
            {
                sb.AppendLine();
                sb.Append("…(truncated; file has ").Append(totalLines)
                  .Append(" lines, use start_line/end_line to page through it)");
            }

            return sb.ToString();
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

        private const int ListFilesLimit = 200;

        private static readonly HashSet<string> ListFilesSkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", ".vs", "bin", "obj", "node_modules", "packages", "TestResults",
            "__pycache__", "env", "venv", "dist", "out", "bundle", "vendor", "tmp", "temp",
            "deps", "pkg", "Pods",
        };

        private Task<string> ListFilesAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p))
                return Task.FromResult("Directory not found: " + p);

            var recursive = args["recursive"]?.Value<bool>() ?? false;

            // Load the nearest .gitignore (walking up to the repo root) and apply it only
            // when the listed directory lives inside that repo.
            var gitRoot = FindGitIgnoreRoot(_getSolutionDir() ?? p);
            var ignore = gitRoot != null && IsUnder(gitRoot, p)
                ? GitIgnoreMatcher.Load(Path.Combine(gitRoot, ".gitignore"))
                : null;

            var dirs = new List<string>();
            var files = new List<string>();
            try
            {
                CollectListEntries(p, p, recursive, dirs, files, ignore, gitRoot ?? p);
            }
            catch (Exception ex)
            {
                return Task.FromResult("Failed to list directory: " + ex.Message);
            }

            var total = dirs.Count + files.Count;
            var entries = new List<string>(total);
            foreach (var d in dirs) entries.Add(d + "/");
            entries.AddRange(files);

            // Directories first (trailing '/'), then files, each alphabetically (case-insensitive).
            entries.Sort((a, b) =>
            {
                var aIsDir = a.EndsWith("/", StringComparison.Ordinal);
                var bIsDir = b.EndsWith("/", StringComparison.Ordinal);
                if (aIsDir != bIsDir) return aIsDir ? -1 : 1;
                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });

            if (entries.Count == 0)
                return Task.FromResult("No files found.");

            var sb = new StringBuilder();
            foreach (var e in entries.Take(ListFilesLimit))
                sb.AppendLine(e);
            if (total > ListFilesLimit)
                sb.AppendLine().AppendLine("(File list truncated. Use list_files on specific subdirectories if you need to explore further.)");
            return Task.FromResult(Truncate(sb.ToString()));
        }

        private void CollectListEntries(string root, string current, bool recursive, List<string> dirs, List<string> files, GitIgnoreMatcher? ignore, string gitRoot)
        {
            string[] subdirs;
            string[] fs;
            try
            {
                subdirs = Directory.GetDirectories(current);
                fs = Directory.GetFiles(current);
            }
            catch
            {
                return; // unreadable directory
            }

            foreach (var d in subdirs)
            {
                if (ListFilesSkippedDirectories.Contains(Path.GetFileName(d)))
                    continue;
                if (ignore != null && ignore.IsIgnored(MakeRelative(gitRoot, d).Replace('\\', '/'), true))
                    continue;

                dirs.Add(MakeRelative(root, d).Replace('\\', '/'));
                if (recursive)
                    CollectListEntries(root, d, true, dirs, files, ignore, gitRoot);
            }

            foreach (var f in fs)
            {
                if (ignore != null && ignore.IsIgnored(MakeRelative(gitRoot, f).Replace('\\', '/'), false))
                    continue;
                files.Add(MakeRelative(root, f).Replace('\\', '/'));
            }
        }

        private static string? FindGitIgnoreRoot(string startDir)
        {
            if (string.IsNullOrWhiteSpace(startDir)) return null;
            var dir = Path.GetFullPath(startDir);
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, ".gitignore")) || Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return null;
        }

        private static bool IsUnder(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(rootFull, pathFull, StringComparison.OrdinalIgnoreCase)
                || pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private Task<string> SearchCodebaseAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return Task.FromResult("No open solution directory to search.");

            var queries = ParseQueries(args["queries"]);
            if (queries.Count == 0)
                return Task.FromResult("Missing or invalid \"queries\" argument. Expected an array of regex patterns.");

            var sb = new StringBuilder();
            foreach (var pattern in queries)
            {
                Regex regex;
                try { regex = new Regex(pattern, RegexOptions.IgnoreCase); }
                catch (ArgumentException ex)
                {
                    sb.AppendLine("Invalid regex \"" + pattern + "\": " + ex.Message);
                    continue;
                }

                var matches = new StringBuilder();
                try
                {
                    foreach (var file in EnumerateTextFiles(root))
                    {
                        string[] lines;
                        try { lines = File.ReadAllLines(file); }
                        catch { continue; }

                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                                matches.Append(MakeRelative(root, file)).Append(':').Append(i + 1).Append(": ").AppendLine(lines[i]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Search for \"" + pattern + "\" failed: " + ex.Message);
                    continue;
                }

                var text = matches.ToString();
                sb.AppendLine("\"" + pattern + "\":");
                sb.AppendLine(text.Length == 0 ? "(no matches)" : MiddleTruncateLines(text, SearchMaxCharsPerQuery));
            }
            return Task.FromResult(sb.ToString());
        }

        private static List<string> ParseQueries(JToken token)
        {
            var result = new List<string>();
            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    var query = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(query))
                        result.Add(query!);
                }
            }
            else
            {
                var single = token?.Value<string>();
                if (!string.IsNullOrWhiteSpace(single))
                    result.Add(single!);
            }
            return result;
        }

        private static string MiddleTruncateLines(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text;

            var half = maxChars / 2;
            var lines = text.Split('\n');

            var head = new List<string>();
            var headChars = 0;
            var i = 0;
            while (i < lines.Length && headChars + lines[i].Length + 1 <= half)
            {
                head.Add(lines[i].TrimEnd('\r'));
                headChars += lines[i].Length + 1;
                i++;
            }

            var tail = new List<string>();
            var tailChars = 0;
            var j = lines.Length - 1;
            while (j >= i && tailChars + lines[j].Length + 1 <= half)
            {
                tail.Add(lines[j].TrimEnd('\r'));
                tailChars += lines[j].Length + 1;
                j--;
            }
            tail.Reverse();

            if (head.Count == 0 && tail.Count == 0)
            {
                // No single whole line fits: fall back to a raw character-level cut.
                return text.Substring(0, half) + "\n…(middle truncated)…\n" + text.Substring(text.Length - (maxChars - half));
            }

            var omitted = lines.Length - head.Count - tail.Count;
            return string.Join("\n", head)
                + "\n…(" + omitted + " lines omitted from the middle)…\n"
                + string.Join("\n", tail);
        }

        private async Task<string> RunCommandsAsync(JObject args, CancellationToken ct)
        {
            var commands = new List<string>();
            var token = args["commands"];
            if (token != null && token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                {
                    var c = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(c)) commands.Add(c);
                }
            }
            else if (token != null)
            {
                var single = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(single)) commands.Add(single);
            }

            if (commands.Count == 0) return "Missing \"commands\" argument (array of command strings).";

            var sb = new StringBuilder();
            foreach (var command in commands)
            {
                var result = await _shell.RunAsync(command, _getSolutionDir() ?? "", ct).ConfigureAwait(false);
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("$ " + command);
                sb.Append(result.Success ? result.Output : "Command failed (exit " + result.ExitCode + "):\n" + result.Output);
            }
            return Truncate(sb.ToString(), 48000);
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

        private async Task<string> FetchWebContentAsync(JObject args, CancellationToken ct)
        {
            var requestsToken = args["requests"];
            if (requestsToken == null || requestsToken.Type != JTokenType.Array)
                return "Missing \"requests\" argument (array of { url, prompt }).";

            var sb = new StringBuilder();
            var index = 0;
            foreach (var item in requestsToken)
            {
                var req = item as JObject;
                if (req == null) continue;
                var url = req["url"]?.Value<string>() ?? "";
                var prompt = req["prompt"]?.Value<string>() ?? "";
                index++;
                if (string.IsNullOrWhiteSpace(url))
                {
                    sb.AppendLine("[" + index + "] Missing \"url\".");
                    continue;
                }

                string text;
                using (var wc = new WebClient())
                using (ct.Register(wc.CancelAsync))
                {
                    try
                    {
                        text = await wc.DownloadStringTaskAsync(url).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("[" + index + "] Web fetch failed (" + url + "): " + ex.Message);
                        continue;
                    }
                }

                var rendered = Truncate(text);
                if (!string.IsNullOrWhiteSpace(prompt) && _ai != null)
                {
                    try
                    {
                        var analysis = await _ai.ChatAsync(
                            new List<ChatMessageData>
                            {
                                new ChatMessageData { Role = "user", Content = prompt + "\n\nCONTENT:\n" + text }
                            },
                            "Extract the requested information from the provided web content according to the user's prompt.",
                            ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(analysis?.Answer))
                            rendered = analysis.Answer;
                    }
                    catch
                    {
                        // AI analysis is best-effort; fall back to the raw content.
                    }
                }

                sb.AppendLine("[" + index + "] " + url);
                sb.AppendLine(rendered);
            }

            return sb.Length == 0 ? "(no requests)" : Truncate(sb.ToString(), 48000);
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

        private static readonly HashSet<string> SearchTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xaml", ".cs", ".vb", ".fs", ".txt", ".md", ".json", ".xml", ".config", ".csproj", ".vbproj", ".fsproj",
            ".sln", ".slnx", ".props", ".targets", ".vsct", ".yaml", ".yml", ".ps1", ".psm1", ".sh", ".bat", ".cmd",
            ".py", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".html", ".htm", ".cshtml", ".razor", ".resx",
            ".ruleset", ".gitignore", ".editorconfig", ".gitattributes", ".manifest", ".tt", ".proto"
        };

        private static readonly HashSet<string> SearchSkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", "bin", "obj", "node_modules", "packages", "TestResults"
        };

        private const long SearchMaxFileBytes = 1024 * 1024; // skip files larger than ~1 MB

        private static IEnumerable<string> EnumerateTextFiles(string dir)
        {
            var results = new List<string>();
            var stack = new Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                string[] files;
                string[] subdirs;
                try
                {
                    files = Directory.GetFiles(current);
                    subdirs = Directory.GetDirectories(current);
                }
                catch
                {
                    continue; // unreadable directory
                }

                foreach (var file in files)
                {
                    if (!SearchTextExtensions.Contains(Path.GetExtension(file)))
                        continue;
                    try
                    {
                        if (new FileInfo(file).Length <= SearchMaxFileBytes)
                            results.Add(file);
                    }
                    catch { /* skip unreadable file */ }
                }

                foreach (var sub in subdirs)
                {
                    if (!SearchSkippedDirectories.Contains(Path.GetFileName(sub)))
                        stack.Push(sub);
                }
            }
            return results;
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

        private async Task<string> EditorAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p)) return "Missing \"path\" argument.";

            var newText = args["new_text"]?.Value<string>() ?? "";
            var oldText = args["old_text"]?.Value<string>();
            int? insertLine = null;
            var insertToken = args["insert_line"];
            if (insertToken != null && insertToken.Type != JTokenType.Null)
            {
                if (insertToken.Type == JTokenType.Integer) insertLine = insertToken.Value<int>();
                else if (int.TryParse(insertToken.Value<string>(), out var parsed)) insertLine = parsed;
            }

            // Read the current content from the open VS editor buffer when available, else from disk.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _getDte();
            var openDoc = dte != null ? VsDocumentService.FindOpenDocument(dte, p) : null;

            string current;
            if (openDoc != null)
            {
                var ready = await VsDocumentService.WaitForDocumentReadyAsync(openDoc).ConfigureAwait(true);
                current = ready ? (VsDocumentService.ReadDocumentText(openDoc) ?? "") : ReadDiskText(p);
            }
            else
            {
                current = ReadDiskText(p);
            }

            string newContent;
            if (insertLine.HasValue)
            {
                newContent = InsertAtLine(current, insertLine.Value, newText);
            }
            else if (oldText != null)
            {
                var idx = current.IndexOf(oldText, StringComparison.Ordinal);
                if (idx < 0) return "old_text was not found in the file (no edit was made).";
                var second = current.IndexOf(oldText, idx + oldText.Length, StringComparison.Ordinal);
                if (second >= 0) return "old_text is not unique in the file; make it more specific and retry.";
                newContent = current.Substring(0, idx) + newText + current.Substring(idx + oldText.Length);
            }
            else
            {
                newContent = newText;
            }

            // Prefer editing the open VS editor buffer; otherwise write straight to disk.
            if (openDoc != null && dte != null)
            {
                var error = VsDocumentService.ReplaceDocumentText(dte, openDoc, newContent);
                if (error != null) return error;
                try { openDoc.Save(); }
                catch (Exception ex) { return "Edited in the editor but failed to save: " + ex.Message; }
                return "Edited in editor and saved: " + p;
            }

            try
            {
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, newContent);
                return "File written: " + p;
            }
            catch (Exception ex) { return "Failed to write file: " + ex.Message; }
        }

        private Task<string> EditorDisplayAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            return Task.FromResult(string.IsNullOrWhiteSpace(p) ? "file" : p);
        }

        private static string ReadDiskText(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return ""; }
        }

        private static string InsertAtLine(string text, int insertLine, string newText)
        {
            var newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var idx = insertLine < 1 ? 0 : insertLine - 1;
            if (idx > lines.Count) idx = lines.Count;
            lines.Insert(idx, newText.Replace("\r\n", "\n").Replace("\r", "\n"));
            return string.Join(newline, lines);
        }

        private Task<string> SkillsAsync(JObject args, CancellationToken ct)
        {
            var skill = args["skill"]?.Value<string>() ?? "";
            var skillArgs = args["args"]?.Value<string>() ?? "";
            var skillsPath = _getSkillsPath();
            if (string.IsNullOrWhiteSpace(skillsPath) || !Directory.Exists(skillsPath))
                return Task.FromResult("No skills directory configured (set \"Skills directory\" in Settings).");

            if (string.IsNullOrWhiteSpace(skill))
            {
                try
                {
                    var names = Directory.EnumerateDirectories(skillsPath)
                        .Select(Path.GetFileName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    return Task.FromResult(names.Count == 0 ? "(no skills found)" : "Available skills: " + string.Join(", ", names));
                }
                catch (Exception ex) { return Task.FromResult("Failed to list skills: " + ex.Message); }
            }

            var candidates = new[]
            {
                Path.Combine(skillsPath, skill, "SKILL.md"),
                Path.Combine(skillsPath, skill + ".md")
            };
            foreach (var file in candidates)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        var content = File.ReadAllText(file);
                        if (!string.IsNullOrWhiteSpace(skillArgs))
                            content += "\n\nInvocation arguments: " + skillArgs;
                        return Task.FromResult(Truncate(content, 40000));
                    }
                }
                catch { /* try the next candidate */ }
            }

            return Task.FromResult("Skill not found: " + skill);
        }

        private Task<string> SubmitAndExitAsync(JObject args, CancellationToken ct)
        {
            var summary = args["summary"]?.Value<string>() ?? "";
            return Task.FromResult(string.IsNullOrWhiteSpace(summary) ? "(no summary)" : summary);
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
            await _buildFeedback.BuildAsync("Release", ct).ConfigureAwait(false);
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

        /// <summary>
        /// Minimal .gitignore matcher: loads patterns from a .gitignore file and checks
        /// whether a repo-relative (posix) path should be ignored. Supports comments,
        /// '!' negation, trailing '/' (directory-only), leading '/' (anchored), '*', '?',
        /// '**' and '[...]' character classes. The last matching pattern wins.
        /// </summary>
        private sealed class GitIgnoreMatcher
        {
            private sealed class Rule
            {
                public Regex Regex { get; set; } = null!;
                public bool Negated { get; set; }
            }

            private readonly List<Rule> _rules = new List<Rule>();

            public static GitIgnoreMatcher Load(string path)
            {
                var matcher = new GitIgnoreMatcher();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return matcher;
                try
                {
                    foreach (var raw in File.ReadAllLines(path))
                        matcher.Add(raw);
                }
                catch
                {
                    // Unreadable .gitignore: treat as no rules.
                }
                return matcher;
            }

            private void Add(string line)
            {
                var trimmed = (line ?? "").Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    return;

                var negated = trimmed[0] == '!';
                if (negated)
                    trimmed = trimmed.Substring(1).TrimStart();

                // Normalize backslashes (Windows-generated .gitignore) to forward slashes.
                var pattern = trimmed.Replace('\\', '/');

                var anchored = pattern.StartsWith("/", StringComparison.Ordinal);
                if (anchored)
                    pattern = pattern.Substring(1);

                var directoryOnly = pattern.EndsWith("/", StringComparison.Ordinal);
                if (directoryOnly)
                    pattern = pattern.TrimEnd('/');

                if (pattern.Length == 0)
                    return;

                _rules.Add(new Rule { Regex = BuildRegex(pattern, anchored, directoryOnly), Negated = negated });
            }

            private static Regex BuildRegex(string pattern, bool anchored, bool directoryOnly)
            {
                var sb = new StringBuilder();
                if (anchored) sb.Append('^');
                else sb.Append("(^|/)");

                for (var i = 0; i < pattern.Length; i++)
                {
                    var c = pattern[i];
                    if (c == '*')
                    {
                        if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                        {
                            if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                            {
                                sb.Append("(?:.*/)?");
                                i += 2;
                            }
                            else
                            {
                                sb.Append(".*");
                                i += 1;
                            }
                        }
                        else
                        {
                            sb.Append("[^/]*");
                        }
                    }
                    else if (c == '?')
                    {
                        sb.Append("[^/]");
                    }
                    else if (c == '[')
                    {
                        var close = pattern.IndexOf(']', i + 1);
                        if (close < 0)
                        {
                            sb.Append(Regex.Escape(c.ToString()));
                        }
                        else
                        {
                            sb.Append(pattern.Substring(i, close - i + 1));
                            i = close;
                        }
                    }
                    else
                    {
                        sb.Append(Regex.Escape(c.ToString()));
                    }
                }

                if (directoryOnly) sb.Append("(/.*)?$");
                else sb.Append('$');

                return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
            }

            public bool IsIgnored(string posixPath, bool isDirectory)
            {
                if (string.IsNullOrEmpty(posixPath))
                    return false;

                var ignored = false;
                foreach (var rule in _rules)
                {
                    if (rule.Regex.IsMatch(posixPath))
                        ignored = !rule.Negated;
                }
                return ignored;
            }
        }
    }
}

