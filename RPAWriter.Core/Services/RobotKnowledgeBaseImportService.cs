using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            foreach (var robotDir in robotDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var robotName = Path.GetFileName(robotDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                progress?.Report("Processing robot " + robotName + "…");

                var files = CollectSourceFiles(robotsRoot, robotDir, out var sourceUpdatedUtc);
                if (files.Count == 0)
                {
                    result.Warnings.Add("No .xaml/.cs files found for robot \"" + robotName + "\"; skipped.");
                    continue;
                }

                var generatedUtc = DateTime.UtcNow;

                var spec = await _ai.ReconstructSpecAsync(robotName, files, cancellationToken).ConfigureAwait(false)
                    ?? new SpecModel { Name = robotName };

                var specMd = MarkdownContent.SpecToMarkdown(spec, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-spec.md"), specMd);
                result.SpecFilesWritten++;

                var tasks = await _ai.DecomposeTasksAsync(spec.RawText ?? "", cancellationToken).ConfigureAwait(false);

                var entries = new List<RobotTaskEntry>();
                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var solution = await _ai.MapTaskToSolutionAsync(task, files, cancellationToken).ConfigureAwait(false);
                    solution.Name = robotName + " — " + (string.IsNullOrWhiteSpace(task?.Description) ? task?.Action : task.Description);
                    solution.SourceRobot = robotName;
                    solution.SpecGeneratedUtc = generatedUtc;
                    solution.CreatedUtc = generatedUtc;
                    solution.SourceUpdatedUtc = sourceUpdatedUtc;
                    await kb.AddAsync(solution, cancellationToken).ConfigureAwait(false);
                    result.TasksAdded++;

                    entries.Add(new RobotTaskEntry
                    {
                        Task = task,
                        SolutionPaths = solution.SolutionPaths ?? new List<string>()
                    });
                }

                var tasksMd = MarkdownContent.RobotTasksToMarkdown(robotName, entries, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-tasks.md"), tasksMd);
                result.TaskFilesWritten++;

                result.RobotsProcessed++;
            }

            return result;
        }

        private static List<RobotSourceFile> CollectSourceFiles(string robotsRoot, string robotDir, out DateTime? sourceUpdatedUtc)
        {
            sourceUpdatedUtc = null;
            var files = new List<RobotSourceFile>();
            var baseDir = Path.GetFullPath(robotsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var path in Directory.EnumerateFiles(robotDir, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (!SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var full = Path.GetFullPath(path);
                    var relative = full.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

            return files;
        }
    }
}
