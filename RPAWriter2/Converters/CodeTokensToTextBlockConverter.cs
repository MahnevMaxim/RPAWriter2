using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using RPAWriter.Core.Services;

namespace RPAWriter2.Converters
{
    /// <summary>
    /// Converts a token list produced by <see cref="CSharpHighlighter"/> into a colored,
    /// monospaced <see cref="TextBlock"/> (no wrapping; the code block provides horizontal scrolling).
    /// </summary>
    public class CodeTokensToTextBlockConverter : IValueConverter
    {
        private static readonly SolidColorBrush KeywordBrush = Freeze("#569CD6");
        private static readonly SolidColorBrush StringBrush = Freeze("#D69D85");
        private static readonly SolidColorBrush CommentBrush = Freeze("#6A9955");
        private static readonly SolidColorBrush NumberBrush = Freeze("#B5CEA8");
        private static readonly SolidColorBrush PlainBrush = Freeze("#DCDCDC");

        private static readonly FontFamily CodeFont = new FontFamily("Consolas");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = CodeFont
            };

            if (value is IReadOnlyList<CodeToken> tokens)
            {
                foreach (var token in tokens)
                    AppendToken(textBlock, token);
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static void AppendToken(TextBlock block, CodeToken token)
        {
            Brush brush = BrushFor(token.Kind);
            string text = token.Text;

            // Preserve line structure: split on line endings and insert LineBreak elements.
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\n' && c != '\r')
                    continue;

                FlushRun(block, text, start, i, brush);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                block.Inlines.Add(new LineBreak());
                start = i + 1;
            }
            FlushRun(block, text, start, text.Length, brush);
        }

        private static void FlushRun(TextBlock block, string text, int start, int end, Brush brush)
        {
            if (end <= start)
                return;
            block.Inlines.Add(new Run(text.Substring(start, end - start)) { Foreground = brush });
        }

        private static Brush BrushFor(CodeTokenKind kind)
        {
            switch (kind)
            {
                case CodeTokenKind.Keyword: return KeywordBrush;
                case CodeTokenKind.String: return StringBrush;
                case CodeTokenKind.Comment: return CommentBrush;
                case CodeTokenKind.Number: return NumberBrush;
                default: return PlainBrush;
            }
        }

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
