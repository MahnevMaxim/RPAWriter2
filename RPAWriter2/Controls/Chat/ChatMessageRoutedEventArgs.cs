using System.Windows;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls.Chat
{
    /// <summary>
    /// Routed event payload raised by a message control (copy, question answered/skipped).
    /// Carries the originating <see cref="ChatMessage"/> so the host can react without
    /// walking the visual tree.
    /// </summary>
    public sealed class ChatMessageRoutedEventArgs : RoutedEventArgs
    {
        public ChatMessageRoutedEventArgs(RoutedEvent routedEvent, ChatMessage? message)
            : base(routedEvent)
        {
            Message = message;
        }

        /// <summary>The message the action applies to (may be null if the DataContext is missing).</summary>
        public ChatMessage? Message { get; }
    }
}
