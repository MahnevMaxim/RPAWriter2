using System;

namespace RPAWriter.Core.Context
{
    /// <summary>
    /// Context-window budget that drives compaction and recovery in the agent loop.
    /// All values are configurable; defaults follow the agreed plan.
    /// </summary>
    public sealed class ContextBudget
    {
        /// <summary>Model context window size in tokens.</summary>
        public int ContextWindowSize { get; set; } = 409600;

        /// <summary>Fraction of the window reserved for the model's reply (0.12 = 12%).</summary>
        public double ReserveFraction { get; set; } = 0.12;

        /// <summary>Fraction of the window that triggers compaction (0.85 = 85%).</summary>
        public double CompactThresholdFraction { get; set; } = 0.85;

        /// <summary>How many most-recent messages are never summarized/truncated.</summary>
        public int KeepRecentMessages { get; set; } = 6;

        /// <summary>Maximum characters of a tool result fed back into the transcript.</summary>
        public int ToolResultMaxChars { get; set; } = 12000;

        /// <summary>Use the "tool" role for tool results; false falls back to "user" for compatibility.</summary>
        public bool UseToolRole { get; set; } = true;

        /// <summary>Maximum number of agent-loop iterations per run.</summary>
        public int MaxIterations { get; set; } = 8;

        /// <summary>When false, automatic compaction before a request is disabled (manual compaction still works).</summary>
        public bool AutoCompact { get; set; } = true;

        /// <summary>Tokens reserved for the model's reply.</summary>
        public int ReserveTokens => (int)(ContextWindowSize * ReserveFraction);

        /// <summary>Input size (tokens) at which compaction should be triggered.</summary>
        public int CompactThresholdTokens => (int)(ContextWindowSize * CompactThresholdFraction);

        /// <summary>Maximum input tokens that still leave room for the reserved reply.</summary>
        public int MaxInputTokens => Math.Max(1, ContextWindowSize - ReserveTokens);

        /// <summary>Builds a budget with defaults, optionally overriding the window size.</summary>
        public static ContextBudget Default(int contextWindowSize)
        {
            var budget = new ContextBudget();
            if (contextWindowSize > 0)
                budget.ContextWindowSize = contextWindowSize;
            return budget;
        }
    }
}
