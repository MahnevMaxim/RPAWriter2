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

        /// <summary>Model context window size in tokens; used to display token consumption in the UI.</summary>
        public int ContextWindowSize { get; set; } = 409600;

        /// <summary>Total budget for retrying a failing request before giving up.</summary>
        public TimeSpan RetryTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>Initial delay between retries; doubles on each attempt up to RetryMaxDelay.</summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Maximum delay between retries.</summary>
        public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// OpenAI-compatible provider: POST {BaseUrl}/chat/completions.
    /// Interoperable with OpenAI, Azure OpenAI (compatible endpoint), Ollama, LM Studio, and vLLM.
    /// </summary>
    public class OpenAiCompatibleProvider : IAiProvider, ITokenUsageProvider, IMessageNotifier, IRetryNotifier
    {
        private readonly HttpClient _http;
        private readonly OpenAiOptions _options;
        private readonly TokenUsage _totalUsage = new TokenUsage();
        private readonly object _usageLock = new object();

        /// <summary>Raised on every retry attempt, carrying the provider's status code and response.</summary>
        public event EventHandler<RetryAttemptEventArgs> RetryAttempt;

        private static readonly Random _random = new Random();

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

        /// <summary>Cumulative tokens consumed across all requests handled by this provider instance.</summary>
        public TokenUsage TotalTokenUsage
        {
            get
            {
                lock (_usageLock)
                {
                    return new TokenUsage
                    {
                        PromptTokens = _totalUsage.PromptTokens,
                        CompletionTokens = _totalUsage.CompletionTokens,
                        TotalTokens = _totalUsage.TotalTokens
                    };
                }
            }
        }

        /// <summary>Context window size (tokens) of the configured model.</summary>
        public int ContextWindowSize => _options.ContextWindowSize > 0 ? _options.ContextWindowSize : 409600;

        /// <summary>Raised after each request when the provider reports token usage.</summary>
        public event EventHandler<TokenUsage> TokenUsageUpdated;

        /// <summary>Raised right before an outgoing message is sent to the AI provider.</summary>
        public event EventHandler<AiMessageEventArgs> MessageSent;

        private void RaiseMessageSent(string role, string content)
        {
            MessageSent?.Invoke(this, new AiMessageEventArgs { Role = role, Content = content ?? "" });
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

        /// <summary>Reads platform-specific rules from the platform subdirectory of the rules path and prepends them to the prompt.</summary>
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
                    "[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}]. " +
                    "Write the action, target and description of each task in Russian.\n\nDocument:\n" +
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

        public async Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
        {
            var filesJson = JsonConvert.SerializeObject(robotFiles ?? new List<RobotSourceFile>());
            var content = await CompleteAsync(
                ApplyPlatformRules(
                    "Reconstruct the technical specification of an RPA robot from its source code. " +
                    "Start from the entry point (main.xaml) and follow the files it invokes; the provided list is the reachable set. " +
                    "Return JSON of the form {\"name\":\"...\",\"description\":\"...\",\"steps\":[\"...\"],\"inputs\":[\"...\"],\"outputs\":[\"...\"]}. " +
                    "Write the description, steps, inputs and outputs in Russian.\n\n" +
                    "Robot name: " + (robotName ?? "") + "\n\nRobot files (relativePath + content):\n" + filesJson,
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
                    "Locate the solution for the task in the robot source files. Return JSON " +
                    "{\"xamlFragment\":\"...\",\"csharpCode\":\"...\",\"solutionPaths\":[\"relative/path/File.xaml\",\"relative/path/File.cs\"]}. " +
                    "solutionPaths must list the file(s) (relative path) where the task is implemented.\n\n" +
                    "Task: " + taskJson + "\n\nRobot files (relativePath + content):\n" + filesJson,
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
            RaiseMessageSent("user", message);
            var obj = await SendAsync(() => BuildRequest(message, _options.ChatSystemPrompt), cancellationToken).ConfigureAwait(false);
            var messageObj = obj["choices"]?[0]?["message"];
            var content = messageObj?["content"]?.Value<string>();
            var reasoning = ExtractReasoning(messageObj);
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning))
                throw new InvalidOperationException("Empty response from the AI provider.");
            return new ChatResponse { Answer = content ?? "", Reasoning = reasoning };
        }

        internal async Task<string> CompleteAsync(string userMessage, CancellationToken cancellationToken, bool notifyMessageSent = true)
        {
            return await CompleteAsync(userMessage, _options.SystemPrompt, cancellationToken, notifyMessageSent).ConfigureAwait(false);
        }

        internal async Task<string> CompleteAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken, bool notifyMessageSent = true)
        {
            if (notifyMessageSent)
                RaiseMessageSent("user", userMessage);
            var obj = await SendAsync(() => BuildRequest(userMessage, systemPrompt), cancellationToken).ConfigureAwait(false);
            var content = obj["choices"]?[0]?["message"]?["content"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Empty response from the AI provider.");
            return content;
        }

        private async Task<JObject> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
        {
            var startedAt = DateTime.UtcNow;
            var deadline = startedAt + _options.RetryTimeout;
            var attempt = 0;
            int? lastStatus = null;
            var lastReason = "";
            var lastBody = "";

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                HttpResponseMessage response = null;
                Exception sendError = null;
                try
                {
                    response = await _http.SendAsync(requestFactory(), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    sendError = new TimeoutException(
                        "Timed out waiting for a response from the AI provider (" + _options.TimeoutSeconds + " s).");
                }
                catch (HttpRequestException ex)
                {
                    sendError = ex;
                }

                if (response != null)
                {
                    var retryAfter = ReadRetryAfter(response);
                    var body = "";
                    try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { body = ""; }

                    if (response.IsSuccessStatusCode)
                    {
                        var obj = JObject.Parse(body);
                        RecordUsage(obj["usage"]);
                        response.Dispose();
                        return obj;
                    }

                    lastStatus = (int)response.StatusCode;
                    lastReason = response.ReasonPhrase ?? "";
                    lastBody = body;
                    response.Dispose();

                    attempt++;
                    var wait = retryAfter ?? ComputeBackoff(attempt);
                    var elapsed = DateTime.UtcNow - startedAt;
                    var remaining = deadline - DateTime.UtcNow;
                    var willRetry = remaining > TimeSpan.Zero;
                    if (willRetry && wait > remaining)
                        wait = remaining;

                    RaiseRetryAttempt(attempt, lastStatus, lastReason, lastBody, wait, elapsed, remaining, willRetry);

                    if (!willRetry) break;

                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    lastStatus = null;
                    lastReason = sendError?.GetType().Name ?? "network error";
                    lastBody = sendError?.Message ?? "";

                    attempt++;
                    var wait = ComputeBackoff(attempt);
                    var elapsed = DateTime.UtcNow - startedAt;
                    var remaining = deadline - DateTime.UtcNow;
                    var willRetry = remaining > TimeSpan.Zero;
                    if (willRetry && wait > remaining)
                        wait = remaining;

                    RaiseRetryAttempt(attempt, lastStatus, lastReason, lastBody, wait, elapsed, remaining, willRetry);

                    if (!willRetry) break;

                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new HttpRequestException(BuildFinalErrorMessage(attempt, DateTime.UtcNow - startedAt, lastStatus, lastReason, lastBody));
        }

        private TimeSpan ComputeBackoff(int attempt)
        {
            var baseSeconds = Math.Max(0, _options.RetryBaseDelay.TotalSeconds);
            var maxSeconds = Math.Max(baseSeconds, _options.RetryMaxDelay.TotalSeconds);
            var seconds = Math.Min(maxSeconds, baseSeconds * Math.Pow(2.0, attempt - 1));
            var jitter = seconds * 0.2 * _random.NextDouble();
            return TimeSpan.FromSeconds(seconds + jitter);
        }

        private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        {
            if (response?.Headers == null) return null;
            if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (int.TryParse(value.Trim(), out var seconds))
                    return TimeSpan.FromSeconds(Math.Max(0, seconds));
                if (DateTimeOffset.TryParse(value.Trim(), out var date))
                {
                    var delta = date - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero) return delta;
                }
            }
            return null;
        }

        private void RaiseRetryAttempt(int attempt, int? statusCode, string reason, string body, TimeSpan wait, TimeSpan elapsed, TimeSpan remaining, bool willRetry)
        {
            var handler = RetryAttempt;
            if (handler == null) return;
            handler(this, new RetryAttemptEventArgs
            {
                Attempt = attempt,
                StatusCode = statusCode,
                ReasonPhrase = reason ?? "",
                Body = TruncateForLog(body ?? "", 500),
                WaitBeforeRetry = wait,
                Elapsed = elapsed,
                Remaining = remaining,
                WillRetry = willRetry
            });
        }

        private static string BuildFinalErrorMessage(int attempt, TimeSpan elapsed, int? statusCode, string reason, string body)
        {
            var status = statusCode.HasValue
                ? statusCode.Value + " (" + reason + ")"
                : "no response (" + reason + ")";
            return "AI provider unavailable after " + attempt + " attempt(s) over " + FormatDuration(elapsed) +
                   ". Last status: " + status + ". Response: " + TruncateForLog(body);
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds + "s";
            if (t.TotalSeconds >= 1) return (int)t.TotalSeconds + "s";
            return (int)t.TotalMilliseconds + "ms";
        }

        private static string TruncateForLog(string text, int max = 2000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "…(truncated)";
        }

        /// <summary>Accumulates token usage reported by the provider and notifies listeners.</summary>
        private void RecordUsage(JToken usage)
        {
            var prompt = ReadTokenCount(usage, "prompt_tokens");
            var completion = ReadTokenCount(usage, "completion_tokens");
            var total = ReadTokenCount(usage, "total_tokens");
            if (total == 0) total = prompt + completion;
            if (total == 0) return;

            lock (_usageLock)
            {
                _totalUsage.PromptTokens += prompt;
                _totalUsage.CompletionTokens += completion;
                _totalUsage.TotalTokens += total;
            }

            TokenUsageUpdated?.Invoke(this, TotalTokenUsage);
        }

        private static int ReadTokenCount(JToken usage, string name)
        {
            var token = usage?[name];
            if (token == null) return 0;
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out var parsed)) return parsed;
            return 0;
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
