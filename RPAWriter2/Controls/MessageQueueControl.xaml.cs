using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Renders the shared pending-message queue and lets the user inspect, edit, reorder,
    /// prioritize ("steer"), delete and clear queued messages. All mutations are applied to the
    /// shared collection owned by the host, so the host's queue pump always sees the up-to-date state.
    /// The control manages its own visibility: it collapses when the queue is empty.
    /// </summary>
    public partial class MessageQueueControl : UserControl
    {
        private ObservableCollection<PendingChatMessage> _messages = new ObservableCollection<PendingChatMessage>();

        public MessageQueueControl()
        {
            InitializeComponent();
            AttachCollection(_messages);
        }

        /// <summary>The shared queue collection owned by the host.</summary>
        public ObservableCollection<PendingChatMessage> Messages
        {
            get => _messages;
            set
            {
                if (!ReferenceEquals(_messages, value))
                    AttachCollection(value ?? new ObservableCollection<PendingChatMessage>());
            }
        }

        /// <summary>Raised after any queue mutation (for host logging; optional).</summary>
        public event EventHandler? Changed;

        private void AttachCollection(ObservableCollection<PendingChatMessage> collection)
        {
            _messages.CollectionChanged -= OnCollectionChanged;

            _messages = collection;
            QueueList.ItemsSource = _messages;
            _messages.CollectionChanged += OnCollectionChanged;
            Reindex();
            UpdateVisibility();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Reindex();
            UpdateVisibility();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Reindex()
        {
            QueuedLabel.Text = "Queued messages (" + _messages.Count + ")";
        }

        private void UpdateVisibility()
        {
            Visibility = _messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static PendingChatMessage? ItemOf(object sender) =>
            (sender as FrameworkElement)?.DataContext as PendingChatMessage;

        private void OnMoveUp(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item == null) return;
            int index = _messages.IndexOf(item);
            if (index <= 0) return;
            _messages.Move(index, index - 1);
        }

        private void OnMoveDown(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item == null) return;
            int index = _messages.IndexOf(item);
            if (index < 0 || index >= _messages.Count - 1) return;
            _messages.Move(index, index + 1);
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item != null)
                _messages.Remove(item);
        }

        private void OnToggleDelivery(object sender, RoutedEventArgs e)
        {
            var item = ItemOf(sender);
            if (item == null) return;
            item.Delivery = item.Delivery == PendingDelivery.Steer
                ? PendingDelivery.Queue
                : PendingDelivery.Steer;
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            _messages.Clear();
        }
    }
}
