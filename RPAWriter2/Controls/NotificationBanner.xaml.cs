using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Reusable notification banner matching the Controls -> Notifications template:
    /// transparent background, a 1px accent border and accent-colored text. The
    /// <see cref="Kind"/> selects the accent color and which action buttons are shown;
    /// Warning/Error additionally expose Copy and Send-to-chat, every level exposes Close.
    /// </summary>
    public partial class NotificationBanner : UserControl
    {
        private static readonly Brush TraceBrush = Freeze(Brushes.White);
        private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0xB5, 0xF6)));
        private static readonly Brush SuccessBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));
        private static readonly Brush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)));
        private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)));

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(NotificationKind), typeof(NotificationBanner),
            new PropertyMetadata(NotificationKind.Info, OnKindChanged));

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(NotificationBanner),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(NotificationBanner),
            new PropertyMetadata(InfoBrush));

        public static readonly DependencyProperty ShowCopyProperty = DependencyProperty.Register(
            nameof(ShowCopy), typeof(bool), typeof(NotificationBanner), new PropertyMetadata(false));

        public static readonly DependencyProperty ShowSendProperty = DependencyProperty.Register(
            nameof(ShowSend), typeof(bool), typeof(NotificationBanner), new PropertyMetadata(false));

        public static readonly DependencyProperty ShowCloseProperty = DependencyProperty.Register(
            nameof(ShowClose), typeof(bool), typeof(NotificationBanner), new PropertyMetadata(true));

        public NotificationBanner()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user clicks the close button.</summary>
        public event RoutedEventHandler? CloseRequested;

        /// <summary>Raised when the user clicks the send-to-chat button.</summary>
        public event RoutedEventHandler? SendToChatRequested;

        public NotificationKind Kind
        {
            get => (NotificationKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        /// <summary>Accent color used for the border and text; derived from <see cref="Kind"/>.</summary>
        public Brush AccentBrush
        {
            get => (Brush)GetValue(AccentBrushProperty);
            set => SetValue(AccentBrushProperty, value);
        }

        public bool ShowCopy
        {
            get => (bool)GetValue(ShowCopyProperty);
            set => SetValue(ShowCopyProperty, value);
        }

        public bool ShowSend
        {
            get => (bool)GetValue(ShowSendProperty);
            set => SetValue(ShowSendProperty, value);
        }

        public bool ShowClose
        {
            get => (bool)GetValue(ShowCloseProperty);
            set => SetValue(ShowCloseProperty, value);
        }

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((NotificationBanner)d).ApplyKind((NotificationKind)e.NewValue);
        }

        private void ApplyKind(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.Trace:
                    AccentBrush = TraceBrush;
                    ShowCopy = false;
                    ShowSend = false;
                    ShowClose = true;
                    break;
                case NotificationKind.Info:
                    AccentBrush = InfoBrush;
                    ShowCopy = false;
                    ShowSend = false;
                    ShowClose = true;
                    break;
                case NotificationKind.Success:
                    AccentBrush = SuccessBrush;
                    ShowCopy = false;
                    ShowSend = false;
                    ShowClose = true;
                    break;
                case NotificationKind.Warning:
                    AccentBrush = WarningBrush;
                    ShowCopy = true;
                    ShowSend = true;
                    ShowClose = true;
                    break;
                case NotificationKind.Error:
                    AccentBrush = ErrorBrush;
                    ShowCopy = true;
                    ShowSend = true;
                    ShowClose = true;
                    break;
                default:
                    AccentBrush = InfoBrush;
                    ShowCopy = false;
                    ShowSend = false;
                    ShowClose = true;
                    break;
            }
        }

        private static Brush Freeze(Brush brush)
        {
            if (brush.CanFreeze)
                brush.Freeze();
            return brush;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            var text = Message;
            if (string.IsNullOrEmpty(text))
                return;
            try { Clipboard.SetText(text); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            SendToChatRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }
    }
}
