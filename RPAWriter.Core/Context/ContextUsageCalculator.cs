using System;
using System.Globalization;
using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Context
{
    /// <summary>Visual severity of the context-window indicator.</summary>
    public enum ContextUsageStatus
    {
        /// <summary>No provider or context window is available; show the placeholder.</summary>
        Idle,

        /// <summary>Usage is inside the normal range.</summary>
        Normal,

        /// <summary>Usage reached the auto-compact threshold.</summary>
        Warning,

        /// <summary>Usage reached the maximum input budget (window minus reserve).</summary>
        Overflow
    }

    /// <summary>
    /// Formatted output of the context-window indicator: the "how full is the window"
    /// line plus a separate session-wide statistics line. Keeping them apart avoids the
    /// previous confusion of mixing the per-request input estimate with cumulative totals.
    /// </summary>
    public sealed class ContextUsageInfo
    {
        /// <summary>Estimated input tokens occupying the window (never negative).</summary>
        public int UsedTokens { get; set; }

        /// <summary>Model context window size in tokens (never negative).</summary>
        public int ContextWindowSize { get; set; }

        /// <summary>Fill ratio, clamped to 0..100; meaningful only when <see cref="IsAvailable"/> is true.</summary>
        public double RatioPercent { get; set; }

        /// <summary>Fill ratio of the cache-hit segment (0..100), clamped to <see cref="RatioPercent"/>.</summary>
        public double CacheHitRatioPercent { get; set; }

        /// <summary>Visual severity derived from the configured thresholds.</summary>
        public ContextUsageStatus Status { get; set; }

        /// <summary>False when there is no context window to show; the UI then renders a placeholder.</summary>
        public bool IsAvailable { get; set; }

        /// <summary>Primary line, e.g. "Context window: 12,345 / 409,600 tokens (3%)".</summary>
        public string FillText { get; set; } = "Context window: —";

        /// <summary>Secondary session-statistics line, e.g. "Total 45,000  ·  Cache hit 8,000  ·  $0.1234".</summary>
        public string StatsText { get; set; } = "";

        /// <summary>Per-request cache split line, e.g. "Cache hit / miss: 14,000 / 3,000"; empty when there is no cache activity.</summary>
        public string CacheText { get; set; } = "";
    }

    /// <summary>
    /// Pure formatting/severity logic for the context-window indicator. Intended to be
    /// consumed both by the host (to render) and by unit tests, with no WPF dependency.
    /// </summary>
    public static class ContextUsageCalculator
    {
        /// <summary>
        /// Computes the indicator state from the estimated input tokens, the context window
        /// size, the two budget thresholds (compact and max-input), the optional cumulative
        /// session usage reported by the provider and the cache-hit/cache-miss tokens of the
        /// most recent request (the light-green segment is clamped to the used ratio; the
        /// hit/miss split is also rendered as <see cref="ContextUsageInfo.CacheText"/>).
        /// </summary>
        public static ContextUsageInfo Compute(
            int usedTokens,
            int contextWindowSize,
            int compactThresholdTokens,
            int maxInputTokens,
            TokenUsage sessionUsage,
            int cacheHitTokens = 0,
            int cacheMissTokens = 0)
        {
            var info = new ContextUsageInfo
            {
                UsedTokens = Math.Max(0, usedTokens),
                ContextWindowSize = Math.Max(0, contextWindowSize)
            };

            info.CacheText = BuildCacheText(cacheHitTokens, cacheMissTokens);

            if (info.ContextWindowSize <= 0)
            {
                info.IsAvailable = false;
                info.Status = ContextUsageStatus.Idle;
                info.FillText = "Context window: —";
                info.StatsText = BuildStatsText(sessionUsage);
                return info;
            }

            info.IsAvailable = true;
            info.RatioPercent = Math.Min(100.0, 100.0 * info.UsedTokens / info.ContextWindowSize);
            info.CacheHitRatioPercent = Math.Min(
                info.RatioPercent,
                Math.Min(100.0, 100.0 * Math.Max(0, cacheHitTokens) / info.ContextWindowSize));
            info.FillText = string.Format(
                CultureInfo.InvariantCulture,
                "Context window: {0:N0} / {1:N0} tokens ({2}%)",
                info.UsedTokens,
                info.ContextWindowSize,
                (int)Math.Round(info.RatioPercent));
            info.Status = ComputeStatus(info.UsedTokens, compactThresholdTokens, maxInputTokens);
            info.StatsText = BuildStatsText(sessionUsage);
            return info;
        }

        private static ContextUsageStatus ComputeStatus(int usedTokens, int compactThresholdTokens, int maxInputTokens)
        {
            if (maxInputTokens > 0 && usedTokens >= maxInputTokens)
                return ContextUsageStatus.Overflow;

            if (compactThresholdTokens > 0 && usedTokens >= compactThresholdTokens)
                return ContextUsageStatus.Warning;

            return ContextUsageStatus.Normal;
        }

        private static string BuildCacheText(int cacheHitTokens, int cacheMissTokens)
        {
            if (cacheHitTokens <= 0 && cacheMissTokens <= 0)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Cache hit / miss: {0:N0} / {1:N0}",
                Math.Max(0, cacheHitTokens),
                Math.Max(0, cacheMissTokens));
        }

        private static string BuildStatsText(TokenUsage usage)
        {
            if (usage == null)
                return string.Empty;

            var parts = new StringBuilder();
            if (usage.TotalTokens > 0)
                parts.Append("Total ").Append(usage.TotalTokens.ToString("N0", CultureInfo.InvariantCulture));

            if (usage.CacheHitTokens > 0)
            {
                AppendSeparator(parts);
                parts.Append("Cache hit ").Append(usage.CacheHitTokens.ToString("N0", CultureInfo.InvariantCulture));
            }

            if (usage.EstimatedCostUsd > 0)
            {
                AppendSeparator(parts);
                parts.Append("$").Append(usage.EstimatedCostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
            }

            return parts.ToString();
        }

        private static void AppendSeparator(StringBuilder parts)
        {
            if (parts.Length > 0)
                parts.Append("  ·  ");
        }
    }
}
