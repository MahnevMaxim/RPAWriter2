using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Compares a value with <see cref="Match"/> (as text, case-insensitive) and returns
    /// <see cref="WhenMatch"/> or <see cref="WhenDifferent"/>, converted to the type the binding
    /// target expects.
    /// <para>
    /// Lets one converter serve every "show/enable this control only in that phase" rule without
    /// pulling enum types into the XAML:
    /// <c>Match="Streaming" WhenMatch="Visible" WhenDifferent="Collapsed"</c> for a Visibility
    /// binding and <c>WhenMatch="False" WhenDifferent="True"</c> for an IsEnabled binding.
    /// </para>
    /// </summary>
    public sealed class EnumMatchConverter : IValueConverter
    {
        /// <summary>Value the source is compared with ("Streaming" for <c>AgentPhase.Streaming</c>).</summary>
        public string Match { get; set; } = "";

        /// <summary>Result when the values are equal (default <c>"True"</c>).</summary>
        public string WhenMatch { get; set; } = "True";

        /// <summary>Result when the values differ (default <c>"False"</c>).</summary>
        public string WhenDifferent { get; set; } = "False";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var matches = string.Equals(ToText(value), Match ?? "", StringComparison.OrdinalIgnoreCase);
            return ToTarget(matches ? WhenMatch : WhenDifferent, targetType);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string ToText(object value)
        {
            return value == null ? "" : (value as string ?? value.ToString() ?? "");
        }

        private static object ToTarget(string text, Type targetType)
        {
            var value = text ?? "";

            if (targetType == null || targetType == typeof(object) || targetType == typeof(string))
                return value;

            if (targetType == typeof(bool))
                return bool.TryParse(value, out var flag) && flag;

            if (targetType.IsEnum)
            {
                try
                {
                    return Enum.Parse(targetType, value, ignoreCase: true);
                }
                catch (ArgumentException)
                {
                    return DependencyProperty.UnsetValue;
                }
            }

            var converter = TypeDescriptor.GetConverter(targetType);
            return converter.CanConvertFrom(typeof(string))
                ? converter.ConvertFromInvariantString(value)
                : (object)value;
        }
    }
}
