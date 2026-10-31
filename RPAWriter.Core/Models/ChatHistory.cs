using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RPAWriter.Core.Models
{
    /// <summary>
    /// A single chat message persisted in the chat history database.
    /// Plain data: the SQLite store maps it to/from table rows, the chat model maps it
    /// to/from <see cref="ChatMessage"/> when a conversation is saved or reopened.
    /// </summary>
    public class ChatHistoryMessage
    {
        public ChatMessageKind Kind { get; set; } = ChatMessageKind.SystemNotice;
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string Mode { get; set; } = "";
        public string ToolName { get; set; } = "";
        public List<string> Options { get; set; } = new List<string>();
        public string CheckpointHash { get; set; } = "";
    }

    /// <summary>
    /// A saved chat conversation with its messages. Raises change notifications because
    /// the history list binds to <see cref="IsActive"/>: exactly one conversation is active
    /// (the one currently shown in the conversation view) and it carries the green indicator.
    /// </summary>
    public class ChatConversation : INotifyPropertyChanged
    {
        private string _id = "";
        private string _title = "";
        private DateTime _createdAtUtc;
        private DateTime _updatedAtUtc;
        private bool _isActive;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(CardTitle));
                }
            }
        }

        public DateTime CreatedAtUtc
        {
            get => _createdAtUtc;
            set { if (_createdAtUtc != value) { _createdAtUtc = value; OnPropertyChanged(); } }
        }

        public DateTime UpdatedAtUtc
        {
            get => _updatedAtUtc;
            set
            {
                if (_updatedAtUtc != value)
                {
                    _updatedAtUtc = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(DisplayDate));
                }
            }
        }

        /// <summary>Snapshot of the conversation messages as stored in the history database.</summary>
        public List<ChatHistoryMessage> Messages { get; set; } = new List<ChatHistoryMessage>();

        /// <summary>
        /// True for the conversation currently shown in the conversation view; drives the
        /// small green indicator on the history card. Not persisted.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        /// <summary>Text shown in the history dropdown.</summary>
        public string DisplayTitle
        {
            get
            {
                var title = string.IsNullOrWhiteSpace(Title) ? "(empty)" : Title;
                var when = UpdatedAtUtc == default(DateTime)
                    ? ""
                    : "  ·  " + UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return title + when;
            }
        }

        /// <summary>Title for a history card: the first message text, or a placeholder.</summary>
        public string CardTitle => string.IsNullOrWhiteSpace(Title) ? "(empty)" : Title;

        /// <summary>Local date shown on a history card.</summary>
        public string DisplayDate => UpdatedAtUtc == default(DateTime)
            ? ""
            : UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
