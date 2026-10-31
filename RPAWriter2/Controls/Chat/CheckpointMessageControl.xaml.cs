using System.Windows;
using System.Windows.Controls;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Chat card for a <see cref="RPAWriter.Core.Models.ChatMessageKind.Checkpoint"/> indicator:
    /// a transparent single-line chip with an optional per-iteration diff button and a Restore action.
    /// </summary>
    public partial class CheckpointMessageControl : UserControl
    {
        public CheckpointMessageControl()
        {
            InitializeComponent();
        }

        private void OnOpenDiff(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.DiffOpenRequestedEvent, DataContext as ChatMessage));
        }

        private void OnRestore(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.CheckpointRestoreRequestedEvent, DataContext as ChatMessage));
        }
    }
}
