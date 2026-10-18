using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Context;
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

        /// <summary>How the agent talks to the model about tools: native function calling or JSON over chat.</summary>
        public ToolCallingMode ToolCallingMode { get; set; } = ToolCallingMode.JsonChat;
        public int TimeoutSeconds { get; set; } = 300;
        /// <summary>Base directory of the prompt template files (System/, Tasks/) shipped with the extension.</summary>
        public string PromptsPath { get; set; } = PromptPaths.ResolvePromptsDirectory();

        /// <summary>Directory of markdown files containing spec-processing and task-formation rules.</summary>
        public string RulesPath { get; set; } = PromptPaths.ResolveRulesDirectory();

        /// <summary>Model context window size in tokens; used to display token consumption in the UI.</summary>
        public int ContextWindowSize { get; set; } = 409600;

        /// <summary>Total budget for retrying a failing request before giving up.</summary>
        public TimeSpan RetryTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>Initial delay between retries; doubles on each attempt up to RetryMaxDelay.</summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Maximum delay between retries.</summary>
        public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Price per 1M uncached input (cache-miss) tokens, in USD.</summary>
        public double InputPricePerMillion { get; set; } = 0.27;

        /// <summary>Price per 1M cache-hit input tokens, in USD.</summary>
        public double CacheHitPricePerMillion { get; set; } = 0.07;

        /// <summary>Price per 1M output tokens, in USD.</summary>
        public double OutputPricePerMillion { get; set; } = 1.10;
    }

    /// <summary>
    /// OpenAI-compatible provider: POST {BaseUrl}/chat/completions.
    /// Interoperable with OpenAI, Azure OpenAI (compatible endpoint), Ollama, LM Studio, and vLLM.
    /// </summary>
    public class OpenAiCompatibleProvider : IAiProvider, ITokenUsageProvider, IMessageNotifier, IRetryNotifier, IPromptReloadable, IStreamingAiProvider, IPromptNotifier
    {
        private readonly HttpClient _http;
        private readonly OpenAiOptions _options;
        private PromptSet _prompts;
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
            _prompts = PromptSet.Load(_options.PromptsPath);
            if (_options.TimeoutSeconds > 0)
                _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }

        /// <summary>Re-reads all prompt templates from the prompts directory on disk.</summary>
        public void ReloadPromptsFromDisk()
        {
            _prompts = PromptSet.Load(_options.PromptsPath);
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
                        TotalTokens = _totalUsage.TotalTokens,
                        CacheHitTokens = _totalUsage.CacheHitTokens,
                        CacheMissTokens = _totalUsage.CacheMissTokens,
                        EstimatedCostUsd = _totalUsage.EstimatedCostUsd
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

        // Raised right before a full request (system prompt + all messages) is sent to the AI provider.
        public event EventHandler<PromptAssembledEventArgs> PromptAssembled;

        private void RaiseMessageSent(string role, string content)
        {
            MessageSent?.Invoke(this, new AiMessageEventArgs { Role = role, Content = content ?? "" });
        }

        private void RaisePromptAssembled(string systemPrompt, IReadOnlyList<ChatMessageData> messages)
        {
            PromptAssembled?.Invoke(this, new PromptAssembledEventArgs
            {
                SystemPrompt = systemPrompt ?? "",
                Messages = messages ?? new List<ChatMessageData>(),
                EstimatedInputTokens = new TokenEstimator().Estimate(systemPrompt, messages)
            });
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
                    _prompts.AnalyzeSpec.Replace("{spec}", spec?.RawText ?? "")),
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
                _prompts.GenerateRobot.Replace("{analysis}", payload),
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
                    _prompts.DecomposeTasks.Replace("{document}", text ?? "")),
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
                    _prompts.ReconstructSpec
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
                    _prompts.MapTaskToSolution
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

        private static TaskDescriptor ParseTask(JToken t)
        {
            return new TaskDescriptor
            {
                Action = t.Value<string>("action"),
                Target = t.Value<string>("target"),
                Description = t.Value<string>("description"),
                ActivityType = t.Value<string>("activityType"),
                SapTransactionCode = t.Value<string>("sapTransactionCode"),
                SapActionKind = ParseSapActionKind(t.Value<string>("sapActionKind"))
            };
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

        public async Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
        {
            RaiseMessageSent("user", message);
            RaisePromptAssembled(_prompts.ChatSystemPrompt, new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = message } });
            var obj = await SendAsync(() => BuildRequest(message, _prompts.ChatSystemPrompt), cancellationToken).ConfigureAwait(false);
            var messageObj = obj["choices"]?[0]?["message"];
            var content = messageObj?["content"]?.Value<string>();
            var reasoning = ExtractReasoning(messageObj);
            var finishReason = obj["choices"]?[0]?["finish_reason"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning))
                throw new InvalidOperationException("Empty response from the AI provider.");
            return new ChatResponse { Answer = content ?? "", Reasoning = reasoning, FinishReason = finishReason };
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
        {
            return ChatAsync(messages, systemPrompt, null, cancellationToken);
        }

        public async Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            foreach (var m in messages)
                RaiseMessageSent(string.IsNullOrWhiteSpace(m?.Role) ? "user" : m.Role, m?.Content ?? "");

            RaisePromptAssembled(systemPrompt, messages);
            var obj = await SendAsync(() => BuildRequest(messages, systemPrompt, tools, stream: false), cancellationToken).ConfigureAwait(false);
            return ParseChatResponse(obj);
        }

        public Task<ChatResponse> ChatStreamingAsync(
            IReadOnlyList<ChatMessageData> messages,
            string systemPrompt,
            IProgress<ChatStreamDelta> progress,
            CancellationToken cancellationToken = default)
        {
            return ChatStreamingAsync(messages, systemPrompt, null, progress, cancellationToken);
        }

        public async Task<ChatResponse> ChatStreamingAsync(
            IReadOnlyList<ChatMessageData> messages,
            string systemPrompt,
            IReadOnlyList<ToolDefinition> tools,
            IProgress<ChatStreamDelta> progress,
            CancellationToken cancellationToken = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            foreach (var m in messages)
                RaiseMessageSent(string.IsNullOrWhiteSpace(m?.Role) ? "user" : m.Role, m?.Content ?? "");

            RaisePromptAssembled(systemPrompt, messages);
            var reasoning = new StringBuilder();
            var content = new StringBuilder();
            var finishReason = new StringBuilder();
            var toolCalls = new List<NativeToolCall>();

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
                    response = await _http.SendAsync(
                        BuildRequest(messages, systemPrompt, tools, stream: true),
                        cancellationToken).ConfigureAwait(false);
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
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            await ReadSseStreamAsync(response, reasoning, content, finishReason, toolCalls, progress, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            response.Dispose();
                        }
                        return new ChatResponse { Answer = content.ToString(), Reasoning = reasoning.ToString(), FinishReason = finishReason.ToString(), ToolCalls = toolCalls };
                    }

                    var retryAfter = ReadRetryAfter(response);
                    var body = "";
                    try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { body = ""; }
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

            throw BuildProviderErrorException(attempt, DateTime.UtcNow - startedAt, lastStatus, lastReason, lastBody);
        }

        /// <summary>
        /// Reads an OpenAI-compatible SSE stream ("data: {json}" lines) and reports
        /// reasoning/content deltas through <paramref name="progress"/>. Falls back to
        /// parsing a plain JSON object when the endpoint ignores <c>stream:true</c>.
        /// </summary>
        private async Task ReadSseStreamAsync(
            HttpResponseMessage response,
            StringBuilder reasoning,
            StringBuilder content,
            StringBuilder finishReason,
            List<NativeToolCall> toolCalls,
            IProgress<ChatStreamDelta> progress,
            CancellationToken cancellationToken)
        {
            var toolAcc = new Dictionary<int, ToolCallAccumulator>();
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var inSse = false;
                var nonSseBuffer = new StringBuilder();

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;

                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith(":", StringComparison.Ordinal)) continue;

                    if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                    {
                        inSse = true;
                        nonSseBuffer.Clear();

                        var data = trimmed.Substring(5).TrimStart();
                        if (data == "[DONE]") break;

                        JObject chunk;
                        try { chunk = JObject.Parse(data); }
                        catch { continue; }

                        RecordUsage(chunk["usage"]);

                        var firstChoice = (chunk["choices"] as JArray)?.First;
                        var fr = firstChoice?["finish_reason"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(fr))
                        {
                            finishReason.Clear();
                            finishReason.Append(fr);
                        }

                        var delta = firstChoice?["delta"];
                        if (delta == null) continue;

                        var reasoningDelta = ExtractReasoning(delta);
                        var contentDelta = delta["content"]?.Value<string>();

                        AccumulateToolCalls(delta["tool_calls"], toolAcc);

                        if (!string.IsNullOrEmpty(reasoningDelta))
                            reasoning.Append(reasoningDelta);
                        if (!string.IsNullOrEmpty(contentDelta))
                            content.Append(contentDelta);

                        if ((!string.IsNullOrEmpty(reasoningDelta) || !string.IsNullOrEmpty(contentDelta)) && progress != null)
                        {
                            progress.Report(new ChatStreamDelta
                            {
                                ReasoningDelta = reasoningDelta,
                                ContentDelta = contentDelta
                            });
                        }
                    }
                    else if (!inSse && trimmed.StartsWith("{", StringComparison.Ordinal))
                    {
                        // The endpoint returned a plain JSON object instead of an SSE stream.
                        nonSseBuffer.Append(trimmed);
                        JObject obj;
                        try { obj = JObject.Parse(nonSseBuffer.ToString()); }
                        catch { continue; } // keep accumulating pretty-printed multi-line JSON

                        var messageObj = obj["choices"]?[0]?["message"];
                        var c = messageObj?["content"]?.Value<string>();
                        var r = ExtractReasoning(messageObj);

                        if (!string.IsNullOrEmpty(r))
                            reasoning.Append(r);
                        if (!string.IsNullOrEmpty(c))
                            content.Append(c);

                        if ((!string.IsNullOrEmpty(r) || !string.IsNullOrEmpty(c)) && progress != null)
                            progress.Report(new ChatStreamDelta { ReasoningDelta = r, ContentDelta = c });

                        RecordUsage(obj["usage"]);

                        var fr = obj["choices"]?[0]?["finish_reason"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(fr))
                        {
                            finishReason.Clear();
                            finishReason.Append(fr);
                        }
                        toolCalls.AddRange(ParseToolCalls(messageObj));
                        return;
                    }
                    // otherwise: ignore (e.g. an SSE "event:" line)
                }
            }

            FinalizeToolCalls(toolCalls, toolAcc);
        }

        internal async Task<string> CompleteAsync(string userMessage, CancellationToken cancellationToken, bool notifyMessageSent = true)
        {
            return await CompleteAsync(userMessage, _prompts.SystemPrompt, cancellationToken, notifyMessageSent).ConfigureAwait(false);
        }

        internal async Task<string> CompleteAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken, bool notifyMessageSent = true)
        {
            if (notifyMessageSent)
                RaiseMessageSent("user", userMessage);
            RaisePromptAssembled(systemPrompt, new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = userMessage } });
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

            throw BuildProviderErrorException(attempt, DateTime.UtcNow - startedAt, lastStatus, lastReason, lastBody);
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

        private static ProviderErrorException BuildProviderErrorException(int attempt, TimeSpan elapsed, int? statusCode, string reason, string body)
        {
            var kind = ClassifyProviderError(statusCode, body);
            var message = BuildFinalErrorMessage(attempt, elapsed, statusCode, reason, body);
            return new ProviderErrorException(kind, message, statusCode, body);
        }

        private static ProviderErrorKind ClassifyProviderError(int? statusCode, string body)
        {
            if (statusCode == 401 || statusCode == 403) return ProviderErrorKind.Auth;
            if (statusCode == 429) return ProviderErrorKind.RateLimit;
            if (statusCode >= 500) return ProviderErrorKind.Transient;
            if (ContainsContextOverflowKeywords(body)) return ProviderErrorKind.ContextWindowExceeded;
            if (statusCode == 400 && ContainsContentFilterKeywords(body)) return ProviderErrorKind.ContentFilter;
            return ProviderErrorKind.Other;
        }

        private static bool ContainsContextOverflowKeywords(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            var b = body.ToLowerInvariant();
            return b.Contains("context length") ||
                   b.Contains("context window") ||
                   b.Contains("too many tokens") ||
                   b.Contains("maximum context") ||
                   b.Contains("max tokens") ||
                   b.Contains("token limit") ||
                   b.Contains("context_length_exceeded") ||
                   b.Contains("prompt is too long") ||
                   b.Contains("input is too long");
        }

        private static bool ContainsContentFilterKeywords(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            var b = body.ToLowerInvariant();
            return b.Contains("content filter") ||
                   b.Contains("content_filter") ||
                   b.Contains("safety");
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

            ReadCacheTokens(usage, prompt, out var cacheHit, out var cacheMiss);
            var cost = CalculateCost(cacheHit, cacheMiss, completion);

            lock (_usageLock)
            {
                _totalUsage.PromptTokens += prompt;
                _totalUsage.CompletionTokens += completion;
                _totalUsage.TotalTokens += total;
                _totalUsage.CacheHitTokens += cacheHit;
                _totalUsage.CacheMissTokens += cacheMiss;
                _totalUsage.EstimatedCostUsd += cost;
            }

            TokenUsageUpdated?.Invoke(this, TotalTokenUsage);
        }

        /// <summary>
        /// Splits reported input tokens into cache-hit and cache-miss counts.
        /// DeepSeek reports <c>prompt_cache_hit_tokens</c>/<c>prompt_cache_miss_tokens</c>;
        /// OpenAI reports <c>prompt_tokens_details.cached_tokens</c>. When the provider
        /// does not report a cache split, the whole input is treated as a cache miss.
        /// </summary>
        private static void ReadCacheTokens(JToken usage, int promptTokens, out int hitTokens, out int missTokens)
        {
            hitTokens = 0;
            missTokens = 0;

            if (usage is JObject obj)
            {
                hitTokens = ReadTokenCount(obj, "prompt_cache_hit_tokens");
                missTokens = ReadTokenCount(obj, "prompt_cache_miss_tokens");

                if (hitTokens == 0 && missTokens == 0)
                {
                    // OpenAI-style cache reporting lives in prompt_tokens_details.
                    if (obj["prompt_tokens_details"] is JObject details)
                        hitTokens = ReadTokenCount(details, "cached_tokens");
                }
            }

            if (hitTokens == 0 && missTokens == 0)
                missTokens = Math.Max(0, promptTokens);
            else if (missTokens == 0)
                missTokens = Math.Max(0, promptTokens - hitTokens);
        }

        /// <summary>Estimates the cost of one request from its token split and per-million prices.</summary>
        private double CalculateCost(int cacheHitTokens, int cacheMissTokens, int completionTokens)
        {
            var inputCost = cacheMissTokens / 1000000.0 * _options.InputPricePerMillion
                          + cacheHitTokens / 1000000.0 * _options.CacheHitPricePerMillion;
            var outputCost = completionTokens / 1000000.0 * _options.OutputPricePerMillion;
            return inputCost + outputCost;
        }

        private static int ReadTokenCount(JToken usage, string name)
        {
            if (!(usage is JObject obj)) return 0;
            var token = obj[name];
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
            if (!(message is JObject obj)) return null;

            var rc = obj["reasoning_content"];
            if (rc != null)
                return rc.Type == JTokenType.String ? rc.Value<string>() : ExtractReasoningInner(rc);

            var r = obj["reasoning"];
            if (r != null)
                return r.Type == JTokenType.String ? r.Value<string>() : ExtractReasoningInner(r);

            return null;
        }

        private static string ExtractReasoningInner(JToken token)
        {
            if (!(token is JObject obj)) return token?.ToString();
            return obj["content"]?.Value<string>()
                ?? obj["summary"]?.Value<string>()
                ?? obj.ToString();
        }

        internal HttpRequestMessage BuildRequest(string userMessage)
        {
            return BuildRequest(userMessage, _prompts.SystemPrompt);
        }

        internal HttpRequestMessage BuildRequest(string userMessage, string systemPrompt)
        {
            var messages = new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = userMessage } };
            return BuildRequest(messages, systemPrompt, null, stream: false);
        }

        internal HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessageData> messages, string systemPrompt)
        {
            return BuildRequest(messages, systemPrompt, null, stream: false);
        }

        internal HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessageData> messages, string systemPrompt, bool stream)
        {
            return BuildRequest(messages, systemPrompt, null, stream);
        }

        internal HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, bool stream)
        {
            var native = _options.ToolCallingMode == ToolCallingMode.Native;
            var list = new List<object> { new { role = "system", content = systemPrompt ?? "" } };
            foreach (var m in messages ?? new List<ChatMessageData>())
            {
                var role = string.IsNullOrWhiteSpace(m?.Role) ? "user" : m.Role;
                var content = m?.Content ?? "";

                if (native && string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    // Native tool calling: keep the "tool" role and echo the tool_call_id.
                    list.Add(new { role = "tool", content = content, tool_call_id = m.ToolCallId ?? "" });
                }
                else if (native && string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) && m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    // Native tool calling: replay the assistant's tool_calls so the model can
                    // associate the following "tool" results with their calls.
                    var tcArray = new JArray();
                    foreach (var tc in m.ToolCalls)
                    {
                        tcArray.Add(new JObject
                        {
                            ["id"] = tc.Id ?? "",
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = tc.Name ?? "",
                                ["arguments"] = (tc.Arguments ?? new JObject()).ToString(Formatting.None)
                            }
                        });
                    }
                    list.Add(new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = content,
                        ["tool_calls"] = tcArray
                    });
                }
                else
                {
                    // JSON-over-chat protocol: OpenAI requires a tool_call_id on every "tool"
                    // message, so a bare "tool" role is invalid and must be downgraded to "user".
                    if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
                        role = "user";
                    list.Add(new { role = role, content = content });
                }
            }

            var payload = new JObject
            {
                ["model"] = _options.Model,
                ["temperature"] = _options.Temperature,
                ["messages"] = JArray.FromObject(list)
            };

            if (native && tools != null && tools.Count > 0)
            {
                var toolsArray = new JArray();
                foreach (var t in tools)
                {
                    toolsArray.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = t.Name ?? "",
                            ["description"] = t.Description ?? "",
                            ["parameters"] = t.Parameters ?? new JObject()
                        }
                    });
                }
                payload["tools"] = toolsArray;
                payload["tool_choice"] = "auto";
            }

            if (stream)
            {
                payload["stream"] = true;
                // Ask OpenAI-compatible endpoints to include token usage in the final SSE chunk;
                // without it the chat context progress bar never receives real token counts during streaming.
                payload["stream_options"] = new JObject { ["include_usage"] = true };
            }

            var json = payload.ToString(Formatting.None);

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

        /// <summary>Parses a non-streaming chat completion JSON into a ChatResponse (content + reasoning + finish_reason + tool calls).</summary>
        private static ChatResponse ParseChatResponse(JObject obj)
        {
            var messageObj = obj["choices"]?[0]?["message"];
            var content = messageObj?["content"]?.Value<string>();
            var reasoning = ExtractReasoning(messageObj);
            var finishReason = obj["choices"]?[0]?["finish_reason"]?.Value<string>();
            var toolCalls = ParseToolCalls(messageObj);
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning) && toolCalls.Count == 0)
                throw new InvalidOperationException("Empty response from the AI provider.");
            return new ChatResponse
            {
                Answer = content ?? "",
                Reasoning = reasoning,
                FinishReason = finishReason,
                ToolCalls = toolCalls
            };
        }

        private static IReadOnlyList<NativeToolCall> ParseToolCalls(JToken message)
        {
            var result = new List<NativeToolCall>();
            if (!(message is JObject obj)) return result;
            if (!(obj["tool_calls"] is JArray arr)) return result;
            foreach (var item in arr)
            {
                var fn = item["function"];
                result.Add(new NativeToolCall
                {
                    Id = item["id"]?.Value<string>() ?? "",
                    Name = fn?["name"]?.Value<string>() ?? "",
                    Arguments = ParseArguments(fn?["arguments"]?.Value<string>())
                });
            }
            return result;
        }

        private static JObject ParseArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return new JObject();
            try { return JObject.Parse(arguments); }
            catch { return new JObject(); }
        }

        /// <summary>Accumulates streamed tool-call fragments (name + arguments) keyed by index.</summary>
        private sealed class ToolCallAccumulator
        {
            public int Index;
            public string Id = "";
            public string Name = "";
            public readonly StringBuilder Arguments = new StringBuilder();
        }

        private static void AccumulateToolCalls(JToken toolCallsToken, Dictionary<int, ToolCallAccumulator> accumulators)
        {
            if (!(toolCallsToken is JArray arr)) return;
            foreach (var item in arr)
            {
                var index = item["index"]?.Value<int>() ?? 0;
                if (!accumulators.TryGetValue(index, out var acc))
                {
                    acc = new ToolCallAccumulator { Index = index };
                    accumulators[index] = acc;
                }

                var id = item["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(id)) acc.Id = id;

                var fn = item["function"];
                if (fn == null) continue;

                var name = fn["name"]?.Value<string>();
                if (!string.IsNullOrEmpty(name)) acc.Name = name;

                var args = fn["arguments"]?.Value<string>();
                if (!string.IsNullOrEmpty(args)) acc.Arguments.Append(args);
            }
        }

        private static void FinalizeToolCalls(List<NativeToolCall> target, Dictionary<int, ToolCallAccumulator> accumulators)
        {
            foreach (var acc in accumulators.Values.OrderBy(a => a.Index))
            {
                target.Add(new NativeToolCall
                {
                    Id = acc.Id,
                    Name = acc.Name,
                    Arguments = ParseArguments(acc.Arguments.ToString())
                });
            }
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
            return FixInvalidJsonEscapes(content);
        }

        /// <summary>
        /// Escapes backslashes that precede characters which are not valid JSON escape
        /// sequences (e.g. the <c>\s</c> produced by AI models when embedding C# verbatim
        /// strings or regex patterns like <c>@"\s+"</c>). Valid escapes are left untouched,
        /// so well-formed JSON is unaffected.
        /// </summary>
        private static string FixInvalidJsonEscapes(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            const string validEscapes = "\"\\/bfnrt";
            var sb = new StringBuilder(content.Length);
            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= content.Length)
                {
                    // Trailing backslash has nothing to escape; keep it literal.
                    sb.Append("\\\\");
                    break;
                }

                var next = content[i + 1];
                if (validEscapes.IndexOf(next) >= 0)
                {
                    // Valid simple escape: keep both characters as-is.
                    sb.Append(c).Append(next);
                    i++;
                }
                else if (next == 'u' && i + 5 < content.Length && IsHex(content, i + 2, 4))
                {
                    // Valid \uXXXX escape: keep it as-is.
                    sb.Append(content, i, 6);
                    i += 5;
                }
                else
                {
                    // Invalid escape: escape the backslash so it stays literal. The following
                    // character is processed normally on the next iteration.
                    sb.Append("\\\\");
                }
            }

            return sb.ToString();
        }

        private static bool IsHex(string value, int start, int length)
        {
            for (var i = start; i < start + length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
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
