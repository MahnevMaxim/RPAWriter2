using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RPAWriter2;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Reusable chat message list: renders a collection of <see cref="ChatMessage"/> entries
    /// (user/agent text, tool calls, questions, sub-agents, code blocks) with auto-scroll,
    /// per-message copy and question answering. The host drives it through <see cref="Messages"/>
    /// and the <see cref="CopyMessageRequested"/>/<see cref="QuestionAnswered"/>/<see cref="QuestionSkipped"/>
    /// events, so the control stays free of agent/domain logic.
    /// </summary>
    public partial class ChatMessagesControl : UserControl
    {
        /// <summary>Answer text stored when the user explicitly skips a question.</summary>
        public const string SkipAnswerText = "User skipped the question.";

        private const double AutoScrollThreshold = 1.0;

        private ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private ICollectionView? _view;
        private bool _autoScroll = true;

        public ChatMessagesControl()
        {
            InitializeComponent();
            AttachView(_messages);
        }

        /// <summary>
        /// Dependency property controlling whether each message shows its timestamp.
        /// Defaults to true, so timestamps are shown unless explicitly disabled in XAML.
        /// </summary>
        public static readonly DependencyProperty ShowTimestampProperty =
            DependencyProperty.Register(
                nameof(ShowTimestamp),
                typeof(bool),
                typeof(ChatMessagesControl),
                new PropertyMetadata(true));

        /// <summary>Gets or sets whether each message shows its timestamp.</summary>
        public bool ShowTimestamp
        {
            get => (bool)GetValue(ShowTimestampProperty);
            set => SetValue(ShowTimestampProperty, value);
        }

        /// <summary>The message collection rendered by this control. Assign an existing collection to share it.</summary>
        public ObservableCollection<ChatMessage> Messages
        {
            get => _messages;
            set
            {
                if (ReferenceEquals(_messages, value)) return;
                AttachView(value ?? new ObservableCollection<ChatMessage>());
            }
        }

        /// <summary>Optional predicate used to hide messages from the list (e.g. the pinned header message).</summary>
        public Func<ChatMessage, bool>? Filter { get; set; }

        /// <summary>Raised when the user clicks the copy button of a message; the host formats and copies it.</summary>
        public event EventHandler<ChatMessage>? CopyMessageRequested;

        /// <summary>Raised after the user picked a question answer; <see cref="QuestionPayload.Answer"/> is already set.</summary>
        public event EventHandler<ChatMessage>? QuestionAnswered;

        /// <summary>Raised after the user skipped a question; <see cref="QuestionPayload.Answer"/>/IsSkipped are already set.</summary>
        public event EventHandler<ChatMessage>? QuestionSkipped;

        /// <summary>Appends a message and, when auto-scroll is enabled, scrolls to the end.</summary>
        public void Append(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Append(message)));
                return;
            }
            _messages.Add(message);
            if (_autoScroll)
                ChatScroll.ScrollToEnd();
        }

        /// <summary>Removes a message from the list.</summary>
        public void Remove(ChatMessage message) => _messages.Remove(message);

        /// <summary>Clears the list.</summary>
        public void Clear() => _messages.Clear();

        /// <summary>Scrolls the list to the bottom and re-arms auto-scroll.</summary>
        public void ScrollToEnd()
        {
            _autoScroll = true;
            ChatScroll.ScrollToEnd();
        }

        /// <summary>Re-applies <see cref="Filter"/> to the visible list.</summary>
        public void RefreshFilter() => _view?.Refresh();

        private void AttachView(ObservableCollection<ChatMessage> messages)
        {
            _messages = messages;
            _view = CollectionViewSource.GetDefaultView(_messages);
            _view.Filter = FilterPredicate;
            ChatList.ItemsSource = _view;
        }

        private bool FilterPredicate(object item)
        {
            if (Filter == null) return true;
            return item is ChatMessage message && Filter(message);
        }

        private void OnChatScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            bool atBottom = ChatScroll.VerticalOffset >= ChatScroll.ScrollableHeight - AutoScrollThreshold;
            if (atBottom)
            {
                _autoScroll = true;
            }
            else if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            {
                _autoScroll = false;
            }
        }

        /// <summary>
        /// Forwards the mouse wheel from the code block's horizontal-only ScrollViewer to its parent,
        /// so hovering over a code block still scrolls the message list vertically.
        /// </summary>
        private void OnCodeScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
                return;

            e.Handled = true;

            var parent = (sender as FrameworkElement)?.Parent as UIElement;
            if (parent == null)
                return;

            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = parent
            });
        }

        private void OnCopyMessage(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ChatMessage message })
                CopyMessageRequested?.Invoke(this, message);
        }

        private void OnQuestionOptionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ChatMessage message, Tag: string option } && !string.IsNullOrEmpty(option))
            {
                if (message.Payload is QuestionPayload q)
                    q.Answer = option;
                QuestionAnswered?.Invoke(this, message);
            }
        }

        private void OnQuestionSkip(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ChatMessage message })
            {
                if (message.Payload is QuestionPayload q)
                {
                    q.Answer = SkipAnswerText;
                    q.IsSkipped = true;
                }
                QuestionSkipped?.Invoke(this, message);
            }
        }
    }
}
