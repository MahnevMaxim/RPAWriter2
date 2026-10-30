using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Raised when the agent invokes a tool, so the UI can show progress.</summary>
    public sealed class AgentToolCall
    {
        public string ToolName { get; set; } = "";
        public string Result { get; set; } = "";

        /// <summary>Short text for the chat progress line (e.g. a file path). Empty means use <see cref="Result"/>.</summary>
        public string DisplayResult { get; set; } = "";
        public bool Blocked { get; set; }
    }

    /// <summary>Result of an agent run: final text and whether any tool was blocked.</summary>
    public sealed class AgentRunResult
    {
        public string FinalText { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public bool ToolBlocked { get; set; }
        public string BlockedTool { get; set; } = "";
    }

    /// <summary>
    /// Runs the agentic tool loop: sends the user message, executes tool calls,
    /// feeds results back, and returns the final answer.
    /// </summary>
    /// <summary>Raised when the agent loop performs a recovery retry (overflow compact or max-tokens nudge).</summary>
    public sealed class AgentRetryArgs : EventArgs
    {
        public string Kind { get; set; } = "";
        public int Attempt { get; set; }
        public int AttemptLimit { get; set; }
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// A resumable snapshot of an in-progress agent loop. The UI stores the last reported
    /// snapshot while the run is streaming so a manual Cancel can be continued from the same
    /// breakpoint instead of restarting the whole task.
    /// </summary>
    public sealed class AgentResumeSnapshot
    {
        /// <summary>Transcript prefix as of the start of the next iteration (provider cache-hit friendly).</summary>
        public IReadOnlyList<ChatMessageData> Messages { get; set; } = new List<ChatMessageData>();

        /// <summary>Zero-based iteration index the resumed run should continue from.</summary>
        public int NextIteration { get; set; }
    }

    public sealed class AgentRunner
    {
        private const int MaxTokensRecoveryLimit = 3;
        private static string MaxTokensRecoveryNudge =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-max-tokens-recovery-nudge.txt", PromptDefaults.MaxTokensRecoveryNudge);

        private const int ProtocolRecoveryLimit = 2;
        private static string ProtocolRecoveryNudge =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-protocol-recovery-nudge.txt", PromptDefaults.ProtocolRecoveryNudge);

        private readonly IAiProvider _ai;
        private readonly AgentToolRegistry _registry;
        private readonly Func<AgentMode, string> _systemPromptFactory;
        private readonly ToolCallingMode _toolCallingMode;
        private readonly AgentPermissions _allowedPermissions;
        private readonly ContextBudget _budget;
        private readonly ITokenEstimator _estimator;
        private readonly IContextCondenser _condenser;
        private readonly ContextAssembler _assembler;

        /// <summary>Raised after each tool execution (or blocked attempt).</summary>
        public event EventHandler<AgentToolCall> ToolExecuted;

        /// <summary>Raised when the context is automatically compacted, so the UI can show a notice.</summary>
        public event EventHandler ContextCompacted;

        /// <summary>Raised for every recovery retry (overflow compact or max-tokens nudge).</summary>
        public event EventHandler<AgentRetryArgs> RetryOccurred;

        /// <summary>Backward-compatible constructor: a fixed iteration cap with default budget thresholds.</summary>
        public AgentRunner(
            IAiProvider ai,
            AgentToolRegistry registry,
            Func<AgentMode, string> systemPromptFactory,
            int maxIterations = 8,
            ToolCallingMode toolCallingMode = ToolCallingMode.JsonChat,
            AgentPermissions allowedPermissions = AgentPermissions.All)
            : this(ai, registry, systemPromptFactory, new ContextBudget { MaxIterations = maxIterations }, null, toolCallingMode, allowedPermissions)
        {
        }

        public AgentRunner(
            IAiProvider ai,
            AgentToolRegistry registry,
            Func<AgentMode, string> systemPromptFactory,
            ContextBudget budget,
            string compactSystemPrompt = null,
            ToolCallingMode toolCallingMode = ToolCallingMode.JsonChat,
            AgentPermissions allowedPermissions = AgentPermissions.All)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _systemPromptFactory = systemPromptFactory ?? throw new ArgumentNullException(nameof(systemPromptFactory));
            _toolCallingMode = toolCallingMode;
            _allowedPermissions = allowedPermissions;
            _budget = budget ?? new ContextBudget();

            _estimator = new TokenEstimator();
            var prompt = compactSystemPrompt ?? PromptLibrary.Load(
                PromptPaths.ResolvePromptsDirectory(),
                PromptLibrary.Categories.System,
                "agent-compact.txt",
                PromptDefaults.AgentCompactSystemPrompt);
            _condenser = new ContextCondenser(_ai, _estimator, prompt);
            _assembler = new ContextAssembler(_estimator, _condenser);
        }

        /// <summary>Backward-compatible single-shot run: no prior transcript, default budget.</summary>
        public Task<AgentRunResult> RunAsync(string userText, AgentMode mode, CancellationToken cancellationToken, IProgress<ChatStreamDelta> progress = null)
        {
            return RunAsync(Array.Empty<ChatMessageData>(), userText, mode, _budget, cancellationToken, progress);
        }

        /// <summary>
        /// Runs the agentic tool loop over a transcript (prior conversation history), appending
        /// the new user message. The transcript is not mutated; tool results are appended per-turn.
        /// </summary>
        public async Task<AgentRunResult> RunAsync(
            IReadOnlyList<ChatMessageData> history,
            string userText,
            AgentMode mode,
            ContextBudget budget,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress = null)
        {
            return await RunAsync(history, userText, null, mode, budget, cancellationToken, progress).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the agentic tool loop with optional images attached to the new user message.
        /// Images are forwarded to the provider as multimodal content parts.
        /// </summary>
        public async Task<AgentRunResult> RunAsync(
            IReadOnlyList<ChatMessageData> history,
            string userText,
            IReadOnlyList<ChatImagePart> images,
            AgentMode mode,
            ContextBudget budget,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress = null,
            IProgress<AgentResumeSnapshot> resumeProgress = null)
        {
            var effectiveBudget = budget ?? _budget;
            var systemPrompt = _systemPromptFactory(mode);

            var messages = new List<ChatMessageData>();
            if (history != null)
                messages.AddRange(history);

            var userMessage = new ChatMessageData { Role = "user", Content = userText ?? "" };
            if (images != null && images.Count > 0)
                userMessage.Images = images;
            messages.Add(userMessage);

            var maxIterations = effectiveBudget.MaxIterations > 0 ? effectiveBudget.MaxIterations : 8;
            var toolDefinitions = ToolSchemaConverter.BuildToolDefinitions(_registry, mode, _allowedPermissions);

            return await RunLoopAsync(messages, 0, systemPrompt, toolDefinitions, maxIterations, mode, effectiveBudget, cancellationToken, progress, resumeProgress).ConfigureAwait(false);
        }

        /// <summary>
        /// Continues a previously cancelled run from a saved snapshot. The transcript is not
        /// rebuilt from UI history; it resumes with the exact message prefix captured at the
        /// breakpoint, so the provider's prompt cache hits and already-executed tools are not
        /// re-run (only the single interrupted step is re-generated).
        /// </summary>
        public async Task<AgentRunResult> ResumeAsync(
            AgentResumeSnapshot snapshot,
            AgentMode mode,
            ContextBudget budget,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress = null,
            IProgress<AgentResumeSnapshot> resumeProgress = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var effectiveBudget = budget ?? _budget;
            var systemPrompt = _systemPromptFactory(mode);
            var messages = new List<ChatMessageData>(snapshot.Messages ?? new List<ChatMessageData>());
            var maxIterations = effectiveBudget.MaxIterations > 0 ? effectiveBudget.MaxIterations : 8;
            var toolDefinitions = ToolSchemaConverter.BuildToolDefinitions(_registry, mode, _allowedPermissions);

            return await RunLoopAsync(messages, snapshot.NextIteration, systemPrompt, toolDefinitions, maxIterations, mode, effectiveBudget, cancellationToken, progress, resumeProgress).ConfigureAwait(false);
        }

        /// <summary>Shared agentic tool loop; <paramref name="startIteration"/> resumes a saved snapshot mid-run.</summary>
        private async Task<AgentRunResult> RunLoopAsync(
            List<ChatMessageData> messages,
            int startIteration,
            string systemPrompt,
            IReadOnlyList<ToolDefinition> toolDefinitions,
            int maxIterations,
            AgentMode mode,
            ContextBudget effectiveBudget,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress,
            IProgress<AgentResumeSnapshot> resumeProgress)
        {
            var protocolAttempts = 0;

            for (var i = startIteration; i < maxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Publish a resumable checkpoint before mutating the transcript for this
                // iteration. Cancelling now re-runs only this iteration: the partial model
                // answer is regenerated and any in-flight tool call is re-executed.
                resumeProgress?.Report(new AgentResumeSnapshot
                {
                    Messages = new List<ChatMessageData>(messages),
                    NextIteration = i
                });

                var prepared = await PrepareContextAsync(messages, systemPrompt, effectiveBudget, cancellationToken).ConfigureAwait(false);
                var response = await CallWithRecoveryAsync(prepared, toolDefinitions, effectiveBudget, cancellationToken, progress).ConfigureAwait(false);

                if (_toolCallingMode == ToolCallingMode.Native)
                {
                    var nativeCalls = response.ToolCalls;
                    if (nativeCalls == null || nativeCalls.Count == 0)
                        return new AgentRunResult { FinalText = response.Answer ?? "", Reasoning = response.Reasoning };

                    // Record the assistant message that requested the tool calls.
                    messages.Add(new ChatMessageData { Role = "assistant", Content = response.Answer ?? "", ToolCalls = nativeCalls });

                    foreach (var call in nativeCalls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var finalText = await ExecuteNativeToolCallAsync(messages, call, mode, effectiveBudget, cancellationToken).ConfigureAwait(false);
                        if (finalText != null)
                            return new AgentRunResult { FinalText = finalText, Reasoning = response.Reasoning };
                    }
                    continue;
                }

                var action = AgentResponseParser.Parse(response.Answer);

                if (action.IsFinal)
                {
                    if (AgentResponseParser.LooksLikeToolMarkup(response.Answer) && protocolAttempts < ProtocolRecoveryLimit)
                    {
                        protocolAttempts++;
                        messages.Add(new ChatMessageData { Role = "assistant", Content = response.Answer });
                        messages.Add(new ChatMessageData { Role = "user", Content = ProtocolRecoveryNudge });
                        continue;
                    }

                    return new AgentRunResult { FinalText = action.Text, Reasoning = response.Reasoning };
                }

                messages.Add(new ChatMessageData { Role = "assistant", Content = response.Answer });

                foreach (var call in action.ToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var finalText = await ExecuteJsonToolCallAsync(messages, call, mode, effectiveBudget, cancellationToken).ConfigureAwait(false);
                    if (finalText != null)
                        return new AgentRunResult { FinalText = finalText, Reasoning = response.Reasoning };
                }
            }

            return new AgentRunResult
            {
                FinalText = "Достигнут лимит шагов агента (" + maxIterations + "). Уточните запрос или разбейте задачу.",
                Reasoning = ""
            };
        }

        private async Task<PreparedContext> PrepareContextAsync(
            IReadOnlyList<ChatMessageData> messages,
            string systemPrompt,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            var prepared = await _assembler.PrepareAsync(messages, systemPrompt, budget, cancellationToken).ConfigureAwait(false);
            if (prepared.WasCondensed)
                ContextCompacted?.Invoke(this, EventArgs.Empty);
            return prepared;
        }

        private async Task<ChatResponse> CallWithRecoveryAsync(
            PreparedContext prepared,
            IReadOnlyList<ToolDefinition> tools,
            ContextBudget budget,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress)
        {
            var current = prepared;
            var overflowRecovered = false;
            var maxTokensAttempts = 0;

            while (true)
            {
                ChatResponse response;
                try
                {
                    response = await CallModelAsync(current, tools, cancellationToken, progress).ConfigureAwait(false);
                }
                catch (ProviderErrorException ex) when (ex.Kind == ProviderErrorKind.ContextWindowExceeded && !overflowRecovered)
                {
                    overflowRecovered = true;
                    RetryOccurred?.Invoke(this, new AgentRetryArgs
                    {
                        Kind = "context-overflow",
                        Attempt = 1,
                        AttemptLimit = 1,
                        Detail = "Context window exceeded -> compact + retry"
                    });
                    var condensed = await _condenser.CondenseAsync(current.Messages, budget, cancellationToken).ConfigureAwait(false);
                    ContextCompacted?.Invoke(this, EventArgs.Empty);
                    var effective = condensed ?? current.Messages;
                    current = new PreparedContext
                    {
                        SystemPrompt = current.SystemPrompt,
                        Messages = effective,
                        WasCondensed = true,
                        EstimatedInputTokens = _estimator.Estimate(current.SystemPrompt, effective)
                    };
                    continue;
                }

                if (maxTokensAttempts < MaxTokensRecoveryLimit && IsTruncatedWithoutToolCall(response))
                {
                    maxTokensAttempts++;
                    RetryOccurred?.Invoke(this, new AgentRetryArgs
                    {
                        Kind = "max-tokens",
                        Attempt = maxTokensAttempts,
                        AttemptLimit = MaxTokensRecoveryLimit,
                        Detail = "Response truncated at output limit -> nudge + retry"
                    });
                    current = new PreparedContext
                    {
                        SystemPrompt = current.SystemPrompt,
                        Messages = AppendNudge(current.Messages, response),
                        WasCondensed = current.WasCondensed,
                        EstimatedInputTokens = current.EstimatedInputTokens
                    };
                    continue;
                }

                return response;
            }
        }

        private async Task<ChatResponse> CallModelAsync(
            PreparedContext prepared,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken,
            IProgress<ChatStreamDelta> progress)
        {
            if (_ai is IStreamingAiProvider streaming)
                return await streaming.ChatStreamingAsync(prepared.Messages, prepared.SystemPrompt, tools, progress, cancellationToken).ConfigureAwait(false);

            var response = await _ai.ChatAsync(prepared.Messages, prepared.SystemPrompt, tools, cancellationToken).ConfigureAwait(false);
            if (progress != null && !string.IsNullOrEmpty(response?.Reasoning))
                progress.Report(new ChatStreamDelta { ReasoningDelta = response.Reasoning });
            return response;
        }

        /// <summary>Executes one native tool call and appends the tool result to the transcript.</summary>
        private async Task<string> ExecuteNativeToolCallAsync(
            List<ChatMessageData> messages,
            NativeToolCall call,
            AgentMode mode,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            if (!_registry.TryGetExecutableTool(call.Name, mode, _allowedPermissions, out var tool, out var blockReason))
            {
                if (tool == null)
                {
                    messages.Add(new ChatMessageData
                    {
                        Role = "tool",
                        Content = blockReason,
                        ToolCallId = call.Id ?? ""
                    });
                }
                else
                {
                    var blocked = new AgentToolCall
                    {
                        ToolName = tool.Name,
                        Blocked = true,
                        Result = blockReason
                    };
                    ToolExecuted?.Invoke(this, blocked);
                    messages.Add(new ChatMessageData { Role = "tool", Content = blockReason, ToolCallId = call.Id ?? "" });
                }
                return null;
            }

            var args = call.Arguments ?? new JObject();
            string resultText;
            try
            {
                resultText = await tool.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultText = "Tool \"" + tool.Name + "\" failed: " + ex.Message;
            }

            string displayText = null;
            try
            {
                displayText = await tool.GetDisplayResultAsync(args, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Display text is best-effort; fall back to the full result below.
            }

            var truncated = Truncate(resultText, budget.ToolResultMaxChars);
            ToolExecuted?.Invoke(this, new AgentToolCall
            {
                ToolName = tool.Name,
                Result = truncated,
                DisplayResult = string.IsNullOrWhiteSpace(displayText) ? "" : displayText,
                Blocked = false
            });

            messages.Add(new ChatMessageData
            {
                Role = "tool",
                Content = "Tool \"" + tool.Name + "\" returned:\n" + truncated,
                ToolCallId = call.Id ?? ""
            });
            return tool.CompletesRun ? resultText : null;
        }

        /// <summary>Executes one JSON-protocol tool call and appends the tool result to the transcript.</summary>
        private async Task<string> ExecuteJsonToolCallAsync(
            List<ChatMessageData> messages,
            AgentToolCallSpec call,
            AgentMode mode,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            if (!_registry.TryGetExecutableTool(call.ToolName, mode, _allowedPermissions, out var tool, out var blockReason))
            {
                if (tool == null)
                {
                    messages.Add(new ChatMessageData
                    {
                        Role = "user",
                        Content = blockReason
                    });
                }
                else
                {
                    var blocked = new AgentToolCall
                    {
                        ToolName = tool.Name,
                        Blocked = true,
                        Result = blockReason
                    };
                    ToolExecuted?.Invoke(this, blocked);
                    messages.Add(new ChatMessageData { Role = "user", Content = blockReason });
                }
                return null;
            }

            string resultText;
            try
            {
                resultText = await tool.ExecuteAsync(call.Arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                resultText = "Tool \"" + tool.Name + "\" failed: " + ex.Message;
            }

            string displayText = null;
            try
            {
                displayText = await tool.GetDisplayResultAsync(call.Arguments, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Display text is best-effort; fall back to the full result below.
            }

            var truncated = Truncate(resultText, budget.ToolResultMaxChars);
            ToolExecuted?.Invoke(this, new AgentToolCall
            {
                ToolName = tool.Name,
                Result = truncated,
                DisplayResult = string.IsNullOrWhiteSpace(displayText) ? "" : displayText,
                Blocked = false
            });

            var toolRole = budget.UseToolRole ? "tool" : "user";
            messages.Add(new ChatMessageData
            {
                Role = toolRole,
                Content = "Tool \"" + tool.Name + "\" returned:\n" + truncated
            });
            return tool.CompletesRun ? resultText : null;
        }

        /// <summary>Manually condenses a transcript (e.g. "Compact now" in the UI).</summary>
        public Task<IReadOnlyList<ChatMessageData>> CompactAsync(
            IReadOnlyList<ChatMessageData> transcript,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            return _condenser.CondenseAsync(transcript, budget, cancellationToken);
        }

        private static bool IsTruncatedWithoutToolCall(ChatResponse response)
        {
            if (response == null || !string.Equals(response.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                return false;
            return AgentResponseParser.Parse(response.Answer).IsFinal;
        }

        private static IReadOnlyList<ChatMessageData> AppendNudge(IReadOnlyList<ChatMessageData> messages, ChatResponse response)
        {
            var list = new List<ChatMessageData>(messages);
            list.Add(new ChatMessageData { Role = "assistant", Content = response?.Answer ?? "" });
            list.Add(new ChatMessageData { Role = "user", Content = MaxTokensRecoveryNudge });
            return list;
        }

        private static string Truncate(string text, int max)
        {
            if (max <= 0)
                max = 12000;
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text;
            return text.Substring(0, max) + "\n…(truncated)";
        }
    }
}
