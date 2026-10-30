using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace RPAWriter2.Markdown
{
    /// <summary>
    /// Attaches a small "copy this block" context menu to the visuals produced by
    /// <see cref="MarkdownRenderer"/>. WPF's <see cref="TextBlock"/> has no text selection, so a
    /// per-block right-click menu is the cheapest way to let the user copy rendered Markdown piece
    /// by piece. Nothing about the rendered markdown changes: the menu is only assigned to
    /// <see cref="FrameworkElement.ContextMenu"/>, it never takes part in layout or painting until
    /// it is actually opened, and the brushes/fonts/margins of the blocks stay untouched.
    /// </summary>
    internal static class MarkdownBlockContextMenu
    {
        private const string CopyHeader = "Copy";
        private const string CopyCodeHeader = "Copy code block";

        /// <summary>Raw source to copy instead of the rendered text (fenced/indented code blocks).</summary>
        private static readonly DependencyProperty RawTextProperty = DependencyProperty.RegisterAttached(
            "RawText",
            typeof(string),
            typeof(MarkdownBlockContextMenu),
            new PropertyMetadata(null));

        private static readonly SolidColorBrush MenuBackground = Freeze("#252526");
        private static readonly SolidColorBrush MenuBorder = Freeze("#3F3F46");
        private static readonly SolidColorBrush MenuForeground = Freeze("#F1F1F1");

        // Two fully built menus shared by every rendered block, one per caption. They are created
        // once and never mutated while they are open - see CreateMenu for why that matters.
        private static ContextMenu? _textMenu;
        private static ContextMenu? _codeMenu;

        /// <summary>
        /// Makes <paramref name="textBlock"/> offer a "Copy" entry in its context menu.
        /// </summary>
        /// <param name="textBlock">Block rendered by <see cref="MarkdownRenderer"/>.</param>
        /// <param name="rawText">
        /// Optional raw text to copy instead of the rendered text (used for code blocks so that
        /// indentation and line endings survive).
        /// </param>
        public static void Attach(TextBlock? textBlock, string? rawText = null)
        {
            if (textBlock == null)
                return;

            if (!string.IsNullOrEmpty(rawText))
                textBlock.SetValue(RawTextProperty, rawText);

            ContextMenu menu;
            if (string.IsNullOrEmpty(rawText))
            {
                if (_textMenu == null)
                    _textMenu = CreateMenu(CopyHeader);

                menu = _textMenu;
            }
            else
            {
                if (_codeMenu == null)
                    _codeMenu = CreateMenu(CopyCodeHeader);

                menu = _codeMenu;
            }

            if (!ReferenceEquals(textBlock.ContextMenu, menu))
            {
                textBlock.ContextMenu = menu;
                textBlock.ContextMenuOpening += OnContextMenuOpening;
            }
        }

        /// <summary>
        /// Creates one menu with a final caption, a final template and a single entry.
        /// </summary>
        /// <remarks>
        /// The caption must be final before the menu opens. Replacing it from
        /// <c>ContextMenu.Opened</c> re-measures the popup after it has already been shown, and the
        /// popup window is then painted over the freshly rendered content - that was the light
        /// rounded strip covering the menu until the mouse forced another paint pass.
        /// The stock template is replaced as well: it paints its own light surface with rounded
        /// corners, so a purely dark menu is impossible without it.
        /// </remarks>
        private static ContextMenu CreateMenu(string header)
        {
            var menu = new ContextMenu
            {
                Background = MenuBackground,
                BorderBrush = MenuBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 3, 0, 3),
                Foreground = MenuForeground,
                Template = CreateMenuTemplate(),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            var item = new MenuItem
            {
                Header = header,
                Style = CreateMenuItemStyle()
            };
            item.Click += (sender, args) => CopyToClipboard(ResolveText(menu.PlacementTarget as TextBlock));

            menu.Items.Add(item);
            return menu;
        }

        /// <summary>
        /// Disables the entry for blocks that carry no text at all. This event is raised before the
        /// popup is measured, so it cannot disturb the first paint pass of the menu.
        /// </summary>
        private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var textBlock = sender as TextBlock;
            if (textBlock == null)
                return;

            var menu = textBlock.ContextMenu as ContextMenu;
            if (menu == null || menu.Items.Count == 0)
                return;

            var item = menu.Items[0] as MenuItem;
            if (item != null)
                item.IsEnabled = !string.IsNullOrEmpty(ResolveText(textBlock));
        }

        private static string? ResolveText(TextBlock? textBlock)
        {
            if (textBlock == null)
                return null;

            if (GetRawText(textBlock) is string raw)
                return raw;

            return ExtractText(textBlock);
        }

        /// <summary>
        /// Returns the plain text of a rendered block. <see cref="TextBlock.Text"/> is empty for
        /// blocks built from <see cref="TextBlock.Inlines"/>, so the text is read through a
        /// <see cref="TextRange"/> over the block content instead.
        /// </summary>
        private static string ExtractText(TextBlock textBlock)
        {
            try
            {
                var range = new TextRange(textBlock.ContentStart, textBlock.ContentEnd);
                return range.Text.TrimEnd('\r', '\n');
            }
            catch (Exception)
            {
                // Not a valid text container in some rare states; fall back to the simple text.
                return textBlock.Text ?? string.Empty;
            }
        }

        private static void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                // Normalize line endings: the raw code text comes from Markdig with LF only,
                // while paragraphs come out of TextRange with CRLF.
                Clipboard.SetText(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"));
            }
            catch (Exception)
            {
                // The clipboard can be locked by another process; never crash the chat over it.
            }
        }

        private static string? GetRawText(DependencyObject? element)
        {
            return element == null ? null : element.GetValue(RawTextProperty) as string;
        }

        /// <summary>
        /// Dark popup chrome. Without it the default WPF template paints a light surface with
        /// rounded corners, which is exactly what showed up in front of the menu.
        /// </summary>
        private static ControlTemplate CreateMenuTemplate()
        {
            const string xaml =
                "<ControlTemplate TargetType='ContextMenu'" +
                " xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Border Background='#252526' BorderBrush='#3F3F46' BorderThickness='1'" +
                " CornerRadius='2' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'" +
                " UseLayoutRounding='True'>" +
                "<StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>" +
                "</Border>" +
                "</ControlTemplate>";

            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        /// <summary>
        /// Menu item look that matches the dark chat palette.
        /// </summary>
        private static Style CreateMenuItemStyle()
        {
            var template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate TargetType='MenuItem'" +
                " xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Border x:Name='Bd' Background='Transparent' Padding='10,5' SnapsToDevicePixels='True'>" +
                "<ContentPresenter ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsHighlighted' Value='True'>" +
                "<Setter TargetName='Bd' Property='Background' Value='#3F3F46'/>" +
                "</Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'>" +
                "<Setter Property='Foreground' Value='#9A9A9A'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>");

            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, MenuForeground));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Arrow));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 120.0));
            return style;
        }

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
