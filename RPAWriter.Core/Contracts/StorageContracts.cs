using System.Collections.Generic;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// Persistence seam for the chat history. Models depend on this interface, never on the
    /// SQLite implementation that lives in the VSIX project, so history scenarios are testable
    /// with an in-memory fake.
    /// </summary>
    public interface IChatHistoryStore
    {
        /// <summary>
        /// Workspace id used to scope every list/load/save/delete operation. An empty value means
        /// the legacy/global scope (no solution open). The model sets this from the workspace
        /// provider before initializing or refreshing history.
        /// </summary>
        string WorkspaceId { get; set; }

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

    /// <summary>
    /// Persistence seam for extension settings. Models depend on this interface, never on the
    /// static <c>SettingsStore</c> or on the concrete JSON file location.
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>Loads the persisted settings, falling back to defaults on any error.</summary>
        SettingsData Load();

        /// <summary>Saves the settings; failures are ignored by implementations.</summary>
        void Save(SettingsData settings);
    }

    /// <summary>
    /// Persistence seam for the workspace-scoped log settings. Models depend on this interface;
    /// the concrete store resolves the workspace folder through <see cref="AppDataPaths"/>.
    /// </summary>
    public interface IWorkspaceLogSettingsStore
    {
        /// <summary>Loads the workspace log settings, falling back to defaults on any error.</summary>
        WorkspaceLogSettings Load();

        /// <summary>Saves the workspace log settings; failures are ignored by implementations.</summary>
        void Save(WorkspaceLogSettings settings);
    }

    /// <summary>
    /// Protected storage for secrets (the API key). The key never lives in the settings JSON and is
    /// never duplicated per workspace; the concrete implementation (DPAPI on Windows) encrypts it.
    /// </summary>
    public interface ISecretsStore
    {
        /// <summary>Returns the stored API key, or empty when none is stored or decryption fails.</summary>
        string LoadApiKey();

        /// <summary>Stores the API key; pass null or empty to remove it.</summary>
        void SaveApiKey(string apiKey);

        /// <summary>Removes the stored API key.</summary>
        void ClearApiKey();
    }
}
