using System;
using System.Collections.Generic;
using System.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Retrieves similar tasks from the knowledge base: token-level similarity (Jaccard)
    /// with boosts for matching activity type and action.
    /// </summary>
    public class SimilarityIndex
    {
        public IReadOnlyList<SimilarTaskMatch> FindSimilar(
            TaskDescriptor query,
            IReadOnlyList<SolutionRecord> records,
            int topK)
        {
            if (records == null || records.Count == 0) return Array.Empty<SimilarTaskMatch>();

            var queryTokens = BuildTokens(query);
            var matches = new List<SimilarTaskMatch>();

            foreach (var record in records)
            {
                foreach (var task in record.Tasks ?? new List<TaskDescriptor>())
                {
                    var score = Score(query, task, queryTokens);
                    if (score <= 0) continue;
                    matches.Add(new SimilarTaskMatch
                    {
                        Query = query,
                        Solution = record,
                        Score = score,
                        Reasons = BuildReasons(query, task)
                    });
                }
            }

            return matches
                .OrderByDescending(m => m.Score)
                .Take(topK)
                .ToList();
        }

        private static IReadOnlyList<string> BuildTokens(TaskDescriptor task)
        {
            var tokens = new List<string>();
            tokens.AddRange(TextNormalizer.Tokenize(task.Action ?? ""));
            tokens.AddRange(TextNormalizer.Tokenize(task.Target ?? ""));
            tokens.AddRange(TextNormalizer.Tokenize(task.Description ?? ""));
            tokens.AddRange(TextNormalizer.NormalizeKeywords(task.Keywords));
            return tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static double Score(TaskDescriptor query, TaskDescriptor candidate, IReadOnlyList<string> queryTokens)
        {
            var candidateTokens = BuildTokens(candidate);
            if (queryTokens.Count == 0 || candidateTokens.Count == 0) return 0;

            var qSet = new HashSet<string>(queryTokens, StringComparer.OrdinalIgnoreCase);
            int overlap = candidateTokens.Count(t => qSet.Contains(t));
            double jaccard = overlap / (double)(qSet.Count + candidateTokens.Count - overlap);

            double activityBoost = 0;
            if (!string.IsNullOrWhiteSpace(query.ActivityType) &&
                string.Equals(query.ActivityType, candidate.ActivityType, StringComparison.OrdinalIgnoreCase))
                activityBoost = 0.2;

            double actionBoost = 0;
            if (!string.IsNullOrWhiteSpace(query.Action) &&
                string.Equals(query.Action, candidate.Action, StringComparison.OrdinalIgnoreCase))
                actionBoost = 0.3;

            return Math.Min(1.0, jaccard + activityBoost + actionBoost);
        }

        private static List<string> BuildReasons(TaskDescriptor query, TaskDescriptor candidate)
        {
            var reasons = new List<string>();
            if (string.Equals(query.ActivityType, candidate.ActivityType, StringComparison.OrdinalIgnoreCase))
                reasons.Add("matched activity type: " + query.ActivityType);
            if (string.Equals(query.Action, candidate.Action, StringComparison.OrdinalIgnoreCase))
                reasons.Add("matched action: " + query.Action);
            if (!string.IsNullOrWhiteSpace(query.Target) &&
                string.Equals(query.Target, candidate.Target, StringComparison.OrdinalIgnoreCase))
                reasons.Add("matched target: " + query.Target);
            if (reasons.Count == 0) reasons.Add("keyword overlap");
            return reasons;
        }
    }
}
