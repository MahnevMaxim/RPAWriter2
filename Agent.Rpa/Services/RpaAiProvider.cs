using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;

namespace Agent.Rpa.Services
{
    /// <summary>
    /// RPA-domain AI provider: spec analysis, task decomposition, task-to-solution
    /// mapping, robot plan generation and algorithm drafting. Built on top of the
    /// generic <see cref="OpenAiCompatibleProvider"/>.
    /// </summary>
    public class RpaAiProvider : OpenAiCompatibleProvider, IRpaAiProvider, IAlgorithmGenerator
    {
        private RpaPromptSet _rpaPrompts;

        public RpaAiProvider(OpenAiOptions options) : this(options, new HttpClient()) { }

        public RpaAiProvider(OpenAiOptions options, HttpClient http) : base(options, http)
        {
            _rpaPrompts = RpaPromptSet.Load(options.PromptsPath);
        }

        public override void ReloadPromptsFromDisk()
        {
            base.ReloadPromptsFromDisk();
            _rpaPrompts = RpaPromptSet.Load(_options.PromptsPath);
        }

        private string ApplyRules(string prompt)
        {
            var rules = ProcessingRulesReader.ReadAll(_options.RulesPath);
            if (string.IsNullOrWhiteSpace(rules))
                return prompt;
            return "The spec-processing and task-formation rules are provided below. Comply with them strictly.\n\n" +
                   rules + "\n\n" + prompt;
        }

        private string ApplyPlatformRules(string prompt, RobotPlatform platform)
        {
            var rules = ProcessingRulesReader.ReadAll(_options.RulesPath, platform.ToString().ToLowerInvariant());
            if (string.IsNullOrWhiteSpace(rules))
                return prompt;
            return "The platform-specific rules are provided below. Comply with them strictly.\n\n" +
                   rules + "\n\n" + prompt;
        }

        public async Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                ApplyRules(
                    _rpaPrompts.AnalyzeSpec.Replace("{spec}", spec?.RawText ?? "")),
                cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            var analysis = new SpecAnalysis { Summary = obj.Value<string>("summary") ?? "" };
            foreach (var t in obj["tasks"] ?? new JArray())
                analysis.Tasks.Add(ParseTask(t));
            return analysis;
        }

        public async Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var payload = JsonConvert.SerializeObject(analysis);
            var content = await CompleteAsync(
                _rpaPrompts.GenerateRobot.Replace("{analysis}", payload),
                cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            var plan = new RobotPlan { Name = obj.Value<string>("name") ?? "Workflow" };
            var root = new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };
            foreach (var step in obj["steps"] ?? new JArray())
                root.Children.Add(ParseStep(step));
            plan.Root = root;
            return plan;
        }

        public async Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                ApplyRules(
                    _rpaPrompts.DecomposeTasks.Replace("{document}", text ?? "")),
                cancellationToken).ConfigureAwait(false);

            var array = ParseArray(content);
            var result = new List<TaskDescriptor>();
            if (array != null)
                foreach (var t in array)
                    result.Add(ParseTask(t));
            return result;
        }

        public async Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
        {
            var filesJson = JsonConvert.SerializeObject(robotFiles ?? new List<RobotSourceFile>());
            var content = await CompleteAsync(
                ApplyPlatformRules(
                    _rpaPrompts.ReconstructSpec
                        .Replace("{robotName}", robotName ?? "")
                        .Replace("{files}", filesJson),
                    platform), cancellationToken, notifyMessageSent: false).ConfigureAwait(false);

            var obj = ParseJson(content);
            var spec = new SpecModel
            {
                Name = obj.Value<string>("name") ?? robotName ?? "Robot",
                SourcePath = robotName,
                Description = obj.Value<string>("description") ?? ""
            };
            AddStringArray(spec.Steps, obj["steps"]);
            AddStringArray(spec.Inputs, obj["inputs"]);
            AddStringArray(spec.Outputs, obj["outputs"]);
            spec.RawText = BuildRawSpecText(spec);
            return spec;
        }

        public async Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
        {
            var taskJson = JsonConvert.SerializeObject(task);
            var filesJson = JsonConvert.SerializeObject(robotFiles ?? new List<RobotSourceFile>());
            var content = await CompleteAsync(
                ApplyPlatformRules(
                    _rpaPrompts.MapTaskToSolution
                        .Replace("{task}", taskJson)
                        .Replace("{files}", filesJson),
                    platform), cancellationToken, notifyMessageSent: false).ConfigureAwait(false);

            var obj = ParseJson(content);
            var record = new SolutionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                XamlFragment = obj.Value<string>("xamlFragment"),
                CSharpCode = obj.Value<string>("csharpCode"),
                Tasks = new List<TaskDescriptor> { task }
            };
            AddStringArray(record.SolutionPaths, obj["solutionPaths"]);
            return record;
        }

        public async Task<string> GenerateAlgorithmDraftAsync(string specMarkdown, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                _rpaPrompts.GenerateAlgorithmDraft.Replace("{spec}", specMarkdown ?? ""),
                _prompts.ChatSystemPrompt,
                cancellationToken).ConfigureAwait(false);
            return content;
        }

        public async Task<AlgorithmConsolidation> ConsolidateAlgorithmAsync(string specMarkdown, IReadOnlyList<string> drafts, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                _rpaPrompts.ConsolidateAlgorithm
                    .Replace("{spec}", specMarkdown ?? "")
                    .Replace("{drafts}", BuildDraftsText(drafts)),
                _prompts.SystemPrompt,
                cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            return new AlgorithmConsolidation
            {
                AlgorithmMarkdown = obj.Value<string>("algorithm") ?? "",
                QuestionsMarkdown = obj.Value<string>("questions") ?? ""
            };
        }

        private static string BuildDraftsText(IReadOnlyList<string> drafts)
        {
            var sb = new StringBuilder();
            var list = drafts ?? new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine().AppendLine("---").AppendLine();
                sb.AppendLine("## Черновик " + (i + 1));
                sb.AppendLine(list[i] ?? "");
            }
            return sb.ToString();
        }

        private static TaskDescriptor ParseTask(JToken t)
        {
            return new TaskDescriptor
            {
                Action = t.Value<string>("action"),
                Target = t.Value<string>("target"),
                Description = t.Value<string>("description"),
                ActivityType = t.Value<string>("activityType"),
                SapTransactionCode = t.Value<string>("sapTransactionCode"),
                SapActionKind = ParseSapActionKind(t.Value<string>("sapActionKind")),
                EstimatedHours = ParseEstimatedHours(t["estimatedHours"])
            };
        }

        private static double? ParseEstimatedHours(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<double?>();
            if (token.Type == JTokenType.String &&
                double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            return null;
        }

        private static SapActionKind? ParseSapActionKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "setup":
                case "настройка":
                case "запуск":
                case "настройка и запуск":
                    return SapActionKind.Setup;
                case "work":
                case "работа":
                case "работа с транзакцией":
                    return SapActionKind.Work;
                case "gettable":
                case "получение таблицы":
                case "получение данных":
                    return SapActionKind.GetTable;
                default:
                    return null;
            }
        }

        private static void AddStringArray(List<string> target, JToken token)
        {
            if (!(token is JArray array)) return;
            foreach (var item in array)
            {
                if (item.Type == JTokenType.String && !string.IsNullOrWhiteSpace(item.Value<string>()))
                    target.Add(item.Value<string>());
            }
        }

        private static string BuildRawSpecText(SpecModel spec)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(spec?.Description)) sb.AppendLine(spec.Description);
            foreach (var step in spec?.Steps ?? new List<string>()) sb.AppendLine(step);
            foreach (var input in spec?.Inputs ?? new List<string>()) sb.AppendLine("Input: " + input);
            foreach (var output in spec?.Outputs ?? new List<string>()) sb.AppendLine("Output: " + output);
            return sb.ToString();
        }

        private static ActivityNode ParseStep(JToken step)
        {
            return new ActivityNode
            {
                Kind = ParseKind(step.Value<string>("kind")),
                Name = step.Value<string>("name"),
                ClassName = step.Value<string>("className"),
                MethodName = step.Value<string>("methodName"),
                Variable = step.Value<string>("variable"),
                Expression = step.Value<string>("expression")
            };
        }

        private static ActivityNodeKind ParseKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return ActivityNodeKind.InvokeMethod;
            switch (kind.Trim().ToLowerInvariant())
            {
                case "sequence": return ActivityNodeKind.Sequence;
                case "invokemethod": return ActivityNodeKind.InvokeMethod;
                case "assign": return ActivityNodeKind.Assign;
                case "writeline": return ActivityNodeKind.WriteLine;
                case "delay": return ActivityNodeKind.Delay;
                case "if": return ActivityNodeKind.If;
                case "while": return ActivityNodeKind.While;
                case "foreach": return ActivityNodeKind.ForEach;
                default: return ActivityNodeKind.InvokeMethod;
            }
        }
    }
}
