using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Context
{
    /// <summary>
    /// Condenses a transcript so it fits the context budget: summarizes older messages
    /// through the same model, falling back to dropping the oldest messages when
    /// summarization is unavailable or fails.
    /// </summary>
    public sealed class ContextCondenser : IContextCondenser
    {
        private readonly IAiProvider _ai;
        private readonly ITokenEstimator _estimator;
        private readonly string _compactSystemPrompt;

        public ContextCondenser(IAiProvider ai, ITokenEstimator estimator, string compactSystemPrompt)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _estimator = estimator ?? new TokenEstimator();
            _compactSystemPrompt = compactSystemPrompt;
        }

        public async Task<IReadOnlyList<ChatMessageData>> CondenseAsync(
            IReadOnlyList<ChatMessageData> transcript,
            ContextBudget budget,
            CancellationToken cancellationToken)
        {
            if (transcript == null || transcript.Count == 0)
                return transcript;

            var keepRecent = Math.Max(1, budget?.KeepRecentMessages ?? 6);
            if (transcript.Count <= keepRecent)
                return transcript;

            var older = transcript.Take(transcript.Count - keepRecent).ToList();
            var keep = transcript.Skip(transcript.Count - keepRecent).ToList();

            var summary = await TrySummarizeAsync(older, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                var result = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "assistant", Content = summary }
                };
                result.AddRange(keep);
                return result;
            }

            return TruncateToBudget(transcript, keepRecent, budget);
        }

        private async Task<string> TrySummarizeAsync(List<ChatMessageData> older, CancellationToken cancellationToken)
        {
            if (older == null || older.Count == 0)
                return null;

            try
            {
                var transcriptText = BuildTranscriptText(older);
                var messages = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = transcriptText }
                };
                var response = await _ai.ChatAsync(messages, _compactSystemPrompt, cancellationToken).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(response?.Answer) ? null : response.Answer.Trim();
            }
            catch
            {
                // Summarization is best-effort; the caller falls back to truncation.
                return null;
            }
        }

        private static string BuildTranscriptText(IEnumerable<ChatMessageData> messages)
        {
            var sb = new StringBuilder();
            foreach (var message in messages)
            {
                if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    continue;
                sb.Append(message.Role).Append(": ").AppendLine(message.Content);
            }
            return sb.ToString().Trim();
        }

        private List<ChatMessageData> TruncateToBudget(
            IReadOnlyList<ChatMessageData> transcript,
            int keepRecent,
            ContextBudget budget)
        {
            var list = transcript.ToList();
            var maxInput = budget != null ? budget.MaxInputTokens : int.MaxValue;

            while (list.Count > keepRecent && _estimator.Estimate(string.Empty, list) > maxInput)
                list.RemoveAt(0);

            return list;
        }
    }
}
