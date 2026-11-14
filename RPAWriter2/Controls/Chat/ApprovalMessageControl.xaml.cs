using System.Windows;
using System.Windows.Controls;
using RPAWriter2.Services;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls.Chat
{
    public partial class ApprovalMessageControl : UserControl
    {
        public ApprovalMessageControl()
        {
            InitializeComponent();
        }

        private void OnApproveClick(object sender, RoutedEventArgs e) => Decide("approved", false);

        private void OnRejectClick(object sender, RoutedEventArgs e) => Decide("rejected", false);

        private void OnApproveAllClick(object sender, RoutedEventArgs e) => Decide("approved", true);

        private void OnRejectAllClick(object sender, RoutedEventArgs e) => Decide("rejected", true);

        private void Decide(string decision, bool applyToAll)
        {
            if (DataContext is not ChatMessage message || message.Payload is not ApprovalPayload payload)
                return;

            payload.ApplyToAll = applyToAll;
            payload.Decision = decision;

            LogService.ChatDebug("Approval decision: \"" + decision + "\" (applyToAll=" + applyToAll + ").");
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.ApprovalDecidedEvent, message));
        }
    }
}
