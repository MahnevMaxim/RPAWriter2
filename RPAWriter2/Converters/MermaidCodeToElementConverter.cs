using System;
using System.Globalization;
using System.Windows.Data;
using RPAWriter2.Controls;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Converts a Mermaid source string into a <see cref="MermaidDiagramControl"/>,
    /// for rendering mermaid fenced code blocks in the chat message list.
    /// </summary>
    public class MermaidCodeToElementConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return new MermaidDiagramControl(value as string ?? string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
