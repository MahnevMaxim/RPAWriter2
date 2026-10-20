using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RPAWriter2.Markdown;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Converts a Markdown string into a rendered <see cref="FrameworkElement"/>
    /// using <see cref="MarkdownRenderer"/>, for binding in the chat UI.
    /// </summary>
    public class MarkdownToElementConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return MarkdownRenderer.Render(value as string ?? string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
