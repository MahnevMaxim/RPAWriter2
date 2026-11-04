using System;
using System.Globalization;
using System.Windows.Data;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Combines several independent enable/visibility conditions into one <c>IsEnabled</c> value:
    /// returns <c>true</c> only when every input is truthy, so a <see cref="System.Windows.Data.MultiBinding"/>
    /// can express "has an active conversation AND the agent is not busy".
    /// <para>
    /// Accepts both <see cref="bool"/> and its textual form (<c>"True"</c>/<c>"False"</c>): a nested
    /// binding converter inside a <see cref="System.Windows.Data.MultiBinding"/> is called with a
    /// <c>null</c> target type, so <see cref="EnumMatchConverter"/> may produce a string there.
    /// </para>
    /// </summary>
    public sealed class AllTrueMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
                return false;

            foreach (var value in values)
            {
                if (!IsTrue(value))
                    return false;
            }

            return true;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool IsTrue(object value)
        {
            if (value is bool flag)
                return flag;

            if (value is string text)
                return bool.TryParse(text, out var parsed) && parsed;

            return false;
        }
    }
}
