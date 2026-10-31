using System.Collections.Generic;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Adapts the static <see cref="ChatHistoryStore"/> to <see cref="IChatHistoryStore"/> so that
    /// models (chat model, log analyzer) depend on the Core contract instead of SQLite. Kept in the
    /// VSIX project because System.Data.SQLite is only available here.
    /// </summary>
    public sealed class SqliteChatHistoryStore : IChatHistoryStore
    {
        public void Initialize() => ChatHistoryStore.Initialize();

        public IReadOnlyList<ChatConversation> ListConversations(int limit = 50)
            => ChatHistoryStore.ListConversations(limit);

        public ChatConversation LoadConversation(string id) => ChatHistoryStore.LoadConversation(id)!;

        public void SaveConversation(ChatConversation conversation)
            => ChatHistoryStore.SaveConversation(conversation);

        public void DeleteConversation(string id) => ChatHistoryStore.DeleteConversation(id);

        public void DeleteAllConversations() => ChatHistoryStore.DeleteAllConversations();
    }
}
