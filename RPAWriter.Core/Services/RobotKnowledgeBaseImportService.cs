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
    /// Импортирует базу знаний из каталога роботов. Для каждой папки робота
    /// восстанавливает техническое задание из исходного кода робота (.xaml/.cs),
    /// декомпозирует его на задачи и записывает файл(ы) решения, где реализована каждая задача.
    /// </summary>
    public class RobotKnowledgeBaseImportService
    {
        /// <summary>Расширения исходных файлов, которые учитываются при импорте.</summary>
        private static readonly string[] SourceExtensions = { ".xaml", ".cs" };

        /// <summary>Расширения файлов проекта, по которым определяется платформа робота.</summary>
        private static readonly string[] ProjectExtensions = { ".sln", ".csproj" };

        /// <summary>Находит ссылки на файлы рабочих процессов .xaml в тексте.</summary>
        private static readonly Regex XamlReferenceRegex = new Regex(@"([A-Za-z0-9_\-\.]+\.xaml)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Находит значения атрибутов типов (TargetType, ClassName, x:Class и т.п.) в XAML.</summary>
        private static readonly Regex TypeAttributeRegex = new Regex(@"(?:TargetType|ClassName|TargetTypeName|ResultType|x:Class)\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Находит значения атрибута MethodName в XAML.</summary>
        private static readonly Regex MethodNameAttributeRegex = new Regex(@"MethodName\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Находит конструкции new Тип(...) в C#-коде.</summary>
        private static readonly Regex NewObjectRegex = new Regex(@"\bnew\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        /// <summary>Находит вызовы методов по имени в коде.</summary>
        private static readonly Regex MethodCallRegex = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        /// <summary>Находит объявления классов, интерфейсов, структур и перечислений.</summary>
        private static readonly Regex ClassDeclRegex = new Regex(@"\b(?:class|interface|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        /// <summary>Находит объявления методов в C#-коде.</summary>
        private static readonly Regex MethodDeclRegex = new Regex(@"(?:^|\s)(?:public|private|protected|internal)?\s*(?:static|async|override|virtual|sealed)?\s*[A-Za-z0-9_<>\[\]\.]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Ключевые слова C#, которые не считаются именами методов или классов.</summary>
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "while", "foreach", "switch", "catch", "using", "typeof", "sizeof",
            "nameof", "await", "throw", "lock", "fixed", "checked", "unchecked", "default",
            "delegate", "is", "as", "get", "set", "add", "remove", "this", "base", "new",
            "return", "try", "finally", "else", "do", "in", "out", "ref", "params", "var"
        };

        /// <summary>AI-провайдер для реконструкции ТЗ, декомпозиции задач и маппинга решений.</summary>
        private readonly IAiProvider _ai;

        /// <summary>Raised for every progress/result message emitted during the import.</summary>
        public event EventHandler<string> MessageReported;

        /// <summary>
        /// Создаёт сервис импорта базы знаний из роботов.
        /// </summary>
        /// <param name="ai">AI-провайдер, используемый для реконструкции ТЗ и декомпозиции задач.</param>
        public RobotKnowledgeBaseImportService(IAiProvider ai)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        /// <summary>
        /// Обходит папки роботов внутри <paramref name="robotsRoot"/> и для каждого робота
        /// записывает <c>&lt;робот&gt;-spec.md</c> и <c>&lt;робот&gt;-tasks.md</c> в
        /// <paramref name="kbDirectory"/>, а также обновляет индекс базы знаний.
        /// </summary>
        /// <param name="robotsRoot">Корневой каталог, содержащий папки роботов.</param>
        /// <param name="kbDirectory">Каталог базы знаний, куда пишутся сформированные файлы.</param>
        /// <param name="progress">Приёмник текстовых сообщений о ходе импорта.</param>
        /// <param name="progressValue">Приёмник прогресса в процентах (0–100).</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Результат импорта: обработанные роботы, записанные файлы и предупреждения.</returns>
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

            Action<string> report = message =>
            {
                if (string.IsNullOrEmpty(message)) return;
                progress?.Report(message);
                MessageReported?.Invoke(this, message);
            };

            var robotDirs = Directory.EnumerateDirectories(robotsRoot, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            progressValue?.Report(0);

            for (var i = 0; i < robotDirs.Count; i++)
            {
                var robotDir = robotDirs[i];
                cancellationToken.ThrowIfCancellationRequested();
                var robotName = Path.GetFileName(robotDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                report("Processing robot " + robotName + "…");

                var input = CollectRobotInput(robotsRoot, robotDir, robotName, report, result.Warnings);
                var files = input.Files;
                if (files.Count == 0)
                {
                    result.Warnings.Add("No .xaml/.cs files found for robot \"" + robotName + "\"; skipped.");
                    progressValue?.Report((i + 1) * 100.0 / robotDirs.Count);
                    continue;
                }

                var generatedUtc = DateTime.UtcNow;

                report("Reconstructing spec for robot " + robotName + "…");
                var spec = await _ai.ReconstructSpecAsync(robotName, files, cancellationToken, input.Platform).ConfigureAwait(false)
                    ?? new SpecModel { Name = robotName };

                report("Forming " + robotName + "-spec.md…");
                var specMd = MarkdownContent.SpecToMarkdown(spec, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-spec.md"), specMd);
                result.SpecFilesWritten++;

                report("Decomposing tasks for robot " + robotName + "…");
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText ?? "", cancellationToken).ConfigureAwait(false);

                var entries = new List<RobotTaskEntry>();
                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var taskLabel = string.IsNullOrWhiteSpace(task?.Description) ? (task?.Action ?? "task") : task.Description;
                    report("Mapping solution for task \"" + taskLabel + "\"…");
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

                report("Forming " + robotName + "-tasks.md…");
                var tasksMd = MarkdownContent.RobotTasksToMarkdown(robotName, entries, generatedUtc);
                File.WriteAllText(Path.Combine(kbDirectory, robotName + "-tasks.md"), tasksMd);
                result.TaskFilesWritten++;

                result.RobotsProcessed++;
                progressValue?.Report((i + 1) * 100.0 / robotDirs.Count);
            }

            progressValue?.Report(100);
            return result;
        }

        /// <summary>
        /// Собирает исходные данные одного робота: папку проекта, исходные файлы,
        /// платформу и замыкание файлов, достижимых из точки входа.
        /// </summary>
        private static RobotInput CollectRobotInput(string robotsRoot, string robotDir, string robotName, Action<string> report, List<string> warnings)
        {
            warnings = warnings ?? new List<string>();
            var input = new RobotInput { RobotName = robotName };
            var baseDir = Path.GetFullPath(robotsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            report?.Invoke("CollectRobotInput: gathering input for robot \"" + robotName + "\"…");

            // 1. Находим папку проекта, названную по имени робота, внутри папки решения;
            //    вспомогательные проекты в решении намеренно игнорируются.
            var projectRoot = FindProjectFolder(robotDir, robotName);
            if (projectRoot == null)
            {
                warnings.Add("Project folder \"" + robotName + "\" not found inside solution \"" + robotName + "\"; using the solution root.");
                projectRoot = robotDir;
            }

            // 2. Читаем все исходные файлы проекта (используются для определения платформы и разрешения ссылок).
            input.Files = CollectFiles(projectRoot, baseDir, out var sourceUpdatedUtc, report);
            input.SourceUpdatedUtc = sourceUpdatedUtc;
            report?.Invoke("CollectFiles: " + input.Files.Count + " source file(s) found.");

            // 3. Определяем платформу робота.
            input.Platform = DetectPlatform(robotDir, projectRoot, input.Files);
            report?.Invoke("DetectPlatform: " + input.Platform + ".");

            // 4. Проходим граф вызовов, начиная с точки входа (main.xaml).
            input.Files = BuildClosure(input.Files, report, warnings);
            report?.Invoke("CollectRobotInput: " + input.Files.Count + " file(s) collected.");

            return input;
        }

        /// <summary>
        /// Ищет папку проекта, названную по имени робота, среди вложенных папок решения.
        /// </summary>
        /// <returns>Путь к папке проекта или null, если она не найдена.</returns>
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
                // пропускаем и позволяем вызывающему коду вернуться к корню решения
            }
            return null;
        }

        /// <summary>
        /// Рекурсивно собирает исходные файлы (.xaml/.cs) из каталога и вычисляет
        /// время последнего изменения источников.
        /// </summary>
        private static List<RobotSourceFile> CollectFiles(string root, string baseDir, out DateTime? sourceUpdatedUtc, Action<string> report)
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
                        report?.Invoke("Researching " + relative + "…");
                        files.Add(new RobotSourceFile { RelativePath = relative, Content = File.ReadAllText(full) });

                        var lastWrite = File.GetLastWriteTimeUtc(full);
                        if (sourceUpdatedUtc == null || lastWrite > sourceUpdatedUtc)
                            sourceUpdatedUtc = lastWrite;
                    }
                    catch
                    {
                        // Пропускаем нечитаемые файлы; робот обрабатывается с тем, что доступно.
                    }
                }
            }
            catch
            {
                // Перечисление каталога не удалось; возвращаем то, что удалось собрать.
            }
            return files;
        }

        /// <summary>
        /// Определяет платформу робота (MWF, UiPath, PIX или WR) по наличию файлов проекта
        /// и составу исходных файлов.
        /// </summary>
        private static RobotPlatform DetectPlatform(string solutionDir, string projectRoot, IReadOnlyList<RobotSourceFile> files)
        {
            // Проверяет наличие файлов проекта (.sln/.csproj) в каталоге.
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

            var hasVsProject = HasProjectFile(solutionDir) || HasProjectFile(projectRoot);
            var hasCs = files.Any(f => IsCs(f.RelativePath));
            var hasXaml = files.Any(f => IsXaml(f.RelativePath));

            if (hasVsProject)
            {
                // Проект Visual Studio с XAML-воркфлоу — это Workflow Foundation (MWF);
                // проект без XAML — чисто кодовый (WR).
                return hasXaml ? RobotPlatform.Mwf : RobotPlatform.Wr;
            }
            if (hasXaml && !hasCs) return RobotPlatform.Pix;
            return RobotPlatform.UiPath;
        }

        /// <summary>
        /// Строит замыкание достижимых файлов, обходя граф вызовов от точки входа (main.xaml).
        /// </summary>
        private static List<RobotSourceFile> BuildClosure(List<RobotSourceFile> projectFiles, Action<string> report, List<string> warnings)
        {
            if (projectFiles.Count == 0) return projectFiles;

            var entry = projectFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f.RelativePath), "main.xaml", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                warnings.Add("Entry point \"main.xaml\" not found; the agent will pick the entry point itself.");
                var all = projectFiles
                    .Select(f => new RobotSourceFile { RelativePath = f.RelativePath, Content = "" })
                    .ToList();
                report?.Invoke("BuildClosure: entry point \"main.xaml\" not found; including " + all.Count + " file(s).");
                return all;
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
                report?.Invoke("Researching " + current.RelativePath + "…");

                EnqueueXamlReferences(current, xamlIndex, queue, visited, warnings, warnedMissing, report);
                EnqueueMatches(classIndex, ExtractTypeReferences(current.Content), queue, visited);
                EnqueueMatches(methodIndex, ExtractMethodReferences(current.Content), queue, visited);
            }

            report?.Invoke("BuildClosure: " + closure.Count + " reachable file(s).");
            return closure;
        }

        /// <summary>
        /// Добавляет в очередь файлы рабочих процессов, на которые ссылается указанный XAML-файл.
        /// </summary>
        private static void EnqueueXamlReferences(RobotSourceFile file, ILookup<string, RobotSourceFile> index, Queue<RobotSourceFile> queue, HashSet<string> visited, List<string> warnings, HashSet<string> warnedMissing, Action<string> report)
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
                {
                    if (!visited.Contains(Normalize(f.RelativePath)))
                    {
                        queue.Enqueue(f);
                        report?.Invoke("EnqueueXamlReferences: \"" + key + ".xaml\" → enqueueing " + f.RelativePath + ".");
                    }
                }
            }
        }

        /// <summary>
        /// Добавляет в очередь файлы из индекса, соответствующие переданным ключам (типам/методам).
        /// </summary>
        private static void EnqueueMatches(ILookup<string, RobotSourceFile> index, IEnumerable<string> keys, Queue<RobotSourceFile> queue, HashSet<string> visited)
        {
            foreach (var key in keys)
            {
                foreach (var f in index[Normalize(key)])
                    if (!visited.Contains(Normalize(f.RelativePath)))
                        queue.Enqueue(f);
            }
        }

        /// <summary>
        /// Строит индекс файлов по ключам, извлекаемым из каждого файла селектором ключей.
        /// </summary>
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

        /// <summary>Извлекает из содержимого имена файлов .xaml, на которые есть ссылки.</summary>
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

        /// <summary>Извлекает из содержимого имена типов (атрибуты типов и конструкции new Тип(...)).</summary>
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

        /// <summary>Извлекает из содержимого имена методов (атрибуты MethodName и вызовы методов).</summary>
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

        /// <summary>Извлекает из содержимого имена объявленных классов и интерфейсов.</summary>
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

        /// <summary>Извлекает из содержимого имена объявленных методов.</summary>
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

        /// <summary>Проверяет, является ли имя ключевым словом C#.</summary>
        private static bool IsCSharpKeyword(string name)
        {
            return CSharpKeywords.Contains(name);
        }

        /// <summary>Проверяет, является ли путь файлом XAML (.xaml).</summary>
        private static bool IsXaml(string path) => path != null && path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);

        /// <summary>Проверяет, является ли путь файлом C# (.cs).</summary>
        private static bool IsCs(string path) => path != null && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        /// <summary>Приводит путь к единому виду, заменяя обратные слэши на прямые.</summary>
        private static string Normalize(string path) => (path ?? "").Replace('\\', '/');

        /// <summary>Собранные исходные данные одного робота.</summary>
        private sealed class RobotInput
        {
            /// <summary>Имя робота (имя папки решения).</summary>
            public string RobotName { get; set; }

            /// <summary>Определённая платформа робота.</summary>
            public RobotPlatform Platform { get; set; }

            /// <summary>Достижимые исходные файлы робота.</summary>
            public List<RobotSourceFile> Files { get; set; } = new List<RobotSourceFile>();

            /// <summary>Время последнего изменения исходных файлов (UTC).</summary>
            public DateTime? SourceUpdatedUtc { get; set; }
        }
    }
}
