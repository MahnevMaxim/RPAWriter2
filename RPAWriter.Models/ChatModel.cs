using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// Chat tab model: owns the conversation state (messages, queue, attachments, history) and
    /// every scenario that changes it (new chat, open, delete, clear, save, view switch).
    /// <para>
    /// The model is UI-free by contract: it is constructed, exercised and asserted without a
    /// <see cref="BaseViewModel"/> and without a view (see <c>documentation/13-model-layer.md</c>).
    /// Layout state (element visibility, focus, scroll) stays in the view; persistence is injected
    /// through <see cref="IChatHistoryStore"/> and <see cref="IUiStateStore"/>.
    /// </para>
    /// <para>
    /// The view host may add messages straight into <see cref="Messages"/> (the chat list control
    /// shares that collection); the model reacts through <see cref="ObservableCollection{T}.CollectionChanged"/>
    /// so the pinned message and the dirty flag stay in sync in both directions.
    /// </para>
    /// </summary>
    public sealed class ChatModel : BaseModel
    {
        private readonly IChatHistoryStore _historyStore;
        private readonly IUiStateStore _uiStateStore;

        private ChatConversation _activeChatConversation;
        private ChatViewMode _viewMode = ChatViewMode.Conversation;
        private ChatMessage _pinnedMessage;
        private AgentMode _mode = AgentMode.Plan;
        private AgentPhase _phase = AgentPhase.Idle;
        private bool _isDirty;
        private bool _suppressDirty;
        private bool _initialized;
        private LogAnalyzer _logAnalyzer;
        private string _lastError = "";

        /// <summary>
        /// Creates the model. <paramref name="uiStateStore"/> may be <c>null</c>: the chat then works
        /// in memory only and nothing is persisted between sessions (headless runs, tests).
        /// <paramref name="logAnalyzer"/> is the child model of the "Analyze logs" panel; the host may
        /// assign it later through <see cref="LogAnalyzer"/> instead, and it may stay <c>null</c>.
        /// </summary>
        public ChatModel(IChatHistoryStore historyStore, IUiStateStore uiStateStore = null, LogAnalyzer logAnalyzer = null)
        {
            _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
            _uiStateStore = uiStateStore;
            LogAnalyzer = logAnalyzer;

            Messages = new ObservableCollection<ChatMessage>();
            Pending = new ObservableCollection<PendingMessage>();
            Attachments = new ObservableCollection<ChatAttachment>();
            History = new ObservableCollection<ChatConversation>();

            _activeChatConversation = CreateConversation();
            Messages.CollectionChanged += OnMessagesChanged;
        }

        #region State

        /// <summary>
        /// Child model of the Analyze logs panel. The host creates it (provider discovery and the
        /// Output window are host concerns) and assigns it before Run(); the chat works without it,
        /// which keeps both models testable in isolation.
        /// </summary>
        public LogAnalyzer LogAnalyzer
        {
            get => _logAnalyzer;
            set
            {
                // Assigned late (after Run), the child still has to start: the host may create it
                // only when the log panel is opened for the first time.
                if (SetProperty(ref _logAnalyzer, value) && _initialized)
                    _logAnalyzer?.Run();
            }
        }

        /// <summary>Transcript of the active conversation, in display order.</summary>
        public ObservableCollection<ChatMessage> Messages { get; }

        /// <summary>Messages queued while the agent was busy (drained by the host after a turn).</summary>
        public ObservableCollection<PendingMessage> Pending { get; }

        /// <summary>Files attached to the next message.</summary>
        public ObservableCollection<ChatAttachment> Attachments { get; }

        /// <summary>Saved conversations, newest first.</summary>
        public ObservableCollection<ChatConversation> History { get; }

        /// <summary>
        /// Conversation currently shown in the conversation view. Setting it moves the green
        /// "active" indicator in the history list (<see cref="ChatConversation.IsActive"/>).
        /// </summary>
        public ChatConversation ActiveChatConversation
        {
            get => _activeChatConversation;
            set
            {
                if (!SetProperty(ref _activeChatConversation, value))
                    return;

                OnPropertyChanged(nameof(ActiveConversationId));
                OnPropertyChanged(nameof(HasActiveConversation));
                UpdateActiveFlags();
            }
        }

        public string ActiveConversationId => _activeChatConversation?.Id ?? "";

        public bool HasActiveConversation => !string.IsNullOrWhiteSpace(ActiveConversationId);

        /// <summary>
        /// Which focus of the chat tab is active. Persisted between sessions
        /// (<see cref="UiStateData.ChatViewMode"/>).
        /// </summary>
        public ChatViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (!SetProperty(ref _viewMode, value))
                    return;

                OnPropertyChanged(nameof(IsHistoryView));
                OnPropertyChanged(nameof(IsConversationView));
            }
        }

        public bool IsHistoryView => _viewMode == ChatViewMode.History;

        public bool IsConversationView => _viewMode == ChatViewMode.Conversation;

        /// <summary>
        /// Message summarised above the transcript: the first user message, or the first entry when
        /// the user has not written anything yet. Updated automatically when messages are added.
        /// </summary>
        public ChatMessage PinnedMessage
        {
            get => _pinnedMessage;
            private set
            {
                if (!SetProperty(ref _pinnedMessage, value))
                    return;

                OnPropertyChanged(nameof(HasPinnedMessage));
            }
        }

        public bool HasPinnedMessage => _pinnedMessage != null;

        /// <summary>Agent mode (Plan/Act/Help) of the current session.</summary>
        public AgentMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        /// <summary>Phase of the current agent turn (drives the footer buttons in the view).</summary>
        public AgentPhase Phase
        {
            get => _phase;
            set => SetProperty(ref _phase, value);
        }

        /// <summary>
        /// True when the transcript changed since the last successful save. The host turns this
        /// into a debounced <see cref="SaveHistoryNow"/> (the timer is view infrastructure).
        /// </summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        /// <summary>Last non-fatal failure (history unavailable, chat could not be opened...). Empty when none.</summary>
        public string LastError
        {
            get => _lastError;
            private set => SetProperty(ref _lastError, value);
        }

        #endregion State

        #region Lifecycle

        /// <summary>
        /// Creates the history storage and loads the saved conversations. Idempotent: the host may
        /// call it explicitly and <c>MvvmFactory.Attach</c> calls it again without side effects.
        /// Never throws: a broken store only sets <see cref="LastError"/>.
        /// </summary>
        public override void Run()
        {
            if (_initialized)
                return;

            _initialized = true;
            LogAnalyzer?.Run();

            try
            {
                _historyStore.Initialize();
            }
            catch (Exception ex)
            {
                LastError = "Chat history unavailable: " + ex.Message;
                return;
            }

            RefreshHistory();
        }

        /// <summary>Last synchronous point to flush the transcript before the tab is unloaded.</summary>
        public override void Closing()
        {
            SaveHistoryNow();
        }

        #endregion Lifecycle

        #region Messages

        /// <summary>
        /// Adds an entry to the transcript. The pinned message and <see cref="IsDirty"/> follow via
        /// <see cref="OnMessagesChanged"/>, so this stays a one-liner for the host.
        /// </summary>
        public ChatMessage AppendMessage(ChatMessage message)
        {
            if (message == null)
                return null;

            Messages.Add(message);
            return message;
        }

        /// <summary>Recomputes <see cref="PinnedMessage"/> from the transcript.</summary>
        public void RefreshPinnedMessage()
        {
            PinnedMessage = Messages.FirstOrDefault(m => m.Kind == ChatMessageKind.User)
                         ?? Messages.FirstOrDefault();
        }

        private void OnMessagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (ChatMessage message in e.NewItems)
                {
                    if (message?.Kind == ChatMessageKind.User && _pinnedMessage == null)
                        RefreshPinnedMessage();
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                RefreshPinnedMessage();
            }

            if (!_suppressDirty && e.Action != NotifyCollectionChangedAction.Reset)
                IsDirty = true;
        }

        #endregion Messages

        #region Pending queue

        /// <summary>
        /// Queues a message while the agent is busy. Empty text without attachments is ignored,
        /// matching the host behaviour (nothing to send).
        /// </summary>
        public PendingMessage Enqueue(string text, IReadOnlyList<ChatAttachment> attachments = null)
        {
            if (string.IsNullOrWhiteSpace(text) && (attachments == null || attachments.Count == 0))
                return null;

            var pending = new PendingMessage
            {
                Text = text ?? "",
                Attachments = attachments ?? new List<ChatAttachment>()
            };

            Pending.Add(pending);
            return pending;
        }

        /// <summary>
        /// Takes the next message to run: the first "Steer" item jumps the queue, otherwise FIFO.
        /// </summary>
        public PendingMessage DequeueNext()
        {
            if (Pending.Count == 0)
                return null;

            var steer = Pending.FirstOrDefault(m => m.Delivery == PendingDelivery.Steer);
            var next = steer ?? Pending[0];
            Pending.Remove(next);
            return next;
        }

        #endregion Pending queue

        #region History scenarios

        /// <summary>Reloads <see cref="History"/> from the store (best effort, newest first).</summary>
        public void RefreshHistory()
        {
            try
            {
                var conversations = _historyStore.ListConversations() ?? new List<ChatConversation>();
                History.Clear();
                foreach (var conversation in conversations)
                    History.Add(conversation);
            }
            catch
            {
                // Listing history is best effort: never break the chat because of the store.
            }

            UpdateActiveFlags();
        }

        /// <summary>
        /// Starts a new empty conversation: saves the current one first, clears the transcript and
        /// the queue, then switches the tab back to the conversation view.
        /// </summary>
        public void StartNewChat(bool markDirty = true)
        {
            SaveHistoryNow();
            ResetToFreshConversation(markDirty);
            ViewMode = ChatViewMode.Conversation;
        }

        /// <summary>
        /// Resets the in-memory conversation without persisting it (used after delete/clear, so the
        /// tab does not keep showing a conversation that no longer exists).
        /// </summary>
        public void ResetConversation()
        {
            ResetToFreshConversation(markDirty: false);
        }

        private void ResetToFreshConversation(bool markDirty)
        {
            Pending.Clear();

            _suppressDirty = true;
            try
            {
                Messages.Clear();
                ActiveChatConversation = CreateConversation();
            }
            finally
            {
                _suppressDirty = false;
            }

            RefreshPinnedMessage();
            IsDirty = markDirty;
        }

        /// <summary>
        /// Loads a saved conversation into the transcript. Returns <c>false</c> when the id is empty,
        /// already active, unknown or the store failed (see <see cref="LastError"/>).
        /// </summary>
        public bool OpenConversation(string id, bool markDirty = true)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            if (string.Equals(id, ActiveConversationId, StringComparison.Ordinal))
                return false;

            SaveHistoryNow();
            Pending.Clear();
            LastError = "";

            ChatConversation stored;
            try
            {
                stored = _historyStore.LoadConversation(id);
            }
            catch (Exception ex)
            {
                LastError = "Failed to open chat: " + ex.Message;
                return false;
            }

            if (stored == null)
                return false;

            _suppressDirty = true;
            try
            {
                Messages.Clear();
                foreach (var storedMessage in stored.Messages ?? new List<ChatHistoryMessage>())
                    Messages.Add(ChatMessageMapper.FromHistoryMessage(storedMessage));

                ActiveChatConversation = stored;
            }
            finally
            {
                _suppressDirty = false;
            }

            RefreshPinnedMessage();
            IsDirty = markDirty;
            return true;
        }

        /// <summary>
        /// Deletes a saved conversation. When it is the active one the tab falls back to a fresh
        /// conversation. Returns <c>false</c> when the store failed (see <see cref="LastError"/>).
        /// </summary>
        public bool DeleteConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            LastError = "";
            try
            {
                _historyStore.DeleteConversation(id);
            }
            catch (Exception ex)
            {
                LastError = "Failed to delete chat: " + ex.Message;
                return false;
            }

            if (string.Equals(id, ActiveConversationId, StringComparison.Ordinal))
                ResetConversation();

            RefreshHistory();
            return true;
        }

        /// <summary>Deletes every saved conversation and resets the tab.</summary>
        public bool ClearHistory()
        {
            LastError = "";
            try
            {
                _historyStore.DeleteAllConversations();
            }
            catch (Exception ex)
            {
                LastError = "Failed to clear chat history: " + ex.Message;
                return false;
            }

            ResetConversation();
            RefreshHistory();
            return true;
        }

        /// <summary>
        /// Persists the active conversation: stores the transcript snapshot, derives the title from
        /// the first user message when it is still empty and refreshes the history list.
        /// Best effort: a failure never interrupts the chat.
        /// </summary>
        public void SaveHistoryNow()
        {
            var conversation = ActiveChatConversation;
            if (conversation == null || string.IsNullOrWhiteSpace(conversation.Id))
                return;

            conversation.Messages = ChatMessageMapper.ToHistoryMessages(Messages);

            if (string.IsNullOrEmpty(conversation.Title))
            {
                var firstUser = conversation.Messages.FirstOrDefault(m => m.Kind == ChatMessageKind.User);
                if (firstUser != null)
                    conversation.Title = ChatMessageMapper.BuildTitle(firstUser.Text);
            }

            conversation.UpdatedAtUtc = DateTime.UtcNow;

            try
            {
                _historyStore.SaveConversation(conversation);
            }
            catch
            {
                return; // keep IsDirty so the next tick retries
            }

            IsDirty = false;
            RefreshHistory();
        }

        /// <summary>
        /// Picks the conversation to restore at startup: the remembered id when it still exists,
        /// otherwise the most recent one, otherwise <c>null</c>.
        /// </summary>
        public ChatConversation ResolveConversationToOpen(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    var stored = _historyStore.LoadConversation(id);
                    if (stored != null)
                        return stored;
                }
                catch
                {
                    // fall back to the most recent conversation below
                }
            }

            return History.FirstOrDefault();
        }

        private void UpdateActiveFlags()
        {
            var activeId = ActiveConversationId;
            foreach (var conversation in History)
                conversation.IsActive = !string.IsNullOrEmpty(activeId)
                                     && string.Equals(conversation.Id, activeId, StringComparison.Ordinal);
        }

        private static ChatConversation CreateConversation()
        {
            return new ChatConversation
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        #endregion History scenarios

        #region UI state

        /// <summary>Loads the persisted session state (defaults when no store is injected).</summary>
        public UiStateData LoadUiState()
        {
            if (_uiStateStore == null)
                return new UiStateData();

            try
            {
                return _uiStateStore.Load() ?? new UiStateData();
            }
            catch
            {
                return new UiStateData();
            }
        }

        /// <summary>
        /// Persists the session state. The active main tab is a view concern, so the host passes it
        /// in; everything else comes from the model.
        /// </summary>
        public void PersistUiState(MainTab activeTab)
        {
            if (_uiStateStore == null)
                return;

            _uiStateStore.Save(new UiStateData
            {
                ChatViewMode = ViewMode,
                LastConversationId = ActiveConversationId,
                AgentMode = Mode,
                ActiveTab = activeTab
            });
        }

        /// <summary>Switches the tab to the conversation view (persisted by the host).</summary>
        public void ShowConversationView()
        {
            ViewMode = ChatViewMode.Conversation;
        }

        /// <summary>Switches the tab to the history list (persisted by the host).</summary>
        public void ShowHistoryView()
        {
            RefreshHistory();
            ViewMode = ChatViewMode.History;
        }

        #endregion UI state

        #region Commands

        /// <summary>Command target: start a new chat.</summary>
        public void NewChatCommand()
        {
            StartNewChat();
        }

        /// <summary>Command target: open a conversation by id (<c>CommandParameter</c> = id).</summary>
        public void OpenConversationCommand(object id)
        {
            OpenConversation(id as string);
        }

        /// <summary>Command target: delete a conversation (<c>CommandParameter</c> = id or conversation).</summary>
        public void DeleteConversationCommand(object target)
        {
            var id = (target as ChatConversation)?.Id ?? target as string;
            DeleteConversation(id);
        }

        /// <summary>Command target: delete every saved conversation.</summary>
        public void ClearHistoryCommand()
        {
            ClearHistory();
        }

        /// <summary>Command target: reload the saved conversations.</summary>
        public void RefreshHistoryCommand()
        {
            RefreshHistory();
        }

        #endregion Commands
    }
}
