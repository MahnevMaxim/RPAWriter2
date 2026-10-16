using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Raised when the agent invokes a tool, so the UI can show progress.</summary>
    public sealed class AgentToolCall
    {
        public string ToolName { get; set; } = "";
        public string Result { get; set; } = "";
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
    public sealed class AgentRunner
    {
        private readonly IAiProvider _ai;
        private readonly AgentToolRegistry _registry;
        private readonly Func<AgentMode, string> _systemPromptFactory;
        private readonly int _maxIterations;

        /// <summary>Raised after each tool execution (or blocked attempt).</summary>
        public event EventHandler<AgentToolCall> ToolExecuted;

        public AgentRunner(
            IAiProvider ai,
            AgentToolRegistry registry,
            Func<AgentMode, string> systemPromptFactory,
            int maxIterations = 8)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _systemPromptFactory = systemPromptFactory ?? throw new ArgumentNullException(nameof(systemPromptFactory));
            _maxIterations = maxIterations > 0 ? maxIterations : 8;
        }

        public async Task<AgentRunResult> RunAsync(string userText, AgentMode mode, CancellationToken cancellationToken, IProgress<ChatStreamDelta> progress = null)
        {
            var systemPrompt = _systemPromptFactory(mode);
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = userText ?? "" }
            };

            for (var i = 0; i < _maxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ChatResponse response;
                if (_ai is IStreamingAiProvider streaming)
                {
                    response = await streaming.ChatStreamingAsync(messages, systemPrompt, progress, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    response = await _ai.ChatAsync(messages, systemPrompt, cancellationToken).ConfigureAwait(false);
                    if (progress != null && !string.IsNullOrEmpty(response.Reasoning))
                        progress.Report(new ChatStreamDelta { ReasoningDelta = response.Reasoning });
                }
                var action = AgentResponseParser.Parse(response.Answer);

                if (action.IsFinal)
                    return new AgentRunResult { FinalText = action.Text, Reasoning = response.Reasoning };

                messages.Add(new ChatMessageData { Role = "assistant", Content = response.Answer });

                var toolName = action.ToolName;
                var tool = _registry.Find(toolName);
                if (tool == null)
                {
                    messages.Add(new ChatMessageData
                    {
                        Role = "tool",
                        Content = "Unknown tool: \"" + toolName + "\". Use one of the tools listed in the system prompt."
                    });
                    continue;
                }

                if (tool.Mutates && mode != AgentMode.Act)
                {
                    var blocked = new AgentToolCall
                    {
                        ToolName = tool.Name,
                        Blocked = true,
                        Result = "Blocked: this mutating tool is not allowed in Plan mode."
                    };
                    ToolExecuted?.Invoke(this, blocked);
                    messages.Add(new ChatMessageData { Role = "tool", Content = blocked.Result });
                    continue;
                }

                string resultText;
                try
                {
                    resultText = await tool.ExecuteAsync(action.Arguments, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    resultText = "Tool \"" + tool.Name + "\" failed: " + ex.Message;
                }

                ToolExecuted?.Invoke(this, new AgentToolCall { ToolName = tool.Name, Result = Truncate(resultText), Blocked = false });
                messages.Add(new ChatMessageData { Role = "tool", Content = Truncate(resultText) });
            }

            return new AgentRunResult
            {
                FinalText = "Достигнут лимит шагов агента (" + _maxIterations + "). Уточните запрос или разбейте задачу.",
                Reasoning = ""
            };
        }

        private static string Truncate(string text, int max = 12000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max) + "\n…(truncated)";
        }
    }
}
