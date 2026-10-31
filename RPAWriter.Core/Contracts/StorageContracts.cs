using System.Collections.Generic;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// Persistence seam for the chat history. Models depend on this interface, never on the
    /// SQLite implementation that lives in the VSIX project, so history scenarios are testable
    /// with an in-memory fake.
    /// </summary>
    public interface IChatHistoryStore
    {
        /// <summary>Creates the storage and its schema if needed.</summary>
        void Initialize();

        /// <summary>Returns conversation metadata (newest first), without messages.</summary>
        IReadOnlyList<ChatConversation> ListConversations(int limit = 50);

        /// <summary>Loads a full conversation with its messages, or null if not found.</summary>
        ChatConversation LoadConversation(string id);

        /// <summary>Inserts or updates a conversation and its messages.</summary>
        void SaveConversation(ChatConversation conversation);

        /// <summary>Deletes a conversation and its messages.</summary>
        void DeleteConversation(string id);

        /// <summary>Deletes every stored conversation.</summary>
        void DeleteAllConversations();
    }

    /// <summary>
    /// Persistence seam for the per-session UI state (active tab, chat view mode, last
    /// conversation). Models depend on this interface; the JSON file store implements it.
    /// </summary>
    public interface IUiStateStore
    {
        /// <summary>Loads the persisted state, falling back to defaults on any error.</summary>
        UiStateData Load();

        /// <summary>Saves the state; failures are ignored by implementations.</summary>
        void Save(UiStateData state);
    }
}
