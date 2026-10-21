using System;
using System.Globalization;
using System.Windows.Data;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Returns true when an answer option equals the selected answer of a question.
    /// Used by the chat question control to highlight the chosen answer button.
    /// </summary>
    public class OptionEqualsAnswerConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            var option = values[0] as string;
            var answer = values[1] as string;

            return !string.IsNullOrEmpty(answer)
                && string.Equals(option, answer, StringComparison.Ordinal);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
