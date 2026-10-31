using System;
using System.Collections.Generic;
using System.Linq;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="IChatHistoryStore"/> used by the model tests. It behaves like the SQLite
    /// store where it matters for the model: it deep-copies on save and load (so a mutated live
    /// conversation never leaks into storage and back), it returns conversations newest first and it
    /// can be told to fail per operation.
    /// </summary>
    public sealed class InMemoryChatHistoryStore : IChatHistoryStore
    {
        private readonly Dictionary<string, ChatConversation> _stored =
            new Dictionary<string, ChatConversation>(StringComparer.Ordinal);

        public int InitializeCount { get; private set; }

        public int SaveCount { get; private set; }

        public int LoadCount { get; private set; }

        public int DeleteCount { get; private set; }

        public int DeleteAllCount { get; private set; }

        public int LastListLimit { get; private set; }

        public bool ThrowOnInitialize { get; set; }

        public bool ThrowOnList { get; set; }

        public bool ThrowOnLoad { get; set; }

        public bool ThrowOnSave { get; set; }

        public bool ThrowOnDelete { get; set; }

        public bool ThrowOnDeleteAll { get; set; }

        /// <summary>Number of conversations currently in the fake storage.</summary>
        public int Count => _stored.Count;

        /// <summary>Adds a conversation straight to storage, as if it had been saved earlier.</summary>
        public void Seed(ChatConversation conversation) => _stored[conversation.Id] = Clone(conversation);

        /// <summary>Returns the stored snapshot (a copy) or null.</summary>
        public ChatConversation Peek(string id) => id != null && _stored.TryGetValue(id, out var stored) ? Clone(stored) : null;

        public void Initialize()
        {
            if (ThrowOnInitialize)
                throw new InvalidOperationException("history store unavailable");

            InitializeCount++;
        }

        public IReadOnlyList<ChatConversation> ListConversations(int limit = 50)
        {
            if (ThrowOnList)
                throw new InvalidOperationException("history list unavailable");

            LastListLimit = limit;
            return _stored.Values
                .OrderByDescending(c => c.UpdatedAtUtc)
                .Take(limit)
                .Select(Clone)
                .ToList();
        }

        public ChatConversation LoadConversation(string id)
        {
            if (ThrowOnLoad)
                throw new InvalidOperationException("conversation could not be loaded");

            LoadCount++;
            return id != null && _stored.TryGetValue(id, out var conversation) ? Clone(conversation) : null;
        }

        public void SaveConversation(ChatConversation conversation)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("conversation could not be saved");

            SaveCount++;
            _stored[conversation.Id] = Clone(conversation);
        }

        public void DeleteConversation(string id)
        {
            if (ThrowOnDelete)
                throw new InvalidOperationException("conversation could not be deleted");

            DeleteCount++;
            _stored.Remove(id);
        }

        public void DeleteAllConversations()
        {
            if (ThrowOnDeleteAll)
                throw new InvalidOperationException("history could not be cleared");

            DeleteAllCount++;
            _stored.Clear();
        }

        private static ChatConversation Clone(ChatConversation source)
        {
            return new ChatConversation
            {
                Id = source.Id,
                Title = source.Title,
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = source.UpdatedAtUtc,
                // IsActive is deliberately not copied: the green indicator is view state, not data.
                Messages = (source.Messages ?? new List<ChatHistoryMessage>())
                    .Select(m => new ChatHistoryMessage
                    {
                        Kind = m.Kind,
                        Text = m.Text,
                        Reasoning = m.Reasoning,
                        Mode = m.Mode,
                        ToolName = m.ToolName,
                        Options = (m.Options ?? new List<string>()).ToList(),
                        CheckpointHash = m.CheckpointHash
                    })
                    .ToList()
            };
        }
    }
}
