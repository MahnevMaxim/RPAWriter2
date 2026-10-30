using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Scope of the read-only robot security audit.</summary>
    public enum SecurityAuditMode
    {
        /// <summary>Full static analysis covering all OWASP-relevant areas.</summary>
        Full,

        /// <summary>Focused scan for hardcoded passwords and logins (credentials) only.</summary>
        CredentialsOnly
    }

    /// <summary>
    /// Read-only security audit of RPA robot solutions. Discovers sibling robot
    /// solutions (RPA-&lt;number&gt;.sln/.slnx) in a directory, collects their source and
    /// configuration files, and asks the AI provider to review each solution as a
    /// security specialist. Produces a single <c>security_audit.md</c> report.
    /// The service only reads files; it never builds, runs or executes anything.
    /// </summary>
    public class RobotSecurityAuditService
    {
        /// <summary>System prompt sent to the model: it acts as a read-only security specialist.</summary>
        public static string SystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "security-audit.txt", PromptDefaults.SecurityAuditSystemPrompt);

        /// <summary>System prompt for the focused credentials-only scan (passwords &amp; logins).</summary>
        public static string CredentialsOnlySystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "security-audit-credentials.txt", PromptDefaults.SecurityAuditCredentialsOnlySystemPrompt);

        /// <summary>Regex matching a robot solution file name: uppercase "RPA-" + digits + .sln/.slnx.</summary>
        private static readonly Regex SolutionNameRegex = new Regex(
            @"^RPA-\d+\.slnx?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Text file extensions collected from a robot solution for the audit.</summary>
        private static readonly string[] SourceExtensions =
        {
            ".sln", ".slnx", ".csproj", ".props", ".targets", ".cs", ".xaml",
            ".config", ".json", ".xml", ".resx", ".settings", ".md", ".txt", ".ps1"
        };

        /// <summary>Directory names never read during collection (build artifacts, VCS, caches).</summary>
        private static readonly string[] ExcludedDirectoryNames =
        {
            "bin", "obj", ".vs", ".git", "packages", "node_modules", "TestResults", ".idea"
        };

        /// <summary>Maximum characters sent to the model per request; larger manifests are split into chunks.</summary>
        private const int MaxChunkChars = 120000;

        /// <summary>Maximum characters read from a single file (guards against huge generated files).</summary>
        private const int MaxFileChars = 20000;

        private static readonly Regex SeverityRegex = new Regex(
            @"^#{1,6}\s*\[?\s*(Critical|High|Medium|Low|Info)\b",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly IAiProvider _ai;

        /// <summary>Raised for every pipeline message emitted during the audit.</summary>
        public event EventHandler<string> MessageReported;

        public RobotSecurityAuditService(IAiProvider ai)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        /// <summary>
        /// Returns the full paths of robot solutions (<c>RPA-&lt;number&gt;.sln/.slnx</c>, uppercase prefix)
        /// located directly in <paramref name="directory"/> (non-recursive), sorted by name.
        /// </summary>
        public static IReadOnlyList<string> FindRobotSolutions(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            var files = new List<string>();
            foreach (var pattern in new[] { "*.sln", "*.slnx" })
            {
                try
                {
                    files.AddRange(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));
                }
                catch
                {
                    // Ignore unreadable directories; return whatever could be enumerated.
                }
            }

            return files
                .Where(f => IsRobotSolution(Path.GetFileName(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the full paths of robot solutions discovered across the robot folders that sit at
        /// the same level as <paramref name="directory"/>. Both the directory itself and its parent are
        /// inspected: flat <c>RPA-&lt;number&gt;.sln/.slnx</c> files in the directory, and the same files
        /// inside every subfolder whose name starts with an uppercase "RPA-" prefix. This makes the audit
        /// cover the currently open robot solution together with its sibling robot folders.
        /// Results are deduplicated and sorted by name.
        /// </summary>
        public static IReadOnlyList<string> FindRobotSolutionsInSiblingFolders(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            var roots = new List<string> { directory };
            var parent = Directory.GetParent(directory);
            if (parent != null)
                roots.Add(parent.FullName);

            var solutions = new List<string>();
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                solutions.AddRange(FindRobotSolutionsInRootAndRobotSubfolders(root));

            return solutions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Collects flat robot solutions from <paramref name="root"/> and from each of its subfolders
        /// whose name starts with an uppercase "RPA-" prefix.
        /// </summary>
        private static IEnumerable<string> FindRobotSolutionsInRootAndRobotSubfolders(string root)
        {
            var results = new List<string>(FindRobotSolutions(root));

            string[] subdirectories;
            try { subdirectories = Directory.GetDirectories(root); }
            catch { return results; }

            foreach (var sub in subdirectories)
            {
                var name = Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!name.StartsWith("RPA-", StringComparison.Ordinal))
                    continue;

                results.AddRange(FindRobotSolutions(sub));
            }

            return results;
        }

        /// <summary>
        /// Returns true when <paramref name="fileName"/> matches the robot solution pattern:
        /// starts with an uppercase "RPA-" followed by one or more digits, then ".sln" or ".slnx".
        /// </summary>
        public static bool IsRobotSolution(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            // The prefix must be exactly "RPA-" (uppercase).
            if (!fileName.StartsWith("RPA-", StringComparison.Ordinal)) return false;
            return SolutionNameRegex.IsMatch(fileName);
        }

        /// <summary>
        /// Audits each solution in <paramref name="solutionPaths"/> (read-only) and writes a combined
        /// Markdown report to <paramref name="reportPath"/>. The report is flushed after each solution
        /// so a mid-run failure does not lose already collected results.
        /// </summary>
        public async Task<RobotSecurityAuditResult> AuditAsync(
            IReadOnlyList<string> solutionPaths,
            string reportPath,
            IProgress<string> progress = null,
            IProgress<double> progressValue = null,
            CancellationToken cancellationToken = default,
            SecurityAuditMode mode = SecurityAuditMode.Full)
        {
            var result = new RobotSecurityAuditResult();
            if (string.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("Report path is required.", nameof(reportPath));

            var solutions = (solutionPaths ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (solutions.Count == 0)
                return result;

            var reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);

            Action<string> log = message =>
            {
                if (string.IsNullOrEmpty(message)) return;
                progress?.Report(message);
                MessageReported?.Invoke(this, message);
            };

            var sb = new StringBuilder();
            sb.AppendLine("# Security Audit Report");
            sb.AppendLine();
            sb.AppendLine("Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            sb.AppendLine(mode == SecurityAuditMode.CredentialsOnly
                ? "Mode: credentials-focused scan (passwords & logins, read-only, nothing executed)"
                : "Mode: full static analysis (read-only, nothing executed)");
            sb.AppendLine();
            sb.AppendLine("## Scope");
            sb.AppendLine();
            sb.AppendLine("Solutions audited (" + solutions.Count + "): " +
                          string.Join(", ", solutions.Select(Path.GetFileName)));
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Solution | Critical | High | Medium | Low | Info | Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|");

            // Write the header (with an empty summary table) first so the report exists even
            // when every solution is skipped (e.g. missing files).
            LineEndings.WriteAllText(reportPath, sb.ToString());

            progressValue?.Report(0);

            for (var i = 0; i < solutions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var solutionPath = solutions[i];
                var name = Path.GetFileName(solutionPath);

                if (!File.Exists(solutionPath))
                {
                    var warning = "Solution file not found: " + solutionPath;
                    result.Warnings.Add(warning);
                    log("Warning: " + warning);
                    continue;
                }

                log("Auditing robot solution " + name + " (" + (i + 1) + "/" + solutions.Count + ")...");

                string section;
                var status = "OK";
                try
                {
                    section = await AuditSolutionAsync(solutionPath, log, cancellationToken, mode).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.SolutionsFailed++;
                    result.Warnings.Add("Failed to audit " + name + ": " + ex.Message);
                    log("Audit failed for " + name + ": " + ex.Message);
                    status = "FAILED";
                    section = "### FAILED\n\n" + ex.Message;
                }

                result.SolutionsAudited++;

                var severities = CountSeverities(section);
                sb.AppendLine("| " + name + " | " +
                              severities["Critical"] + " | " +
                              severities["High"] + " | " +
                              severities["Medium"] + " | " +
                              severities["Low"] + " | " +
                              severities["Info"] + " | " +
                              status + " |");

                sb.AppendLine();
                sb.AppendLine("## " + name);
                sb.AppendLine();
                sb.AppendLine(section ?? string.Empty);
                sb.AppendLine();

                LineEndings.WriteAllText(reportPath, sb.ToString());
                progressValue?.Report((i + 1) * 100.0 / solutions.Count);
            }

            log("Security audit completed: " + result.SolutionsAudited + " solution(s) audited, " +
                result.SolutionsFailed + " failed.");
            return result;
        }

        /// <summary>
        /// Collects and analyzes one robot solution. Read-only: enumerates and reads files,
        /// sends a single (or chunked) manifest to the AI provider, and returns the Markdown findings.
        /// </summary>
        private async Task<string> AuditSolutionAsync(string solutionPath, Action<string> log, CancellationToken cancellationToken, SecurityAuditMode mode)
        {
            var solutionDirectory = Path.GetDirectoryName(solutionPath);
            var name = Path.GetFileName(solutionPath);
            var systemPrompt = mode == SecurityAuditMode.CredentialsOnly ? CredentialsOnlySystemPrompt : SystemPrompt;

            log("Collecting source files for " + name + "...");
            var files = CollectFiles(solutionDirectory);
            log("Collected " + files.Count + " file(s) for " + name + ".");

            if (files.Count == 0)
                throw new InvalidOperationException("No source files collected for " + name + ".");

            var chunks = ChunkFiles(files, MaxChunkChars);
            log("Built manifest for " + name + ": " + chunks.Sum(c => c.Length) + " chars in " + chunks.Count + " chunk(s).");

            var sections = new List<string>();
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                log("Analyzing " + name + " - chunk " + (chunkIndex + 1) + "/" + chunks.Count + "...");

                var content = chunks.Count == 1
                    ? chunks[chunkIndex]
                    : "Part " + (chunkIndex + 1) + " of " + chunks.Count + " of robot solution " + name + ":\n\n" + chunks[chunkIndex];

                var messages = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = content }
                };

                var response = await _ai.ChatAsync(messages, systemPrompt, cancellationToken).ConfigureAwait(false);
                var answer = response?.Answer?.Trim();
                if (string.IsNullOrWhiteSpace(answer))
                    throw new InvalidOperationException("The AI provider returned an empty response for " + name + ".");

                sections.Add(answer);
                log("Received " + answer.Length + " chars for " + name + " chunk " + (chunkIndex + 1) + "/" + chunks.Count + ".");
            }

            return sections.Count == 1 ? sections[0] : string.Join("\n\n", sections);
        }

        /// <summary>
        /// Recursively collects text files (source, project, solution and configuration files)
        /// under <paramref name="root"/>, skipping build/VCS/cache directories and unreadable files.
        /// </summary>
        private static List<RobotSourceFile> CollectFiles(string root)
        {
            var files = new List<RobotSourceFile>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return files;

            var baseDirectory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            try
            {
                foreach (var path in EnumerateFiles(root))
                {
                    var extension = Path.GetExtension(path);
                    if (!SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var full = Path.GetFullPath(path);
                        var relative = full.Length > baseDirectory.Length
                            ? full.Substring(baseDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            : Path.GetFileName(full);

                        var content = File.ReadAllText(full);
                        if (content.Length > MaxFileChars)
                            content = content.Substring(0, MaxFileChars) + "\n... (file truncated)";

                        files.Add(new RobotSourceFile { RelativePath = relative, Content = content });
                    }
                    catch
                    {
                        // Skip unreadable files; the audit proceeds with what is available.
                    }
                }
            }
            catch
            {
                // Directory enumeration failed; return whatever was collected.
            }

            return files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Enumerates files recursively, skipping excluded directory names.</summary>
        private static IEnumerable<string> EnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var directory = stack.Pop();

                string[] subdirectories;
                try { subdirectories = Directory.GetDirectories(directory); }
                catch { continue; }

                foreach (var sub in subdirectories)
                {
                    var name = Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        stack.Push(sub);
                }

                string[] files;
                try { files = Directory.GetFiles(directory); }
                catch { continue; }

                foreach (var file in files)
                    yield return file;
            }
        }

        /// <summary>
        /// Groups files into manifests where each chunk stays under <paramref name="maxChars"/>;
        /// chunk boundaries always fall between file entries, never inside one.
        /// </summary>
        private static List<string> ChunkFiles(IReadOnlyList<RobotSourceFile> files, int maxChars)
        {
            var chunks = new List<string>();
            var current = new StringBuilder();

            foreach (var file in files)
            {
                var entry = "### FILE: " + file.RelativePath + "\n" + (file.Content ?? string.Empty) + "\n\n";
                if (current.Length > 0 && current.Length + entry.Length > maxChars)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                current.Append(entry);
            }

            if (current.Length > 0)
                chunks.Add(current.ToString());

            return chunks;
        }

        /// <summary>Counts severity labels in a Markdown findings section (Critical/High/Medium/Low/Info).</summary>
        private static Dictionary<string, int> CountSeverities(string markdown)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "Critical", 0 }, { "High", 0 }, { "Medium", 0 }, { "Low", 0 }, { "Info", 0 }
            };

            if (string.IsNullOrWhiteSpace(markdown))
                return counts;

            foreach (Match match in SeverityRegex.Matches(markdown))
            {
                var severity = match.Groups[1].Value;
                var key = char.ToUpperInvariant(severity[0]) + severity.Substring(1).ToLowerInvariant();
                if (counts.ContainsKey(key))
                    counts[key]++;
            }

            return counts;
        }
    }
}
