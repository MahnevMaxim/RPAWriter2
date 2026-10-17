using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Context
{
    /// <summary>A prepared provider request: system prompt + (possibly condensed) messages.</summary>
    public sealed class PreparedContext
    {
        public string SystemPrompt { get; set; }
        public IReadOnlyList<ChatMessageData> Messages { get; set; }
        public bool WasCondensed { get; set; }
        public int EstimatedInputTokens { get; set; }
    }

    /// <summary>
    /// Prepares a transcript for the next model call (the Cline "prepareTurn" equivalent):
    /// estimates the input size and condenses the transcript when it approaches the budget.
    /// </summary>
    public sealed class ContextAssembler
    {
        private readonly ITokenEstimator _estimator;
        private readonly IContextCondenser _condenser;

        public ContextAssembler(ITokenEstimator estimator, IContextCondenser condenser)
        {
            _estimator = estimator ?? new TokenEstimator();
            _condenser = condenser;
        }

        public async Task<PreparedContext> PrepareAsync(
            IReadOnlyList<ChatMessageData> transcript,
            string systemPrompt,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            var messages = transcript ?? (IReadOnlyList<ChatMessageData>)new List<ChatMessageData>();
            var estimated = _estimator.Estimate(systemPrompt, messages);

            var needsCondense = _condenser != null &&
                                budget.AutoCompact &&
                                (estimated > budget.MaxInputTokens || estimated > budget.CompactThresholdTokens);

            if (needsCondense)
            {
                var condensed = await _condenser.CondenseAsync(messages, budget, cancellationToken).ConfigureAwait(false);
                var effective = condensed ?? messages;
                return new PreparedContext
                {
                    SystemPrompt = systemPrompt,
                    Messages = effective,
                    WasCondensed = true,
                    EstimatedInputTokens = _estimator.Estimate(systemPrompt, effective)
                };
            }

            return new PreparedContext
            {
                SystemPrompt = systemPrompt,
                Messages = messages,
                WasCondensed = false,
                EstimatedInputTokens = estimated
            };
        }
    }
}
