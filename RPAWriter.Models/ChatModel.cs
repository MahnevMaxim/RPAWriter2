using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IChatPromptSink _chatSink;
        private readonly IWorkspaceProvider _workspaceProvider;

        /// <summary>
        /// Backing field of <see cref="ActiveChatConversation"/> — the single source of truth for
        /// "which chat is active". Activity is never stored, cached or mirrored anywhere else.
        /// </summary>
        private ChatConversation _activeChatConversation;
        private ChatViewMode _viewMode = ChatViewMode.Conversation;
        private ChatMessage _pinnedMessage;
        private AgentMode _mode = AgentMode.Plan;
        private AgentPhase _phase = AgentPhase.Idle;
        private bool _hasResumableRun;
        private bool _suppressModeSwitch;
        private bool _isDirty;
        private bool _suppressDirty;
        private bool _initialized;
        private LogAnalyzer _logAnalyzer;
        private string _lastError = "";

        /// <summary>
        /// Append-only transcript used as the persistence source. Unlike <see cref="Messages"/>
        /// (the visible/working transcript, which compaction and scope switches may shrink), this
        /// buffer is only ever added to within an active conversation and is reset when the
        /// conversation is detached, so <see cref="SaveHistoryNow"/> can never overwrite a stored
        /// conversation with a truncated snapshot.
        /// </summary>
        private readonly List<ChatMessage> _fullHistory = new List<ChatMessage>();

        /// <summary>Suppresses <see cref="_fullHistory"/> tracking while the visible transcript is replaced by compaction.</summary>
        private bool _suppressFullHistory;

        private readonly Func<DateTime> _utcNow;
        private readonly object _statusTimerGate = new object();
        private Timer _statusTimer;
        private DateTime _statusStartedAtUtc;
        private TimeSpan _statusAccumulated;
        private bool _statusPaused;
        private int _statusElapsedSeconds;
        private string _statusText = "";
        private string _statusErrorText = "";

        /// <summary>
        /// Creates the model. <paramref name="uiStateStore"/> may be <c>null</c>: the chat then works
        /// in memory only and nothing is persisted between sessions (headless runs, tests).
        /// <paramref name="logAnalyzer"/> is the child model of the "Analyze logs" panel; the host may
        /// assign it later through <see cref="LogAnalyzer"/> instead, and it may stay <c>null</c>.
        /// </summary>
        public ChatModel(IChatHistoryStore historyStore, IUiStateStore uiStateStore = null, LogAnalyzer logAnalyzer = null, IChatPromptSink chatSink = null, IWorkspaceProvider workspaceProvider = null, Func<DateTime> utcNow = null)
        {
            _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
            _uiStateStore = uiStateStore;
            _chatSink = chatSink;
            _workspaceProvider = workspaceProvider;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            LogAnalyzer = logAnalyzer;

            Messages = new ObservableCollection<ChatMessage>();
            Pending = new ObservableCollection<PendingMessage>();
            Attachments = new ObservableCollection<ChatAttachment>();
            History = new ObservableCollection<ChatConversation>();

            // No conversation is created here: "no active chat" is the model's ordinary starting
            // state (see CloseActiveChat). The host activates a conversation as soon as something
            // has to be displayed or sent (EnsureActiveConversation, StartNewChat, OpenConversation).
            _activeChatConversation = null;
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
        /// The active conversation — the one and only definition of "which chat is active":
        /// a chat is active while this property is not <c>null</c>.
        /// <para>
        /// Assigning this property is the only way activity changes (open, new chat, restore, close,
        /// delete). Every other member that looks like an activity flag is a derived projection and is
        /// never a second source: <see cref="ActiveConversationId"/>, <see cref="HasActiveConversation"/>
        /// and <see cref="ChatConversation.IsActive"/> (recomputed from here by
        /// <see cref="UpdateActiveFlags"/>). Do not duplicate the state — read this property instead of
        /// caching, copying or persisting "is active" anywhere else.
        /// </para>
        /// <para>
        /// "Closed" is expressed by <c>null</c> and by nothing else — not by <see cref="ViewMode"/>, not
        /// by <see cref="UiStateData.LastConversationId"/> and not by a host flag (a running agent turn is
        /// no activity flag either). A closed chat owns no transcript and is reopened lazily by
        /// <see cref="EnsureActiveConversation"/>.
        /// </para>
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

        /// <summary>
        /// Id of <see cref="ActiveChatConversation"/> — a read-only projection, not a second source of
        /// activity. Never cache it and never branch on it instead of the property itself.
        /// </summary>
        public string ActiveConversationId => _activeChatConversation?.Id ?? "";

        /// <summary>
        /// Convenience projection: <c>true</c> when <see cref="ActiveChatConversation"/> is set.
        /// Derived only - never mirror or persist it.
        /// </summary>
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

        /// <summary>
        /// Agent mode (Plan/Act/Help) of the current session. Assigning it records the switch in the
        /// transcript, so the mode is owned entirely by the model and the two-way toggle binding only
        /// reflects it. <see cref="RestoreMode"/> is the one assignment that does not record.
        /// </summary>
        public AgentMode Mode
        {
            get => _mode;
            set
            {
                if (!SetProperty(ref _mode, value))
                    return;

                RecordModeSwitch(value);
            }
        }

        /// <summary>
        /// Phase of the current agent turn. Drives the footer through bindings (Cancel while
        /// <see cref="AgentPhase.Streaming"/>, "Do it!!!" otherwise), so the host only assigns it
        /// and never touches the controls.
        /// </summary>
        public AgentPhase Phase
        {
            get => _phase;
            set
            {
                if (!SetProperty(ref _phase, value))
                    return;

                // The Resume button is a projection of the phase as well.
                OnPropertyChanged(nameof(CanResume));
            }
        }

        /// <summary>
        /// True while a cancelled turn left a breakpoint the host can still continue. Only the flag
        /// lives here: the breakpoint itself (agent snapshot, mode, context budget) is runner
        /// infrastructure and stays with the host.
        /// </summary>
        public bool HasResumableRun
        {
            get => _hasResumableRun;
            set
            {
                if (!SetProperty(ref _hasResumableRun, value))
                    return;

                OnPropertyChanged(nameof(CanResume));
            }
        }

        /// <summary>
        /// Read-only projection behind the "Resume task" button: a breakpoint exists and no turn is
        /// running. Derived only - never persisted or mirrored in a second field.
        /// </summary>
        public bool CanResume => _hasResumableRun && _phase == AgentPhase.Idle;

        /// <summary>
        /// Elapsed seconds of the current turn, frozen while the agent waits for the user's answer.
        /// Part of the chat domain (not the view): the model owns the turn timer and the view only
        /// binds to this and <see cref="StatusText"/>.
        /// </summary>
        public int StatusElapsedSeconds
        {
            get => _statusElapsedSeconds;
            private set => SetProperty(ref _statusElapsedSeconds, value);
        }

        /// <summary>
        /// Shimmer status line text: "Sending request…", "Thinking… (N s)" or "Waiting for answer…".
        /// Empty when no turn is running.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>
        /// Red error line text shown when the last turn failed ("Error: …"). Empty when none.
        /// </summary>
        public string StatusErrorText
        {
            get => _statusErrorText;
            private set => SetProperty(ref _statusErrorText, value);
        }

        /// <summary>
        /// Starts the turn timer. The model owns the one-second ticker and only raises
        /// <see cref="StatusElapsedSeconds"/>/<see cref="StatusText"/>; the host just binds to them.
        /// </summary>
        public void StartStatus()
        {
            lock (_statusTimerGate)
            {
                _statusTimer?.Dispose();
                _statusStartedAtUtc = _utcNow();
                _statusAccumulated = TimeSpan.Zero;
                _statusPaused = false;
                StatusErrorText = "";
                Phase = AgentPhase.Streaming;
                _statusTimer = new Timer(_ => RefreshStatus(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                RefreshStatusCore();
            }
        }

        /// <summary>Stops the turn timer and returns the chat to <see cref="AgentPhase.Idle"/>.</summary>
        public void StopStatus()
        {
            lock (_statusTimerGate)
            {
                _statusTimer?.Dispose();
                _statusTimer = null;
                _statusPaused = false;
                _statusAccumulated = TimeSpan.Zero;
                StatusElapsedSeconds = 0;
                StatusText = "";
                StatusErrorText = "";
                Phase = AgentPhase.Idle;
            }
        }

        /// <summary>
        /// Freezes the turn timer while an interactive question waits for the user's answer. The turn
        /// stays <see cref="AgentPhase.Streaming"/> (the Cancel button remains available) but the
        /// elapsed seconds stop counting.
        /// </summary>
        public void PauseStatus()
        {
            lock (_statusTimerGate)
            {
                if (_statusPaused || _statusTimer == null)
                    return;

                _statusAccumulated += _utcNow() - _statusStartedAtUtc;
                _statusPaused = true;
                _statusTimer?.Dispose();
                _statusTimer = null;
                StatusText = "Waiting for answer…";
            }
        }

        /// <summary>Resumes the turn timer after the user answered the pending question.</summary>
        public void ResumeStatus()
        {
            lock (_statusTimerGate)
            {
                if (!_statusPaused)
                    return;

                _statusPaused = false;
                _statusStartedAtUtc = _utcNow();
                _statusTimer = new Timer(_ => RefreshStatus(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                RefreshStatusCore();
            }
        }

        /// <summary>Stops the turn timer with an error and sets <see cref="AgentPhase.Error"/>.</summary>
        public void ShowStatusError(string message)
        {
            lock (_statusTimerGate)
            {
                _statusTimer?.Dispose();
                _statusTimer = null;
                _statusPaused = false;
                _statusAccumulated = TimeSpan.Zero;
                StatusElapsedSeconds = 0;
                StatusText = "";
                StatusErrorText = string.IsNullOrEmpty(message) ? "" : "Error: " + message;
                Phase = AgentPhase.Error;
            }
        }

        /// <summary>
        /// Recomputes <see cref="StatusElapsedSeconds"/> and <see cref="StatusText"/> from the clock.
        /// Called by the internal one-second ticker; public so headless tests can advance a fake clock
        /// and assert the elapsed computation deterministically.
        /// </summary>
        public void RefreshStatus()
        {
            lock (_statusTimerGate)
            {
                RefreshStatusCore();
            }
        }

        private void RefreshStatusCore()
        {
            var elapsed = _statusPaused
                ? _statusAccumulated
                : _statusAccumulated + (_utcNow() - _statusStartedAtUtc);

            var seconds = elapsed.TotalSeconds < 0 ? 0 : (int)elapsed.TotalSeconds;
            StatusElapsedSeconds = seconds;

            StatusText = _statusPaused
                ? "Waiting for answer…"
                : (seconds < 2 ? "Sending request…" : "Thinking…") + " (" + seconds + " s)";
        }

        /// <summary>
        /// Applies a user-initiated mode switch. The <see cref="Mode"/> setter records the transcript
        /// entry, so this is a thin alias that keeps the public scenario name used by callers and tests.
        /// </summary>
        public void SwitchMode(AgentMode mode)
        {
            Mode = mode;
        }

        /// <summary>
        /// Restores the persisted mode without recording a transcript entry. The host uses this on
        /// startup instead of assigning <see cref="Mode"/> directly, so a session reload never adds a
        /// spurious "Mode: ..." line to the restored transcript.
        /// </summary>
        public void RestoreMode(AgentMode mode)
        {
            _suppressModeSwitch = true;
            try
            {
                Mode = mode;
            }
            finally
            {
                _suppressModeSwitch = false;
            }
        }

        /// <summary>
        /// Quick command behind the "Do it!!!" footer button: switches the agent to Act mode and sends
        /// the user's input text followed by "Do it!!!" through the injected
        /// <see cref="IChatPromptSink"/>. An empty/whitespace input sends a bare "Do it!!!". A missing
        /// sink makes the command a pure mode switch (headless tests); send failures are reported
        /// through <see cref="NotifyMessage"/>.
        /// </summary>
        public async Task DoItCommand(string inputText)
        {
            SwitchMode(AgentMode.Act);

            if (_chatSink == null)
                return;

            var prompt = string.IsNullOrWhiteSpace(inputText)
                ? "Do it!!!"
                : inputText + Environment.NewLine + "Do it!!!";

            try
            {
                await _chatSink.SendPromptAsync(prompt);
            }
            catch (Exception ex)
            {
                NotifyMessage(new Notify { Message = ex.Message, NotifyLevel = NotifyLevel.Error });
            }
        }

        private void RecordModeSwitch(AgentMode mode)
        {
            if (_suppressModeSwitch)
                return;

            AppendMessage(new ChatMessage
            {
                Kind = ChatMessageKind.ModeSwitch,
                Mode = AgentModeNames.ToValue(mode),
                Text = "Mode: " + AgentModeNames.ToValue(mode)
            });
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

            _historyStore.WorkspaceId = _workspaceProvider?.GetCurrentWorkspace()?.Id ?? "";

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
            lock (_statusTimerGate)
            {
                _statusTimer?.Dispose();
                _statusTimer = null;
            }
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

        /// <summary>
        /// Replaces the visible transcript with a compacted one without shrinking the persisted
        /// history. The host computes the condensed transcript and calls this so compaction frees
        /// the context window (the next turn is built from <see cref="Messages"/>) while the
        /// append-only buffer that <see cref="SaveHistoryNow"/> writes stays intact.
        /// </summary>
        public void ApplyCompactedTranscript(IEnumerable<ChatMessage> compacted)
        {
            if (compacted == null)
                return;

            _suppressDirty = true;
            _suppressFullHistory = true;
            try
            {
                Messages.Clear();
                foreach (var message in compacted)
                    Messages.Add(message);
            }
            finally
            {
                _suppressFullHistory = false;
                _suppressDirty = false;
            }

            RefreshPinnedMessage();
            IsDirty = true;
        }

        private void OnMessagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (ChatMessage message in e.NewItems)
                {
                    if (message?.Kind == ChatMessageKind.User && _pinnedMessage == null)
                        RefreshPinnedMessage();

                    // Keep the append-only persistence buffer in sync with real additions. The
                    // compaction path suppresses this (ApplyCompactedTranscript) so a compacted
                    // visible transcript never pollutes the persisted history with duplicates.
                    if (message != null && !_suppressFullHistory)
                        _fullHistory.Add(message);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (ChatMessage message in e.OldItems)
                {
                    if (message != null)
                        _fullHistory.Remove(message);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                RefreshPinnedMessage();
                // _fullHistory is deliberately left intact here: Clear() is used both to detach a
                // conversation (callers reset the buffer explicitly) and to replace the visible
                // transcript during compaction (the buffer must survive).
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
        /// Re-scopes history to another workspace after the open solution changed. The active
        /// conversation is flushed to its previous scope first, then closed; the list is reloaded
        /// from the new scope.
        /// </summary>
        public void SetWorkspaceScope(string workspaceId)
        {
            var normalized = workspaceId ?? "";

            // A solution event that reports the same scope (e.g. a spurious Opened/AfterClosing)
            // must not flush, close the active chat or reload history: doing so on every solution
            // event is what tore an in-progress conversation apart. Only a real scope change acts.
            if (string.Equals(_historyStore.WorkspaceId ?? "", normalized, StringComparison.Ordinal))
                return;

            // Flush while still scoped to the previous workspace, then switch and reload.
            SaveHistoryNow();
            _historyStore.WorkspaceId = normalized;
            ClearConversationState(markDirty: false);
            ActiveChatConversation = null;
            ViewMode = ChatViewMode.History;
            LastError = "";
            RefreshHistory();
        }

        /// <summary>
        /// Starts a new empty conversation: saves the current one first, clears the transcript and
        /// the queue, then switches the tab back to the conversation view.
        /// </summary>
        public void StartNewChat(bool markDirty = true)
        {
            SaveHistoryNow();
            ClearConversationState(markDirty);
            ActiveChatConversation = CreateConversation();
            ViewMode = ChatViewMode.Conversation;
        }

        /// <summary>
        /// Resets the in-memory conversation without persisting it (used after delete/clear, so the
        /// tab does not keep showing a conversation that no longer exists). The tab is left without an
        /// active conversation — the same "closed" state <see cref="CloseActiveChat"/> produces.
        /// </summary>
        public void ResetConversation()
        {
            ClearConversationState(markDirty: false);
            ActiveChatConversation = null;
        }

        /// <summary>
        /// Returns the active conversation, creating one when the chat is closed. Every entry point that
        /// produces a message on the user's behalf (Send errors, "Send to RPA Writer", "Send exception to
        /// RPA Writer", notification banners) calls this first, so a message can never be written into a
        /// transcript that belongs to no conversation — such a message would be neither displayed nor
        /// saved.
        /// <para>
        /// Idempotent: with a conversation already active it only returns it.
        /// </para>
        /// </summary>
        public ChatConversation EnsureActiveConversation()
        {
            if (_activeChatConversation != null)
                return _activeChatConversation;

            // A closed chat owns no transcript: drop whatever was appended while it was closed, so it
            // does not leak into the conversation created here.
            ClearConversationState(markDirty: false);
            ActiveChatConversation = CreateConversation();
            return _activeChatConversation;
        }

        /// <summary>
        /// Closes the chat (the close button of the conversation header): flushes the transcript, drops
        /// it from memory and leaves the tab without an active conversation —
        /// <see cref="ActiveChatConversation"/> becomes <c>null</c>, so the next <see cref="PersistUiState"/>
        /// writes an empty <see cref="UiStateData.LastConversationId"/> and the closed chat is not
        /// restored on the next start. The conversation stays in <see cref="History"/> and can be
        /// reopened at any time.
        /// <para>
        /// Closing is not <see cref="ShowHistoryView"/>: browsing the history keeps the conversation
        /// active, so messages keep landing in it and the tab returns to the conversation view.
        /// </para>
        /// </summary>
        public void CloseActiveChat()
        {
            SaveHistoryNow();
            ClearConversationState(markDirty: false);
            ActiveChatConversation = null;
            ViewMode = ChatViewMode.History;
        }

        /// <summary>
        /// Clears what belongs to the conversation in memory (transcript, queue, pinned entry) and
        /// detaches it without creating a replacement: the caller decides whether the tab continues with
        /// a fresh conversation (<see cref="StartNewChat"/>, <see cref="EnsureActiveConversation"/>) or
        /// with none at all (<see cref="CloseActiveChat"/>, <see cref="ResetConversation"/>).
        /// </summary>
        private void ClearConversationState(bool markDirty)
        {
            Pending.Clear();

            _suppressDirty = true;
            try
            {
                Messages.Clear();
                _fullHistory.Clear();
                ActiveChatConversation = null;
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

            LastError = "";

            // Load the target first: a missing conversation fails fast without flushing the active
            // one, and the active chat is only saved once the open is known to succeed.
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

            SaveHistoryNow();
            Pending.Clear();

            _suppressDirty = true;
            try
            {
                Messages.Clear();
                _fullHistory.Clear();
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
        /// A closed chat has nothing to save: with <see cref="ActiveChatConversation"/> <c>null</c> the call
        /// returns immediately.
        /// Best effort: a failure never interrupts the chat.
        /// </summary>
        public void SaveHistoryNow()
        {
            var conversation = ActiveChatConversation;
            if (conversation == null || string.IsNullOrWhiteSpace(conversation.Id))
                return;

            // Persist from the append-only full history, not from the visible transcript: compaction
            // and scope switches may shrink Messages without the stored conversation losing its tail.
            conversation.Messages = ChatMessageMapper.ToHistoryMessages(_fullHistory);

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
        /// otherwise the most recent one. An empty id means the previous session ended with the chat
        /// closed (<see cref="CloseActiveChat"/> persists it that way) and returns <c>null</c>: nothing is
        /// guessed, so a chat the user closed does not come back on the next start.
        /// </summary>
        public ChatConversation ResolveConversationToOpen(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

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

            return History.FirstOrDefault();
        }

        /// <summary>
        /// Projects <see cref="ActiveChatConversation"/> onto the history list and is the single writer
        /// of <see cref="ChatConversation.IsActive"/>. Called from the property setter and after every
        /// history reload; no other code path (the view included) may set that flag.
        /// </summary>
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
                // Restore hint for the next session only — not an activity flag.
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
