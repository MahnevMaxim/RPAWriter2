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

        /// <summary>When the message was created (UTC); persisted so a reopened chat keeps its original timestamps.</summary>
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// A saved chat conversation with its messages. Raises change notifications because
    /// the history list binds to <see cref="IsActive"/> — the green "active chat" indicator.
    /// <para>
    /// <see cref="IsActive"/> is a projection of <c>ChatModel.ActiveChatConversation</c> (the single
    /// source of "which chat is active"): at most one conversation is active, none while no chat is
    /// open. It is written only by <c>ChatModel.UpdateActiveFlags</c>, never persisted, and the view
    /// must not set it.</para>
    /// </summary>
    public class ChatConversation : INotifyPropertyChanged
    {
        private string _id = "";
        private string _title = "";
        private DateTime _createdAtUtc;
        private DateTime _updatedAtUtc;
        private bool _isActive;
        private bool _isImportant;
        private int _usedTokens;

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
        /// Derived display flag: <c>true</c> while this conversation is the model's
        /// <c>ChatModel.ActiveChatConversation</c>. It mirrors that property only —
        /// it is not a second source of activity, is never persisted, and the view must not set it
        /// (if it disagrees with the model it is stale by definition). Written only by
        /// <c>ChatModel.UpdateActiveFlags</c>; drives the small green indicator on the history card.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Whether the user pinned this conversation as important in the history list. Unlike
        /// <see cref="IsActive"/> this is persisted data (SQLite <c>is_important</c> column) and the
        /// view only reads it; it is written by <c>ChatModel.ToggleImportant</c>.
        /// </summary>
        public bool IsImportant
        {
            get => _isImportant;
            set { if (_isImportant != value) { _isImportant = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Cumulative token usage of this conversation (prompt/completion/total, cache split, cost).
        /// Runtime-only for now: kept here as plain data so it can be persisted later without a
        /// domain change. Accumulate through <see cref="AccumulateUsage"/> so bindings get notified.
        /// </summary>
        public TokenUsage Usage { get; } = new TokenUsage();

        /// <summary>
        /// Estimated input tokens currently occupying the model's context window for this
        /// conversation. Runtime-only; updated by the model from the provider's prompt assembly
        /// and token reports.
        /// </summary>
        public int UsedTokens
        {
            get => _usedTokens;
            set
            {
                if (_usedTokens != value)
                {
                    _usedTokens = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Adds a single request's usage to <see cref="Usage"/> and notifies bindings.</summary>
        public void AccumulateUsage(TokenUsage delta)
        {
            if (delta == null)
                return;

            Usage.PromptTokens += delta.PromptTokens;
            Usage.CompletionTokens += delta.CompletionTokens;
            Usage.TotalTokens += delta.TotalTokens;
            Usage.CacheHitTokens += delta.CacheHitTokens;
            Usage.CacheMissTokens += delta.CacheMissTokens;
            Usage.EstimatedCostUsd += delta.EstimatedCostUsd;
            OnPropertyChanged(nameof(Usage));
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
