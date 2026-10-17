using System;
using System.Collections.Generic;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Context
{
    /// <summary>
    /// Conservative, provider-agnostic token estimator: roughly ceil(chars/4) plus a
    /// small per-message overhead. Intended to keep an outgoing request safely inside
    /// the context window for OpenAI-compatible endpoints that do not expose a tokenizer.
    /// </summary>
    public sealed class TokenEstimator : ITokenEstimator
    {
        private const int CharsPerToken = 4;
        private const int PerMessageOverhead = 4;

        public int Estimate(string systemPrompt, IReadOnlyList<ChatMessageData> messages)
        {
            var total = 0;
            if (!string.IsNullOrEmpty(systemPrompt))
                total += EstimateText(systemPrompt);

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    if (message == null)
                        continue;
                    total += PerMessageOverhead;
                    total += EstimateText(message.Content);
                }
            }

            return Math.Max(0, total);
        }

        private static int EstimateText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return (text.Length + CharsPerToken - 1) / CharsPerToken;
        }
    }
}
