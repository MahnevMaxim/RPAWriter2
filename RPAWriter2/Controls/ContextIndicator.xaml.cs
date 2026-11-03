using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPAWriter.Core.Context;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Reusable context-window indicator: a fill bar plus two text lines — the window fill
    /// ("Context window: N / M tokens (P%)") and, separately, the session-wide statistics
    /// (total tokens, cache hits, estimated cost). All formatting/severity logic lives in
    /// <see cref="ContextUsageCalculator"/>, so this control stays pure presentation.
    /// </summary>
    public partial class ContextIndicator : UserControl
    {
        private static readonly Brush IdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E)));
        private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0xB5, 0xF6)));
        private static readonly Brush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)));
        private static readonly Brush OverflowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)));

        public static readonly DependencyProperty UsedTokensProperty = DependencyProperty.Register(
            nameof(UsedTokens), typeof(int), typeof(ContextIndicator),
            new PropertyMetadata(0, OnStateChanged));

        public static readonly DependencyProperty ContextWindowSizeProperty = DependencyProperty.Register(
            nameof(ContextWindowSize), typeof(int), typeof(ContextIndicator),
            new PropertyMetadata(0, OnStateChanged));

        public static readonly DependencyProperty CompactThresholdTokensProperty = DependencyProperty.Register(
            nameof(CompactThresholdTokens), typeof(int), typeof(ContextIndicator),
            new PropertyMetadata(0, OnStateChanged));

        public static readonly DependencyProperty MaxInputTokensProperty = DependencyProperty.Register(
            nameof(MaxInputTokens), typeof(int), typeof(ContextIndicator),
            new PropertyMetadata(0, OnStateChanged));

        public static readonly DependencyProperty SessionUsageProperty = DependencyProperty.Register(
            nameof(SessionUsage), typeof(TokenUsage), typeof(ContextIndicator),
            new PropertyMetadata(null, OnStateChanged));

        public ContextIndicator()
        {
            InitializeComponent();
            Refresh();
        }

        /// <summary>Estimated input tokens occupying the window.</summary>
        public int UsedTokens
        {
            get => (int)GetValue(UsedTokensProperty);
            set => SetValue(UsedTokensProperty, value);
        }

        /// <summary>Model context window size in tokens; 0 hides the fill and shows the placeholder.</summary>
        public int ContextWindowSize
        {
            get => (int)GetValue(ContextWindowSizeProperty);
            set => SetValue(ContextWindowSizeProperty, value);
        }

        /// <summary>Token count at which the indicator turns into the warning state.</summary>
        public int CompactThresholdTokens
        {
            get => (int)GetValue(CompactThresholdTokensProperty);
            set => SetValue(CompactThresholdTokensProperty, value);
        }

        /// <summary>Token count at which the indicator turns into the overflow state.</summary>
        public int MaxInputTokens
        {
            get => (int)GetValue(MaxInputTokensProperty);
            set => SetValue(MaxInputTokensProperty, value);
        }

        /// <summary>Optional cumulative usage reported by the provider (total/cache/cost).</summary>
        public TokenUsage SessionUsage
        {
            get => (TokenUsage)GetValue(SessionUsageProperty);
            set => SetValue(SessionUsageProperty, value);
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ContextIndicator)d).Refresh();

        private void Refresh()
        {
            var info = ContextUsageCalculator.Compute(
                UsedTokens,
                ContextWindowSize,
                CompactThresholdTokens,
                MaxInputTokens,
                SessionUsage);

            FillBar.Value = info.RatioPercent;
            FillBar.Foreground = BrushFor(info.Status);

            FillText.Text = info.FillText;

            var stats = info.StatsText;
            StatsText.Text = stats;
            StatsText.Visibility = string.IsNullOrEmpty(stats) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Brush BrushFor(ContextUsageStatus status)
        {
            switch (status)
            {
                case ContextUsageStatus.Overflow:
                    return OverflowBrush;
                case ContextUsageStatus.Warning:
                    return WarningBrush;
                case ContextUsageStatus.Normal:
                    return NormalBrush;
                default:
                    return IdleBrush;
            }
        }

        private static Brush Freeze(Brush brush)
        {
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }
    }
}
