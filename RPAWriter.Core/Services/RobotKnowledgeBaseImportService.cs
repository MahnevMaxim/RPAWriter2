using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Imports the knowledge base from a directory of robots. For each robot folder it
    /// reconstructs the technical specification from the robot's source code (.xaml/.cs),
    /// decomposes it into tasks, and records the solution file(s) where each task is implemented.
    /// </summary>
    public class RobotKnowledgeBaseImportService
    {
        private static readonly string[] SourceExtensions = { ".xaml", ".cs" };
        private static readonly string[] ProjectExtensions = { ".sln", ".csproj", ".wfproj" };

        private static readonly Regex XamlReferenceRegex = new Regex(@"([A-Za-z0-9_\-\.]+\.xaml)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TypeAttributeRegex = new Regex(@"(?:TargetType|ClassName|TargetTypeName|ResultType|x:Class)\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MethodNameAttributeRegex = new Regex(@"MethodName\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NewObjectRegex = new Regex(@"\bnew\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex MethodCallRegex = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex ClassDeclRegex = new Regex(@"\b(?:class|interface|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex MethodDeclRegex = new Regex(@"(?:^|\s)(?:public|private|protected|internal)?\s*(?:static|async|override|virtual|sealed)?\s*[A-Za-z0-9_<>\[\]\.]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "while", "foreach", "switch", "catch", "using", "typeof", "sizeof",
            "nameof", "await", "throw", "lock", "fixed", "checked", "unchecked", "default",
            "delegate", "is", "as", "get", "set", "add", "remove", "this", "base", "new",
            "return", "try", "finally", "else", "do", "in", "out", "ref", "params", "var"
        };

        private readonly IAiProvider _ai;

        public RobotKnowledgeBaseImportService(IAiProvider ai)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        /// <summary>
        /// Walks the robot folders under <paramref name="robotsRoot"/>, and for each robot writes
        /// <c>&lt;robot&gt;-spec.md</c> and <c>&lt;robot&gt;-tasks.md</c> into <paramref name="kbDirectory"/>
        /// and updates the knowledge-base index.
        /// </summary>
        public async Task<RobotKnowledgeBaseImportResult> ImportFromRobotsAsync(
            string robotsRoot,
            string kbDirectory,
            IProgress<string> progress = null,
            IProgress<double> progressValue = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RobotKnowledgeBaseImportResult();
            if (string.IsNullOrWhiteSpace(robotsRoot) || !Directory.Exists(robotsRoot))
                return result;
            if (string.IsNullOrWhiteSpace(kbDirectory))
                throw new ArgumentException("Knowledge base directory is required.", nameof(kbDirectory));

            Directory.CreateDirectory(kbDirectory);
            var kb = new FileKnowledgeBase(kbDirectory);

            var robotDirs = Directory.EnumerateDirectories(robotsRoot, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            progressValue?.Report(0);

            for (var i = 0; i < robotDirs.Count; i++)
            {
                var robotDir = robotDirs[i];
                cancellationToken.ThrowIfCancellationRequested();
                var robotName = Path.GetFileName(robotDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                progress?.Report("Processing robot " + robotName + "…");

                var input = CollectRobotInput(robotsRoot, robotDir, robotName, progress, result.Warnings);
                var files = input.Files;
                if (files.Count == 0)
                {
                    result.Warnings.Add("No .xaml/.cs files found for robot \"" + robotName + "\"; skipped.");
                    progressValue?.Report((i + 1) * 100.0 / robotDirs.Count);
                    continue;
                }

                var generatedUtc = DateTime.UtcNow;

                progress?.Report("Reconstructing spec for robot " + robotName + "…");
                var spec = await _ai.ReconstructSpecAsync(robotName, files, cancellationToken, input.Platform).ConfigureAwait(false)
                    ?? new SpecModel { Name = robotName };

                progress?.Report("Forming " + robotName + "-spec.md…");
                var specMd = MarkdownContent.SpecToMarkdown(spec, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-spec.md"), specMd);
                result.SpecFilesWritten++;

                progress?.Report("Decomposing tasks for robot " + robotName + "…");
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText ?? "", cancellationToken).ConfigureAwait(false);

                var entries = new List<RobotTaskEntry>();
                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var taskLabel = string.IsNullOrWhiteSpace(task?.Description) ? (task?.Action ?? "task") : task.Description;
                    progress?.Report("Mapping solution for task \"" + taskLabel + "\"…");
                    var solution = await _ai.MapTaskToSolutionAsync(task, files, cancellationToken, input.Platform).ConfigureAwait(false);
                    solution.Name = robotName + " — " + (string.IsNullOrWhiteSpace(task?.Description) ? task?.Action : task.Description);
                    solution.SourceRobot = robotName;
                    solution.SpecGeneratedUtc = generatedUtc;
                    solution.CreatedUtc = generatedUtc;
                    solution.SourceUpdatedUtc = input.SourceUpdatedUtc;
                    await kb.AddAsync(solution, cancellationToken).ConfigureAwait(false);
                    result.TasksAdded++;

                    entries.Add(new RobotTaskEntry
                    {
                        Task = task,
                        SolutionPaths = solution.SolutionPaths ?? new List<string>()
                    });
                }

                progress?.Report("Forming " + robotName + "-tasks.md…");
                var tasksMd = MarkdownContent.RobotTasksToMarkdown(robotName, entries, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-tasks.md"), tasksMd);
                result.TaskFilesWritten++;

                result.RobotsProcessed++;
                progressValue?.Report((i + 1) * 100.0 / robotDirs.Count);
            }

            progressValue?.Report(100);
            return result;
        }

        private static RobotInput CollectRobotInput(string robotsRoot, string robotDir, string robotName, IProgress<string> progress, List<string> warnings)
        {
            warnings = warnings ?? new List<string>();
            var input = new RobotInput { RobotName = robotName };
            var baseDir = Path.GetFullPath(robotsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 1. Locate the project folder named after the robot inside the solution folder;
            //    auxiliary projects in the solution are intentionally ignored.
            var projectRoot = FindProjectFolder(robotDir, robotName);
            if (projectRoot == null)
            {
                warnings.Add("Project folder \"" + robotName + "\" not found inside solution \"" + robotName + "\"; using the solution root.");
                projectRoot = robotDir;
            }

            // 2. Read all source files of the project (used for platform detection and reference resolution).
            input.Files = CollectFiles(projectRoot, baseDir, out var sourceUpdatedUtc, progress);
            input.SourceUpdatedUtc = sourceUpdatedUtc;

            // 3. Detect the robot platform.
            input.Platform = DetectPlatform(robotDir, projectRoot, input.Files);

            // 4. Follow the call graph starting from the entry point (main.xaml).
            input.Files = BuildClosure(input.Files, progress, warnings);

            return input;
        }

        private static string FindProjectFolder(string solutionDir, string robotName)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(solutionDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.Equals(name, robotName, StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch
            {
                // fall through and let the caller fall back to the solution root
            }
            return null;
        }

        private static List<RobotSourceFile> CollectFiles(string root, string baseDir, out DateTime? sourceUpdatedUtc, IProgress<string> progress)
        {
            sourceUpdatedUtc = null;
            var files = new List<RobotSourceFile>();
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(path);
                    if (!SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var full = Path.GetFullPath(path);
                        var relative = full.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        progress?.Report("Researching " + relative + "…");
                        files.Add(new RobotSourceFile { RelativePath = relative, Content = File.ReadAllText(full) });

                        var lastWrite = File.GetLastWriteTimeUtc(full);
                        if (sourceUpdatedUtc == null || lastWrite > sourceUpdatedUtc)
                            sourceUpdatedUtc = lastWrite;
                    }
                    catch
                    {
                        // Skip unreadable files; the robot is still processed with what is available.
                    }
                }
            }
            catch
            {
                // Directory enumeration failed; return whatever was collected.
            }
            return files;
        }

        private static RobotPlatform DetectPlatform(string solutionDir, string projectRoot, IReadOnlyList<RobotSourceFile> files)
        {
            bool HasProjectFile(string dir)
            {
                if (string.IsNullOrEmpty(dir)) return false;
                try
                {
                    return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Any(p => ProjectExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
                }
                catch { return false; }
            }

            bool HasWfProject(string dir)
            {
                if (string.IsNullOrEmpty(dir)) return false;
                try
                {
                    return Directory.EnumerateFiles(dir, "*.wfproj", SearchOption.AllDirectories).Any();
                }
                catch { return false; }
            }

            var hasVsProject = HasProjectFile(solutionDir) || HasProjectFile(projectRoot);
            var hasWf = HasWfProject(solutionDir) || HasWfProject(projectRoot);
            var hasMainXaml = files.Any(f => string.Equals(Path.GetFileName(f.RelativePath), "main.xaml", StringComparison.OrdinalIgnoreCase));
            var hasCs = files.Any(f => IsCs(f.RelativePath));
            var hasXaml = files.Any(f => IsXaml(f.RelativePath));

            if (hasVsProject)
            {
                if (hasWf || hasMainXaml) return RobotPlatform.Mwf;
                return RobotPlatform.Wr;
            }
            if (hasXaml && !hasCs) return RobotPlatform.Pix;
            return RobotPlatform.UiPath;
        }

        private static List<RobotSourceFile> BuildClosure(List<RobotSourceFile> projectFiles, IProgress<string> progress, List<string> warnings)
        {
            if (projectFiles.Count == 0) return projectFiles;

            var entry = projectFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f.RelativePath), "main.xaml", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                warnings.Add("Entry point \"main.xaml\" not found; the agent will pick the entry point itself.");
                return projectFiles
                    .Select(f => new RobotSourceFile { RelativePath = f.RelativePath, Content = "" })
                    .ToList();
            }

            var xamlIndex = IndexBy(projectFiles, f => IsXaml(f.RelativePath), f => new[] { Path.GetFileNameWithoutExtension(f.RelativePath) });
            var classIndex = IndexBy(projectFiles, f => IsCs(f.RelativePath), f => ExtractDeclaredClassNames(f.Content));
            var methodIndex = IndexBy(projectFiles, f => IsCs(f.RelativePath), f => ExtractDeclaredMethodNames(f.Content));

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var warnedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var closure = new List<RobotSourceFile>();
            var queue = new Queue<RobotSourceFile>();
            queue.Enqueue(entry);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(Normalize(current.RelativePath))) continue;
                closure.Add(current);
                progress?.Report("Researching " + current.RelativePath + "…");

                EnqueueXamlReferences(current, xamlIndex, queue, visited, warnings, warnedMissing);
                EnqueueMatches(classIndex, ExtractTypeReferences(current.Content), queue, visited);
                EnqueueMatches(methodIndex, ExtractMethodReferences(current.Content), queue, visited);
            }

            return closure;
        }

        private static void EnqueueXamlReferences(RobotSourceFile file, ILookup<string, RobotSourceFile> index, Queue<RobotSourceFile> queue, HashSet<string> visited, List<string> warnings, HashSet<string> warnedMissing)
        {
            foreach (var key in ExtractXamlReferences(file.Content))
            {
                var matches = index[Normalize(key)].ToList();
                if (matches.Count == 0)
                {
                    if (warnedMissing.Add(key))
                        warnings.Add("Referenced workflow \"" + key + ".xaml\" (from " + file.RelativePath + ") was not found in the project.");
                    continue;
                }
                foreach (var f in matches)
                    if (!visited.Contains(Normalize(f.RelativePath)))
                        queue.Enqueue(f);
            }
        }

        private static void EnqueueMatches(ILookup<string, RobotSourceFile> index, IEnumerable<string> keys, Queue<RobotSourceFile> queue, HashSet<string> visited)
        {
            foreach (var key in keys)
            {
                foreach (var f in index[Normalize(key)])
                    if (!visited.Contains(Normalize(f.RelativePath)))
                        queue.Enqueue(f);
            }
        }

        private static ILookup<string, RobotSourceFile> IndexBy(IEnumerable<RobotSourceFile> files, Func<RobotSourceFile, bool> predicate, Func<RobotSourceFile, IEnumerable<string>> keySelector)
        {
            var entries = new List<KeyValuePair<string, RobotSourceFile>>();
            foreach (var f in files)
            {
                if (!predicate(f)) continue;
                foreach (var key in keySelector(f))
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    entries.Add(new KeyValuePair<string, RobotSourceFile>(Normalize(key), f));
                }
            }
            return entries.ToLookup(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ExtractXamlReferences(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in XamlReferenceRegex.Matches(content))
            {
                var name = Path.GetFileNameWithoutExtension(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    yield return name;
            }
        }

        private static IEnumerable<string> ExtractTypeReferences(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in TypeAttributeRegex.Matches(content))
            {
                var type = m.Groups[1].Value.Trim();
                var shortName = type.Contains('.') ? type.Substring(type.LastIndexOf('.') + 1) : type;
                if (!string.IsNullOrWhiteSpace(shortName) && seen.Add(shortName))
                    yield return shortName;
            }
            foreach (Match m in NewObjectRegex.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    yield return name;
            }
        }

        private static IEnumerable<string> ExtractMethodReferences(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in MethodNameAttributeRegex.Matches(content))
            {
                var name = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    yield return name;
            }
            foreach (Match m in MethodCallRegex.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (IsCSharpKeyword(name)) continue;
                if (seen.Add(name))
                    yield return name;
            }
        }

        private static IEnumerable<string> ExtractDeclaredClassNames(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in ClassDeclRegex.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    yield return name;
            }
        }

        private static IEnumerable<string> ExtractDeclaredMethodNames(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in MethodDeclRegex.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (IsCSharpKeyword(name)) continue;
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    yield return name;
            }
        }

        private static bool IsCSharpKeyword(string name)
        {
            return CSharpKeywords.Contains(name);
        }

        private static bool IsXaml(string path) => path != null && path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);

        private static bool IsCs(string path) => path != null && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) => (path ?? "").Replace('\\', '/');

        private sealed class RobotInput
        {
            public string RobotName { get; set; }
            public RobotPlatform Platform { get; set; }
            public List<RobotSourceFile> Files { get; set; } = new List<RobotSourceFile>();
            public DateTime? SourceUpdatedUtc { get; set; }
        }
    }
}
