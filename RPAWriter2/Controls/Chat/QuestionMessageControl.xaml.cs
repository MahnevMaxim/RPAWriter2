using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls.Chat
{
    public partial class QuestionMessageControl : UserControl
    {
        public QuestionMessageControl()
        {
            InitializeComponent();
        }

        private void OnQuestionOptionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || string.IsNullOrEmpty(button.Tag as string))
                return;

            var option = (string)button.Tag;
            if (DataContext is ChatMessage message && message.Payload is QuestionPayload q)
                q.Answer = option;

            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.QuestionAnsweredEvent, DataContext as ChatMessage));
        }

        private void OnQuestionSkip(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChatMessage message && message.Payload is QuestionPayload q)
            {
                q.Answer = ChatMessagesControl.SkipAnswerText;
                q.IsSkipped = true;
            }

            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.QuestionSkippedEvent, DataContext as ChatMessage));
        }
    }
}
