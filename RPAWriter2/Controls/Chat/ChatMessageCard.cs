using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Shared chrome for every chat message: an accent border, a header with a collapse
    /// chevron, the timestamp and the copy button, and a content area for the per-kind body.
    /// The chrome lives in a <see cref="ControlTemplate"/> (see ChatResources.xaml), so each
    /// per-kind control supplies its body as this control's <see cref="ContentControl.Content"/>.
    /// Emits bubbling routed events for copy/question actions.
    /// </summary>
    public class ChatMessageCard : ContentControl
    {
        static ChatMessageCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ChatMessageCard), new FrameworkPropertyMetadata(typeof(ChatMessageCard)));
        }

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(
                nameof(IsExpanded),
                typeof(bool),
                typeof(ChatMessageCard),
                new PropertyMetadata(true));

        public static readonly RoutedEvent CopyRequestedEvent =
            EventManager.RegisterRoutedEvent("CopyRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ChatMessageCard));

        public static readonly RoutedEvent QuestionAnsweredEvent =
            EventManager.RegisterRoutedEvent("QuestionAnswered", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ChatMessageCard));

        public static readonly RoutedEvent QuestionSkippedEvent =
            EventManager.RegisterRoutedEvent("QuestionSkipped", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ChatMessageCard));

        /// <summary>Whether the message body is visible. Defaults to true (expanded).</summary>
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // Event handlers cannot live in the shared ResourceDictionary (it has no
            // code-behind), so wire the template buttons up here.
            if (GetTemplateChild("ToggleButton") is Button toggle)
                toggle.Click += OnToggleExpanded;
            if (GetTemplateChild("CopyButton") is Button copy)
                copy.Click += OnCopy;
        }

        private void OnToggleExpanded(object sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(CopyRequestedEvent, DataContext as ChatMessage));
        }
    }
}
