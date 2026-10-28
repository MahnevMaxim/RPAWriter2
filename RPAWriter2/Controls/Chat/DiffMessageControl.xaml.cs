using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Chat card for a <see cref="RPAWriter.Core.Models.ChatMessageKind.Diff"/> indicator:
    /// a single transparent button that opens the iteration diff in the diff tool window.
    /// </summary>
    public partial class DiffMessageControl : UserControl
    {
        public DiffMessageControl()
        {
            InitializeComponent();
        }

        private void OnOpenDiff(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new ChatMessageRoutedEventArgs(ChatMessageCard.DiffOpenRequestedEvent, DataContext as ChatMessage));
        }
    }
}
