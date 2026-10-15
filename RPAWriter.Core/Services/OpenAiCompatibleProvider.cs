using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    public class OpenAiOptions
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; }
        public string Model { get; set; } = "gpt-4o-mini";
        public double Temperature { get; set; } = 0.2;
        public int TimeoutSeconds { get; set; } = 300;
        public string SystemPrompt { get; set; } =
            "You are an RPA robot development agent for Windows Workflow Foundation. " +
            "Always respond with strictly valid JSON and no prose.";

        /// <summary>Prompt for free-form chat (plain text, no JSON requirement).</summary>
        public string ChatSystemPrompt { get; set; } =
            "You are an RPA robot development assistant for Windows Workflow Foundation. " +
            "Answer concisely and to the point in plain text, without JSON, unless explicitly requested.";

        /// <summary>Directory of markdown files containing spec-processing and task-formation rules.</summary>
        public string RulesPath { get; set; } = "";
    }

    /// <summary>
    /// OpenAI-compatible provider: POST {BaseUrl}/chat/completions.
    /// Interoperable with OpenAI, Azure OpenAI (compatible endpoint), Ollama, LM Studio, and vLLM.
    /// </summary>
    public class OpenAiCompatibleProvider : IAiProvider
    {
        private readonly HttpClient _http;
        private readonly OpenAiOptions _options;

        static OpenAiCompatibleProvider()
        {
            // On .NET Framework the default security protocol can still negotiate TLS 1.0/1.1,
            // which modern OpenAI-compatible endpoints reject by forcibly closing the connection.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // Best-effort only; some runtimes restrict changing this value.
            }
        }

        public OpenAiCompatibleProvider(OpenAiOptions options) : this(options, new HttpClient()) { }

        public OpenAiCompatibleProvider(OpenAiOptions options, HttpClient http)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            if (_options.TimeoutSeconds > 0)
                _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }

        /// <summary>Reads spec-processing rules from the configured directory (when set) and prepends them to the prompt.</summary>
        private string ApplyRules(string prompt)
        {
            var rules = ProcessingRulesReader.ReadAll(_options.RulesPath);
            if (string.IsNullOrWhiteSpace(rules))
                return prompt;
            return "The spec-processing and task-formation rules are provided below. Comply with them strictly.\n\n" +
                   rules + "\n\n" + prompt;
        }

        public async Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                ApplyRules(
                    "Analyze the technical specification and return JSON of the form " +
                    "{\"summary\":\"...\",\"tasks\":[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}]}.\n\nSpec:\n" +
                    (spec?.RawText ?? "")), cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            var analysis = new SpecAnalysis { Summary = obj.Value<string>("summary") ?? "" };
            foreach (var t in obj["tasks"] ?? new JArray())
            {
                analysis.Tasks.Add(new TaskDescriptor
                {
                    Action = t.Value<string>("action"),
                    Target = t.Value<string>("target"),
                    Description = t.Value<string>("description"),
                    ActivityType = t.Value<string>("activityType")
                });
            }
            return analysis;
        }

        public async Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var payload = JsonConvert.SerializeObject(analysis);
            var content = await CompleteAsync(
                "Construct a workflow plan. Return JSON of the form {\"name\":\"...\",\"steps\":[" +
                "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"...\"}," +
                "{\"kind\":\"Assign\",\"variable\":\"...\",\"expression\":\"...\"}]}. " +
                "kind: Sequence|InvokeMethod|Assign|WriteLine|Delay. Source analysis: " + payload,
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
                    "Decompose the document into small atomic tasks. Return a JSON array: " +
                    "[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}].\n\nDocument:\n" +
                    (text ?? "")), cancellationToken).ConfigureAwait(false);

            var array = ParseArray(content);
            var result = new List<TaskDescriptor>();
            if (array != null)
            {
                foreach (var t in array)
                {
                    result.Add(new TaskDescriptor
                    {
                        Action = t.Value<string>("action"),
                        Target = t.Value<string>("target"),
                        Description = t.Value<string>("description"),
                        ActivityType = t.Value<string>("activityType")
                    });
                }
            }
            return result;
        }

        public async Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default)
        {
            var filesJson = JsonConvert.SerializeObject(robotFiles ?? new List<RobotSourceFile>());
            var content = await CompleteAsync(
                "Reconstruct the technical specification of an RPA robot from its source code (.xaml/.cs). " +
                "Return JSON of the form {\"name\":\"...\",\"description\":\"...\",\"steps\":[\"...\"],\"inputs\":[\"...\"],\"outputs\":[\"...\"]}.\n\n" +
                "Robot name: " + (robotName ?? "") + "\n\nRobot files (relativePath + content):\n" + filesJson,
                cancellationToken).ConfigureAwait(false);

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

        public async Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default)
        {
            var taskJson = JsonConvert.SerializeObject(task);
            var filesJson = JsonConvert.SerializeObject(robotFiles ?? new List<RobotSourceFile>());
            var content = await CompleteAsync(
                "Locate the solution for the task in the robot source files. Return JSON " +
                "{\"xamlFragment\":\"...\",\"csharpCode\":\"...\",\"solutionPaths\":[\"relative/path/File.xaml\",\"relative/path/File.cs\"]}. " +
                "solutionPaths must list the file(s) (relative path) where the task is implemented.\n\n" +
                "Task: " + taskJson + "\n\nRobot files (relativePath + content):\n" + filesJson,
                cancellationToken).ConfigureAwait(false);

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

        public async Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
        {
            var request = BuildRequest(message, _options.ChatSystemPrompt);
            var obj = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var messageObj = obj["choices"]?[0]?["message"];
            var content = messageObj?["content"]?.Value<string>();
            var reasoning = ExtractReasoning(messageObj);
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning))
                throw new InvalidOperationException("Empty response from the AI provider.");
            return new ChatResponse { Answer = content ?? "", Reasoning = reasoning };
        }

        internal async Task<string> CompleteAsync(string userMessage, CancellationToken cancellationToken)
        {
            return await CompleteAsync(userMessage, _options.SystemPrompt, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<string> CompleteAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken)
        {
            var request = BuildRequest(userMessage, systemPrompt);
            var obj = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = obj["choices"]?[0]?["message"]?["content"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Empty response from the AI provider.");
            return content;
        }

        private async Task<JObject> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Timed out waiting for a response from the AI provider (" + _options.TimeoutSeconds + " s). Verify that the provider is reachable.");
            }
            using (response)
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JObject.Parse(body);
            }
        }

        /// <summary>
        /// Extracts the model's "reasoning" from a message. Supports:
        /// DeepSeek R1 / Ollama / vLLM — <c>reasoning_content</c>; OpenAI o-series — <c>reasoning</c>.
        /// Either field may be a string or an object with <c>content</c>/<c>summary</c>.
        /// </summary>
        private static string ExtractReasoning(JToken message)
        {
            if (message == null) return null;

            var rc = message["reasoning_content"];
            if (rc != null)
                return rc.Type == JTokenType.String ? rc.Value<string>() : ExtractReasoningInner(rc);

            var r = message["reasoning"];
            if (r != null)
                return r.Type == JTokenType.String ? r.Value<string>() : ExtractReasoningInner(r);

            return null;
        }

        private static string ExtractReasoningInner(JToken token)
        {
            if (token == null) return null;
            return token["content"]?.Value<string>()
                ?? token["summary"]?.Value<string>()
                ?? token.ToString();
        }

        internal HttpRequestMessage BuildRequest(string userMessage)
        {
            return BuildRequest(userMessage, _options.SystemPrompt);
        }

        internal HttpRequestMessage BuildRequest(string userMessage, string systemPrompt)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            };
            var payload = new
            {
                model = _options.Model,
                messages = messages,
                temperature = _options.Temperature
            };
            var json = JsonConvert.SerializeObject(payload);

            var url = (_options.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/') + "/chat/completions";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return request;
        }

        private static JObject ParseJson(string content)
        {
            return JObject.Parse(CleanContent(content));
        }

        private static JArray ParseArray(string content)
        {
            return JArray.Parse(CleanContent(content));
        }

        private static string CleanContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Empty JSON from the AI provider.");
            content = content.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = content.IndexOf('\n');
                var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence > firstNewline)
                    content = content.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            return content;
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
