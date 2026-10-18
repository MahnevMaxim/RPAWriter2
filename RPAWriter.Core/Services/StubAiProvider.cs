using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Deterministic AI provider stub (offline/demo mode).
    /// Splits text into sentences and maps activity types by keyword.
    /// </summary>
    public class StubAiProvider : IAiProvider
    {
        private static readonly Dictionary<string, string> ActivityMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "open", "OpenBrowser" },
                { "click", "ClickElement" },
                { "press", "ClickElement" },
                { "read", "ReadTable" },
                { "fill", "FillField" },
                { "send", "Submit" },
                { "write", "WriteExcel" },
                { "save", "WriteExcel" }
            };

        public async Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
        {
            List<TaskDescriptor> tasks;
            if (spec?.Tasks != null && spec.Tasks.Count > 0)
                tasks = spec.Tasks.Select(CloneTask).ToList();
            else
                tasks = (await DecomposeTasksAsync(spec?.RawText ?? "", cancellationToken)).Select(CloneTask).ToList();

            return new SpecAnalysis
            {
                Summary = "Stub: spec analysis \"" + (spec?.Name ?? "") + "\"",
                Tasks = tasks
            };
        }

        public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var plan = new RobotPlan { Name = "Robot" };
            var root = new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Main" };
            root.Children.Add(new ActivityNode { Kind = ActivityNodeKind.WriteLine, Expression = "Robot start" });
            foreach (var task in analysis?.Tasks ?? new List<TaskDescriptor>())
            {
                root.Children.Add(new ActivityNode
                {
                    Kind = ActivityNodeKind.InvokeMethod,
                    Name = task.Description ?? task.Action,
                    ClassName = "RpaActions",
                    MethodName = MethodNameFor(task),
                    Arguments = new List<ArgumentAssignment>
                    {
                        new ArgumentAssignment { Parameter = "target", Value = task.Target }
                    }
                });
            }
            plan.Root = root;
            return Task.FromResult(plan);
        }

        public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
        {
            var tasks = new List<TaskDescriptor>();
            if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<IReadOnlyList<TaskDescriptor>>(tasks);

            foreach (var sentence in Regex.Split(text, @"(?<=[.!?;])\s+"))
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                var action = GuessAction(sentence);
                tasks.Add(new TaskDescriptor
                {
                    Action = action,
                    Target = sentence.Trim(),
                    Description = sentence.Trim(),
                    ActivityType = ActivityMap.ContainsKey(action) ? ActivityMap[action] : "Custom",
                    Keywords = TextNormalizer.Tokenize(sentence).ToList()
                });
            }
            return Task.FromResult<IReadOnlyList<TaskDescriptor>>(tasks);
        }

        public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
        {
            var files = (robotFiles ?? new List<RobotSourceFile>()).Where(f => f != null).ToList();
            var spec = new SpecModel
            {
                Name = robotName ?? "Robot",
                SourcePath = robotName,
                Description = "Робот \"" + (robotName ?? "Robot") + "\" восстановлен по " + files.Count + " исходным файлам."
            };

            var sb = new StringBuilder();
            sb.AppendLine(spec.Description);
            foreach (var file in files)
            {
                spec.Steps.Add(file.RelativePath);
                sb.AppendLine("Файл: " + file.RelativePath + ".");
                foreach (var method in ExtractMethodNames(file.Content))
                    sb.AppendLine("Реализует метод " + method + ".");
            }
            spec.RawText = sb.ToString();
            return Task.FromResult(spec);
        }

        public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
        {
            var method = MethodNameFor(task);
            var files = (robotFiles ?? new List<RobotSourceFile>()).Where(f => f != null).ToList();
            var paths = files
                .Where(f => !string.IsNullOrEmpty(f.Content) && f.Content.IndexOf(method, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(f => f.RelativePath)
                .ToList();
            if (paths.Count == 0 && files.Count > 0)
                paths.Add(files[0].RelativePath);

            var record = new SolutionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = task?.Description ?? task?.Action ?? "Task",
                Tasks = new List<TaskDescriptor> { CloneTask(task) },
                XamlFragment = "<InvokeMethod MethodName=\"" + method + "\" TargetType=\"Robots.RpaActions\" />",
                CSharpCode = "public static void " + method + "() { /* TODO */ }",
                SolutionPaths = paths
            };
            return Task.FromResult(record);
        }

        private static IEnumerable<string> ExtractMethodNames(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return Enumerable.Empty<string>();
            return Regex.Matches(content, @"\b[A-Za-z_][A-Za-z0-9_]*\s*\(")
                .Cast<Match>()
                .Select(m => m.Value.TrimEnd('(').Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
        {
            return ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = message } },
                "",
                cancellationToken);
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
        {
            var lastUser = (messages ?? new List<ChatMessageData>())
                .LastOrDefault(m => m != null && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
            var text = lastUser?.Content ?? "";
            return Task.FromResult(new ChatResponse
            {
                Reasoning = "",
                Answer = "Заглушка: получено сообщение \"" + text + "\". Настройте OpenAI-совместимый провайдер на вкладке Settings."
            });
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            return ChatAsync(messages, systemPrompt, cancellationToken);
        }

        private static string MethodNameFor(TaskDescriptor task)
        {
            var action = task?.Action ?? "Run";
            if (ActivityMap.TryGetValue(action, out var mapped)) return mapped;
            var id = new string(action.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(id) ? "Run" : id;
        }

        private static string GuessAction(string text)
        {
            foreach (var kv in ActivityMap)
                if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Key;
            return "run";
        }

        private static TaskDescriptor CloneTask(TaskDescriptor source)
        {
            if (source == null) return null;
            var t = new TaskDescriptor
            {
                Action = source.Action,
                Target = source.Target,
                Description = source.Description,
                ActivityType = source.ActivityType,
                SapTransactionCode = source.SapTransactionCode,
                SapActionKind = source.SapActionKind
            };
            if (source.Inputs != null) t.Inputs.AddRange(source.Inputs);
            if (source.Outputs != null) t.Outputs.AddRange(source.Outputs);
            if (source.Keywords != null) t.Keywords.AddRange(source.Keywords);
            return t;
        }
    }
}
