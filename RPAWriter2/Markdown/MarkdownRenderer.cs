using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using RPAWriter.Core.Services;
using RPAWriter2.Converters;

// WPF's System.Windows.Documents namespace also defines Block/Inline/Table/TableRow/TableCell,
// which collide with Markdig's block model. Aliases let us use the Markdig types unqualified.
using Block = Markdig.Syntax.Block;
using Inline = Markdig.Syntax.Inlines.Inline;
using Table = Markdig.Extensions.Tables.Table;
using TableRow = Markdig.Extensions.Tables.TableRow;
using TableCell = Markdig.Extensions.Tables.TableCell;

namespace RPAWriter2.Markdown
{
    /// <summary>
    /// Renders a Markdown string into a WPF visual tree for the dark-themed chat UI.
    /// Uses Markdig for GFM parsing (tables, task lists, strikethrough, autolinks) and
    /// reuses <see cref="CSharpHighlighter"/> for C# syntax highlighting inside fenced code blocks.
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UseListExtras()
            .Build();

        private static readonly HashSet<string> CSharpLanguages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "csharp", "cs", "c#" };

        private static readonly FontFamily CodeFont = new FontFamily("Consolas");

        private static readonly SolidColorBrush TextBrush = Freeze("#F1F1F1");
        private static readonly SolidColorBrush MutedBrush = Freeze("#9A9A9A");
        private static readonly SolidColorBrush LinkBrush = Freeze("#569CD6");
        private static readonly SolidColorBrush CodeBackground = Freeze("#1E1E1E");
        private static readonly SolidColorBrush CodeInlineBackground = Freeze("#333337");
        private static readonly SolidColorBrush CodeInlineForeground = Freeze("#DCDCDC");
        private static readonly SolidColorBrush BorderBrush = Freeze("#3F3F46");
        private static readonly SolidColorBrush QuoteBorderBrush = Freeze("#4A6B9A");
        private static readonly SolidColorBrush QuoteBackground = Freeze("#1E2733");
        private static readonly SolidColorBrush TableHeaderBackground = Freeze("#333337");
        private static readonly SolidColorBrush TableEvenBackground = Freeze("#2A2A2E");
        private static readonly SolidColorBrush TableOddBackground = Freeze("#252526");
        private static readonly SolidColorBrush CheckedBrush = Freeze("#4CAF50");

        /// <summary>
        /// Optional handler invoked when the user clicks a relative (non-http/https/mailto)
        /// markdown link. Set it before calling <see cref="Render"/>; the rendered hyperlinks
        /// capture the delegate, so it can be cleared immediately afterwards. Used by the
        /// built-in documentation viewer to navigate between shipped .md files.
        /// </summary>
        public static Func<string, bool>? RelativeLinkNavigator { get; set; }

        /// <summary>Renders Markdown into a <see cref="StackPanel"/> of block elements.</summary>
        public static FrameworkElement Render(string markdown)
        {
            var panel = new StackPanel();
            var document = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
            foreach (var block in document)
            {
                var element = RenderBlock(block, 0);
                if (element != null)
                    panel.Children.Add(element);
            }
            return panel;
        }

        private static FrameworkElement RenderBlock(Block block, int depth)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return RenderHeading(heading);

                case ParagraphBlock paragraph:
                    return CreateTextBlock(paragraph.Inline, TextWrapping.Wrap);

                case FencedCodeBlock fenced:
                    return RenderCodeBlock(fenced, fenced.Info);

                case CodeBlock code:
                    return RenderCodeBlock(code, string.Empty);

                case QuoteBlock quote:
                    return RenderQuote(quote, depth);

                case ListBlock list:
                    return RenderList(list, depth);

                case ThematicBreakBlock _:
                    return new Border
                    {
                        Height = 1,
                        Background = BorderBrush,
                        Margin = new Thickness(0, 8, 0, 8)
                    };

                case Table table:
                    return RenderTable(table);

                case HtmlBlock _:
                    // Raw HTML is intentionally not interpreted inside the chat; drop it.
                    return null;

                case LinkReferenceDefinitionGroup _:
                    return null;

                case LeafBlock leaf when leaf.Inline != null:
                    return CreateTextBlock(leaf.Inline, TextWrapping.Wrap);

                default:
                    return null;
            }
        }

        private static FrameworkElement RenderHeading(HeadingBlock heading)
        {
            return CreateTextBlock(
                heading.Inline,
                TextWrapping.Wrap,
                fontWeight: FontWeights.Bold,
                margin: new Thickness(0, heading.Level == 1 ? 8 : 10, 0, 4));
        }

        private static FrameworkElement RenderCodeBlock(CodeBlock code, string info)
        {
            string language = FirstWord(info);
            string text = MarkdownCodeBlockText.GetText(code);
            text = text.TrimEnd('\r', '\n');

            IReadOnlyList<CodeToken> tokens = IsCSharp(language)
                ? CSharpHighlighter.Highlight(text)
                : new[] { new CodeToken(CodeTokenKind.Plain, text) };

            var converter = new CodeTokensToTextBlockConverter();
            var textBlock = converter.Convert(tokens, typeof(TextBlock), null, CultureInfo.InvariantCulture) as TextBlock
                            ?? new TextBlock { Text = text };

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = textBlock
            };

            return new Border
            {
                Background = CodeBackground,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 4, 0, 4),
                Child = scroll
            };
        }

        private static FrameworkElement RenderQuote(QuoteBlock quote, int depth)
        {
            var content = new StackPanel();
            foreach (var child in quote)
            {
                var element = RenderBlock(child, depth + 1);
                if (element != null)
                    content.Children.Add(element);
            }

            return new Border
            {
                BorderBrush = QuoteBorderBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Background = QuoteBackground,
                Padding = new Thickness(8, 4, 4, 4),
                Margin = new Thickness(depth * 12, 4, 0, 4),
                Child = content
            };
        }

        private static FrameworkElement RenderList(ListBlock list, int depth)
        {
            var panel = new StackPanel { Margin = new Thickness(depth * 16, 2, 0, 2) };
            int number = ParseOrderedStart(list);

            foreach (var child in list)
            {
                if (!(child is ListItemBlock item))
                    continue;

                panel.Children.Add(RenderListItem(list, item, number, depth));
                if (list.IsOrdered)
                    number++;
            }

            return panel;
        }

        private static FrameworkElement RenderListItem(ListBlock list, ListItemBlock item, int number, int depth)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TaskList task = FindTaskList(item);

            string marker;
            if (task != null)
            {
                marker = task.Checked ? "☑" : "☐";
            }
            else if (list.IsOrdered)
            {
                marker = number + (list.OrderedDelimiter == ')' ? ")" : ".");
            }
            else
            {
                marker = "•";
            }

            var markerText = new TextBlock
            {
                Text = marker,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold,
                Foreground = task != null ? (task.Checked ? CheckedBrush : MutedBrush) : TextBrush
            };
            Grid.SetColumn(markerText, 0);
            grid.Children.Add(markerText);

            var content = new StackPanel();
            foreach (var child in item)
            {
                var element = RenderBlock(child, depth + 1);
                if (element != null)
                    content.Children.Add(element);
            }

            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            return grid;
        }

        private static FrameworkElement RenderTable(Table table)
        {
            var rows = new List<TableRow>();
            foreach (var child in table)
            {
                if (child is TableRow row)
                    rows.Add(row);
            }

            if (rows.Count == 0)
                return null;

            int columnCount = 0;
            foreach (var row in rows)
                columnCount = Math.Max(columnCount, CountCells(row));

            if (columnCount == 0)
                return null;

            var grid = new Grid();
            for (int i = 0; i < columnCount; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cells = new List<TableCell>();
                foreach (var child in row)
                {
                    if (child is TableCell cell)
                        cells.Add(cell);
                }

                for (int c = 0; c < columnCount; c++)
                {
                    var cell = c < cells.Count ? cells[c] : null;
                    bool lastColumn = c == columnCount - 1;
                    bool lastRow = r == rows.Count - 1;

                    var border = new Border
                    {
                        Background = row.IsHeader
                            ? TableHeaderBackground
                            : (r % 2 == 0 ? TableEvenBackground : TableOddBackground),
                        BorderBrush = BorderBrush,
                        BorderThickness = new Thickness(0, 0, lastColumn ? 0 : 1, lastRow ? 0 : 1),
                        Padding = new Thickness(6, 3, 6, 3)
                    };

                    if (cell != null)
                    {
                        var content = new StackPanel();
                        foreach (var child in cell)
                        {
                            var element = RenderBlock(child, 0);
                            if (element != null)
                                content.Children.Add(element);
                        }
                        border.Child = content;
                    }

                    Grid.SetRow(border, r);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }
            }

            return new Border
            {
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4),
                Child = grid
            };
        }

        private static TextBlock CreateTextBlock(
            ContainerInline inline,
            TextWrapping wrapping,
            FontWeight? fontWeight = null,
            Thickness? margin = null)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = wrapping,
                Foreground = TextBrush
            };

            if (fontWeight.HasValue)
                textBlock.FontWeight = fontWeight.Value;

            if (margin.HasValue)
                textBlock.Margin = margin.Value;

            AppendContainer(inline, textBlock.Inlines);
            return textBlock;
        }

        private static void AppendContainer(ContainerInline inline, InlineCollection target)
        {
            if (inline == null)
                return;

            foreach (var child in inline)
                AppendInline(child, target);
        }

        private static void AppendInline(Inline inline, InlineCollection target)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;

                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = CodeFont,
                        Background = CodeInlineBackground,
                        Foreground = CodeInlineForeground
                    });
                    break;

                case LineBreakInline breakInline:
                    if (breakInline.IsHard)
                        target.Add(new LineBreak());
                    else
                        target.Add(new Run(" "));
                    break;

                case EmphasisInline emphasis:
                    target.Add(BuildEmphasis(emphasis));
                    break;

                case LinkInline link:
                    AppendLink(link, target);
                    break;

                case AutolinkInline autolink:
                    AppendAutolink(autolink, target);
                    break;

                case HtmlInline html:
                    target.Add(new Run(html.Tag) { Foreground = MutedBrush });
                    break;

                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()));
                    break;

                case TaskList _:
                    // The checkbox glyph is rendered by the list marker; skip the inline here.
                    break;

                case ContainerInline container:
                    var span = new Span();
                    foreach (var child in container)
                        AppendInline(child, span.Inlines);
                    target.Add(span);
                    break;

                default:
                    target.Add(new Run(inline.ToString()));
                    break;
            }
        }

        private static Span BuildEmphasis(EmphasisInline emphasis)
        {
            Span span;
            if (emphasis.DelimiterChar == '~')
                span = new Span { TextDecorations = TextDecorations.Strikethrough };
            else if (emphasis.DelimiterCount >= 2)
                span = new Bold();
            else
                span = new Italic();

            foreach (var child in emphasis)
                AppendInline(child, span.Inlines);

            return span;
        }

        private static void AppendLink(LinkInline link, InlineCollection target)
        {
            if (link.IsImage)
            {
                target.Add(new Run(link.Label ?? "image") { FontStyle = FontStyles.Italic, Foreground = MutedBrush });
                return;
            }

            var url = link.Url ?? string.Empty;
            if (!TryCreateNavigableUri(url, out var uri))
            {
                var navigator = RelativeLinkNavigator;
                if (!string.IsNullOrWhiteSpace(url) && navigator != null)
                {
                    Uri? relativeUri;
                    try
                    {
                        relativeUri = new Uri(url, UriKind.RelativeOrAbsolute);
                    }
                    catch
                    {
                        relativeUri = null;
                    }

                    if (relativeUri != null)
                    {
                        var relative = new Hyperlink
                        {
                            NavigateUri = relativeUri,
                            Foreground = LinkBrush,
                            TextDecorations = TextDecorations.Underline,
                            ToolTip = string.IsNullOrWhiteSpace(link.Title) ? url : link.Title
                        };
                        foreach (var child in link)
                            AppendInline(child, relative.Inlines);
                        relative.RequestNavigate += (s, e) =>
                        {
                            e.Handled = true;
                            navigator(url);
                        };
                        target.Add(relative);
                        return;
                    }
                }

                var span = new Span();
                foreach (var child in link)
                    AppendInline(child, span.Inlines);
                target.Add(span);
                return;
            }

            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline,
                ToolTip = string.IsNullOrWhiteSpace(link.Title) ? url : link.Title
            };
            hyperlink.RequestNavigate += OnRequestNavigate;

            foreach (var child in link)
                AppendInline(child, hyperlink.Inlines);

            target.Add(hyperlink);
        }

        private static void AppendAutolink(AutolinkInline autolink, InlineCollection target)
        {
            string href = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
            if (!TryCreateNavigableUri(href, out var uri))
            {
                target.Add(new Run(autolink.Url));
                return;
            }

            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline
            };
            hyperlink.Inlines.Add(new Run(autolink.Url));
            hyperlink.RequestNavigate += OnRequestNavigate;
            target.Add(hyperlink);
        }

        private static bool TryCreateNavigableUri(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!Uri.TryCreate(value, UriKind.Absolute, out var absolute))
                return false;

            if (absolute.Scheme != Uri.UriSchemeHttp &&
                absolute.Scheme != Uri.UriSchemeHttps &&
                absolute.Scheme != Uri.UriSchemeMailto)
                return false;

            uri = absolute;
            return true;
        }

        private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            if (e.Uri == null)
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Best-effort only; never crash the chat window because a link failed to open.
            }
        }

        private static TaskList FindTaskList(ListItemBlock item)
        {
            foreach (var child in item)
            {
                if (!(child is ParagraphBlock paragraph) || paragraph.Inline == null)
                    continue;

                foreach (var inline in paragraph.Inline)
                {
                    if (inline is TaskList taskList)
                        return taskList;

                    // The checkbox must be the first meaningful content; stop at real text.
                    if (inline is LiteralInline literal && !string.IsNullOrWhiteSpace(literal.Content.ToString()))
                        return null;
                }
            }

            return null;
        }

        private static int ParseOrderedStart(ListBlock list)
        {
            if (!list.IsOrdered)
                return 0;

            if (int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return 1;
        }

        private static int CountCells(TableRow row)
        {
            int count = 0;
            foreach (var child in row)
            {
                if (child is TableCell)
                    count++;
            }
            return count;
        }

        private static string FirstWord(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            int space = trimmed.IndexOfAny(new[] { ' ', '\t' });
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        private static bool IsCSharp(string language)
        {
            return CSharpLanguages.Contains(language);
        }

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
