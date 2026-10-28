using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Chat card for a <see cref="RPAWriter.Core.Models.ChatMessageKind.Checkpoint"/> indicator:
    /// shows the snapshot hash/time and surfaces Compare/Restore as routed events to the host.
    /// </summary>
    public partial class CheckpointMessageControl : UserControl
    {
        public CheckpointMessageControl()
        {
            InitializeComponent();
        }

        private void OnCompare(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.CheckpointCompareRequestedEvent, DataContext as ChatMessage));
        }

        private void OnRestore(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.CheckpointRestoreRequestedEvent, DataContext as ChatMessage));
        }
    }
}
