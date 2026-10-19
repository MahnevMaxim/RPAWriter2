using System;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Runs a nested agent loop for a sub-agent (spawn_agent): a fresh context window,
    /// the same registry/tools as the parent (gated by the current mode + permissions),
    /// and a custom system prompt that extends the base agent prompt with the sub-agent's
    /// role/behavior instructions. The sub-agent's final answer is returned as text.
    /// </summary>
    public static class SubAgentRunner
    {
        public static async Task<string> RunAsync(
            IAiProvider ai,
            AgentToolRegistry registry,
            AgentMode mode,
            ToolCallingMode toolCallingMode,
            AgentPermissions allowedPermissions,
            ContextBudget budget,
            Func<AgentMode, string> systemPromptFactory,
            string instructions,
            string task,
            CancellationToken cancellationToken)
        {
            if (ai == null) throw new ArgumentNullException(nameof(ai));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (systemPromptFactory == null) throw new ArgumentNullException(nameof(systemPromptFactory));

            var trimmedInstructions = (instructions ?? string.Empty).Trim();
            var effectiveBudget = budget ?? new ContextBudget();

            string SubPrompt(AgentMode m)
            {
                var basePrompt = systemPromptFactory(m) ?? string.Empty;
                return string.IsNullOrEmpty(trimmedInstructions)
                    ? basePrompt
                    : basePrompt + "\n\n" + trimmedInstructions;
            }

            var runner = new AgentRunner(
                ai,
                registry,
                SubPrompt,
                effectiveBudget,
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowedPermissions);

            var result = await runner.RunAsync(
                Array.Empty<ChatMessageData>(),
                task ?? string.Empty,
                mode,
                effectiveBudget,
                cancellationToken).ConfigureAwait(false);

            return result?.FinalText ?? string.Empty;
        }
    }
}
