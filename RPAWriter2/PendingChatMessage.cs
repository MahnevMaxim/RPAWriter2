using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Material.Icons;

namespace RPAWriter2
{
    /// <summary>Delivery mode of a queued message (mirrors Cline's "queue" / "steer").</summary>
    public enum PendingDelivery
    {
        /// <summary>Normal FIFO order.</summary>
        Queue,
        /// <summary>Process before every queued item (jump the queue).</summary>
        Steer
    }

    /// <summary>A user message buffered while the agent is still running.</summary>
    public sealed class PendingChatMessage : INotifyPropertyChanged
    {
        private string _text = "";
        private PendingDelivery _delivery = PendingDelivery.Queue;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; } = Guid.NewGuid().ToString("N");

        /// <summary>Editable message text.</summary>
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); } }
        }

        /// <summary>Attachments captured at send time (data objects; safe to keep while queued).</summary>
        public IReadOnlyList<ChatAttachment> Attachments { get; set; } = new List<ChatAttachment>();

        /// <summary>Delivery mode; the queue pump prefers Steer items.</summary>
        public PendingDelivery Delivery
        {
            get => _delivery;
            set { if (_delivery != value) { _delivery = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeliveryIcon)); OnPropertyChanged(nameof(DeliveryToolTip)); } }
        }

        // Display helpers
        public string Preview => string.IsNullOrWhiteSpace(Text)
            ? "(attachments only)"
            : Text.Replace('\r', ' ').Replace('\n', ' ').Trim();

        public bool HasAttachments => Attachments != null && Attachments.Count > 0;

        public string AttachmentSummary => HasAttachments ? "📎 " + Attachments.Count : "";

        public MaterialIconKind DeliveryIcon => Delivery == PendingDelivery.Steer
            ? MaterialIconKind.FastForward
            : MaterialIconKind.HourglassEmpty;
        public string DeliveryToolTip => Delivery == PendingDelivery.Steer
            ? "Steer: process next (jump the queue)"
            : "Queue: normal FIFO order";

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
