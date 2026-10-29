using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Returns the source collection without its first element.
    /// Used to render log parameters where the first pair is already shown as the collapsed header.
    /// </summary>
    public sealed class SkipFirstConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is IEnumerable enumerable))
                return value;

            return enumerable.Cast<object>().Skip(1).ToList();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
