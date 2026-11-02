using System;
using System.Globalization;
using System.Windows.Data;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Maps <see cref="AgentMode"/> to the toggle value shown in the mode switch ("Plan", "Act",
    /// "Help") and back.
    /// <para>
    /// The mapping itself lives in Core (<see cref="AgentModeNames"/>) because the persisted
    /// session state and the transcript entry use the same names; this class only adapts it to a
    /// binding. Unknown or not-yet-initialized values fall back to
    /// <see cref="AgentMode.Plan"/>, so the two-way binding on
    /// <c>SegmentedToggle.SelectedValue</c> can never fail to convert.
    /// </para>
    /// </summary>
    public sealed class AgentModeToToggleValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return AgentModeNames.ToValue(value is AgentMode mode ? mode : AgentMode.Plan);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return AgentModeNames.Parse(value);
        }
    }
}
