using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Models;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Covers <see cref="ChatModel"/> headless: no view model, no view, no Visual Studio.
    /// Every scenario the chat tab performs (new chat, open, delete, clear, save, view switch,
    /// pending queue) is exercised here on an in-memory store.
    /// </summary>
    [TestFixture]
    public class ChatModelTests
    {
        private InMemoryChatHistoryStore _store;
        private InMemoryUiStateStore _uiState;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryChatHistoryStore();
            _uiState = new InMemoryUiStateStore();
        }

        private ChatModel CreateModel(bool withUiState = true)
        {
            return new ChatModel(_store, withUiState ? _uiState : null);
        }

        private static ChatMessage UserText(string text)
        {
            return new ChatMessage { Kind = ChatMessageKind.User, Text = text };
        }

        private static ChatMessage AgentText(string text)
        {
            return new ChatMessage { Kind = ChatMessageKind.AgentText, Text = text };
        }

        private static ChatConversation StoredConversation(string id, string title, params ChatHistoryMessage[] messages)
        {
            return new ChatConversation
            {
                Id = id,
                Title = title,
                CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                Messages = messages.ToList()
            };
        }

        #region Construction

        [Test]
        public void CanBeConstructedWithoutViewModelAndView()
        {
            var model = CreateModel();

            Assert.That(model.ViewModel, Is.Null, "a model must start without a view model");
            Assert.That(model.Messages, Is.Empty);
            Assert.That(model.Pending, Is.Empty);
            Assert.That(model.Attachments, Is.Empty);
            Assert.That(model.History, Is.Empty);
            Assert.That(model.ActiveChatConversation, Is.Not.Null);
            Assert.That(model.ActiveConversationId, Is.Not.Empty);
            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.Conversation));
            Assert.That(model.Mode, Is.EqualTo(AgentMode.Plan));
            Assert.That(model.Phase, Is.EqualTo(AgentPhase.Idle));
            Assert.That(model.PinnedMessage, Is.Null);
            Assert.That(model.IsDirty, Is.False);
        }

        [Test]
        public void CanBeConstructedWithoutUiStateStore()
        {
            var model = CreateModel(withUiState: false);

            Assert.DoesNotThrow(() => model.PersistUiState(MainTab.Chat));
            Assert.That(model.LoadUiState().ChatViewMode, Is.EqualTo(ChatViewMode.Conversation));
        }

        [Test]
        public void Constructor_RejectsMissingHistoryStore()
        {
            Assert.Throws<ArgumentNullException>(() => new ChatModel(null, _uiState));
        }

        #endregion Construction

        #region Run / lifecycle

        [Test]
        public void Run_InitializesStoreAndLoadsHistory()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();

            model.Run();

            Assert.That(_store.InitializeCount, Is.EqualTo(1));
            Assert.That(model.History.Select(c => c.Id), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Run_IsIdempotent()
        {
            var model = CreateModel();

            model.Run();
            model.Run();

            Assert.That(_store.InitializeCount, Is.EqualTo(1));
        }

        [Test]
        public void Run_RecordsLastErrorWhenStoreIsUnavailable()
        {
            _store.ThrowOnInitialize = true;
            var model = CreateModel();

            model.Run();

            Assert.That(model.LastError, Does.Contain("Chat history unavailable"));
        }

        [Test]
        public void Run_StartsTheLogAnalyzerChildModel()
        {
            var analyzer = CreateLogAnalyzer();
            var model = new ChatModel(_store, null, analyzer);

            model.Run();

            Assert.That(analyzer.IsSettingsLoaded, Is.True, "the child model runs with the chat model");
        }

        [Test]
        public void LogAnalyzer_RaisesPropertyChangedWhenAttached()
        {
            var model = CreateModel();
            model.Run();
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);
            var analyzer = CreateLogAnalyzer();

            model.LogAnalyzer = analyzer;

            Assert.That(model.LogAnalyzer, Is.SameAs(analyzer));
            Assert.That(changed, Does.Contain(nameof(ChatModel.LogAnalyzer)),
                "the panel binds to Chat.LogAnalyzer, so the property must notify");
            Assert.That(analyzer.IsSettingsLoaded, Is.True, "a child assigned after Run is started too");
        }

        /// <summary>Minimal log model over the test fakes (the model is exercised in LogAnalyzerTests).</summary>
        private static LogAnalyzer CreateLogAnalyzer()
        {
            var sink = new RecordingChatSink();
            return new LogAnalyzer(
                new FakeLogProviderSource(),
                new FakeOutputWindowTextSource(),
                sink,
                sink,
                new InMemoryLogAnalysisSettings());
        }

        [Test]
        public void Closing_SavesActiveConversation()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));

            model.Closing();

            Assert.That(_store.SaveCount, Is.EqualTo(1));
            Assert.That(model.IsDirty, Is.False);
        }

        #endregion Run / lifecycle

        #region Messages and pinned message

        [Test]
        public void AppendMessage_AddsToTranscriptAndMarksDirty()
        {
            var model = CreateModel();
            var message = UserText("hello");

            var appended = model.AppendMessage(message);

            Assert.That(appended, Is.SameAs(message));
            Assert.That(model.Messages, Has.Count.EqualTo(1));
            Assert.That(model.IsDirty, Is.True);
        }

        [Test]
        public void AppendMessage_IgnoresNull()
        {
            var model = CreateModel();

            Assert.That(model.AppendMessage(null), Is.Null);
            Assert.That(model.Messages, Is.Empty);
        }

        [Test]
        public void AppendMessage_PinsFirstUserMessage()
        {
            var model = CreateModel();
            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "notice" });
            var user = UserText("real question");
            model.AppendMessage(user);

            Assert.That(model.PinnedMessage, Is.SameAs(user));
            Assert.That(model.HasPinnedMessage, Is.True);
        }

        [Test]
        public void AppendMessage_DoesNotReplacePinnedUserMessage()
        {
            var model = CreateModel();
            var first = UserText("first");
            model.AppendMessage(first);
            model.AppendMessage(UserText("second"));

            Assert.That(model.PinnedMessage, Is.SameAs(first));
        }

        [Test]
        public void ExplicitRefreshPinsFirstEntryWhenThereIsNoUserMessage()
        {
            // Same as the host: the pinned header is refreshed explicitly on new chat / open,
            // and then falls back to the first entry when the user has not written anything.
            var model = CreateModel();
            var notice = new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "notice" };
            model.AppendMessage(notice);

            model.RefreshPinnedMessage();

            Assert.That(model.PinnedMessage, Is.SameAs(notice));
        }

        [Test]
        public void SystemNoticeAloneDoesNotPinTheHeader()
        {
            var model = CreateModel();

            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "notice" });

            Assert.That(model.PinnedMessage, Is.Null, "the header only pins a user message as it arrives");
        }

        [Test]
        public void MessagesAddedByTheView_AlsoUpdatePinnedMessageAndDirtyFlag()
        {
            // The host adds entries through the shared collection (ChatMessagesControl.Append).
            var model = CreateModel();
            var user = UserText("from the view");

            model.Messages.Add(user);

            Assert.That(model.PinnedMessage, Is.SameAs(user));
            Assert.That(model.IsDirty, Is.True);
        }

        [Test]
        public void ClearMessages_ResetsPinnedMessage()
        {
            var model = CreateModel();
            model.AppendMessage(UserText("hello"));

            model.Messages.Clear();

            Assert.That(model.PinnedMessage, Is.Null);
            Assert.That(model.HasPinnedMessage, Is.False);
        }

        [Test]
        public void PinnedMessage_NotifiesPropertyChanged()
        {
            var model = CreateModel();
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.AppendMessage(UserText("hello"));

            Assert.That(changed, Does.Contain(nameof(ChatModel.PinnedMessage)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.HasPinnedMessage)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.IsDirty)));
        }

        #endregion Messages and pinned message

        #region Save history

        [Test]
        public void SaveHistoryNow_StoresTranscriptSnapshot()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));
            model.AppendMessage(AgentText("hi there"));

            model.SaveHistoryNow();

            var stored = _store.Peek(model.ActiveConversationId);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.Messages.Select(m => m.Kind),
                Is.EqualTo(new[] { ChatMessageKind.User, ChatMessageKind.AgentText }));
            Assert.That(stored.Messages.Select(m => m.Text), Is.EqualTo(new[] { "hello", "hi there" }));
        }

        [Test]
        public void SaveHistoryNow_DerivesTitleFromFirstUserMessage()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "notice" });
            model.AppendMessage(UserText("Deploy the robot"));

            model.SaveHistoryNow();

            Assert.That(model.ActiveChatConversation.Title, Is.EqualTo("Deploy the robot"));
            Assert.That(_store.Peek(model.ActiveConversationId).Title, Is.EqualTo("Deploy the robot"));
        }

        [Test]
        public void SaveHistoryNow_TruncatesLongTitle()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText(new string('x', 100)));

            model.SaveHistoryNow();

            var title = model.ActiveChatConversation.Title;
            Assert.That(title, Has.Length.EqualTo(61));
            Assert.That(title, Does.EndWith("…"));
        }

        [Test]
        public void SaveHistoryNow_KeepsManualTitle()
        {
            var model = CreateModel();
            model.Run();
            model.ActiveChatConversation.Title = "Fixed name";
            model.AppendMessage(UserText("some text"));

            model.SaveHistoryNow();

            Assert.That(model.ActiveChatConversation.Title, Is.EqualTo("Fixed name"));
        }

        [Test]
        public void SaveHistoryNow_UpdatesTimestampAndClearsDirty()
        {
            var model = CreateModel();
            model.Run();
            var before = model.ActiveChatConversation.UpdatedAtUtc;
            model.AppendMessage(UserText("hello"));
            Assert.That(model.IsDirty, Is.True);

            model.SaveHistoryNow();

            Assert.That(model.ActiveChatConversation.UpdatedAtUtc, Is.GreaterThanOrEqualTo(before));
            Assert.That(model.IsDirty, Is.False);
        }

        [Test]
        public void SaveHistoryNow_RefreshesHistoryList()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));

            model.SaveHistoryNow();

            Assert.That(model.History.Select(c => c.Id), Contains.Item(model.ActiveConversationId));
        }

        [Test]
        public void SaveHistoryNow_KeepsDirtyWhenStoreFails()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));
            _store.ThrowOnSave = true;

            Assert.DoesNotThrow(() => model.SaveHistoryNow());

            Assert.That(model.IsDirty, Is.True, "a failed save must keep the transcript dirty so it is retried");
        }

        [Test]
        public void SaveHistoryNow_DoesNothingWithoutActiveConversation()
        {
            var model = CreateModel();
            model.ActiveChatConversation = null;

            model.SaveHistoryNow();

            Assert.That(_store.SaveCount, Is.Zero);
        }

        #endregion Save history

        #region New chat / open / delete / clear

        [Test]
        public void StartNewChat_SavesPreviousConversationAndStartsFreshOne()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("old chat"));
            var previousId = model.ActiveConversationId;

            model.StartNewChat();

            Assert.That(_store.Peek(previousId), Is.Not.Null, "the previous conversation must be flushed first");
            Assert.That(model.ActiveConversationId, Is.Not.EqualTo(previousId));
            Assert.That(model.Messages, Is.Empty);
            Assert.That(model.PinnedMessage, Is.Null);
            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.Conversation));
        }

        [Test]
        public void StartNewChat_ClearsPendingQueue()
        {
            var model = CreateModel();
            model.Enqueue("queued");

            model.StartNewChat();

            Assert.That(model.Pending, Is.Empty);
        }

        [Test]
        public void StartNewChat_DoesNotMarkDirtyWhenAskedForCleanState()
        {
            var model = CreateModel();

            model.StartNewChat(markDirty: false);

            Assert.That(model.IsDirty, Is.False);
        }

        [Test]
        public void StartNewChat_ClearsActiveIndicators()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            model.OpenConversation("a");
            Assert.That(model.History.Count(c => c.IsActive), Is.EqualTo(1));

            model.StartNewChat();

            Assert.That(model.History.Count(c => c.IsActive), Is.Zero);
        }

        [Test]
        public void OpenConversation_LoadsTranscriptFromStore()
        {
            _store.Seed(StoredConversation("a", "First",
                new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "question" },
                new ChatHistoryMessage { Kind = ChatMessageKind.AgentText, Text = "answer" }));
            var model = CreateModel();
            model.Run();

            var opened = model.OpenConversation("a");

            Assert.That(opened, Is.True);
            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "question", "answer" }));
            Assert.That(model.ActiveConversationId, Is.EqualTo("a"));
            Assert.That(model.PinnedMessage, Is.Not.Null);
            Assert.That(model.PinnedMessage.Text, Is.EqualTo("question"));
        }

        [Test]
        public void OpenConversation_RestoresToolCallAndCheckpointPayloads()
        {
            _store.Seed(StoredConversation("a", "First",
                new ChatHistoryMessage { Kind = ChatMessageKind.ToolCall, ToolName = "read_file", Text = "content" },
                new ChatHistoryMessage { Kind = ChatMessageKind.Checkpoint, CheckpointHash = "abc123" }));
            var model = CreateModel();
            model.Run();

            model.OpenConversation("a");

            var tool = model.Messages[0].Payload as ToolCallPayload;
            Assert.That(tool, Is.Not.Null);
            Assert.That(tool.ToolName, Is.EqualTo("read_file"));
            Assert.That(tool.Result, Is.EqualTo("content"));

            var checkpoint = model.Messages[1].Payload as CheckpointPayload;
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(checkpoint.CommitHash, Is.EqualTo("abc123"));
        }

        [Test]
        public void OpenConversation_ReturnsFalseForUnknownId()
        {
            var model = CreateModel();
            model.Run();

            Assert.That(model.OpenConversation("missing"), Is.False);
            Assert.That(model.LastError, Is.Empty);
        }

        [Test]
        public void OpenConversation_ReturnsFalseForEmptyId()
        {
            var model = CreateModel();

            Assert.That(model.OpenConversation("  "), Is.False);
            Assert.That(_store.LoadCount, Is.Zero);
        }

        [Test]
        public void OpenConversation_IgnoresTheAlreadyActiveConversation()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            model.OpenConversation("a");
            var loadsBefore = _store.LoadCount;

            Assert.That(model.OpenConversation("a"), Is.False);
            Assert.That(_store.LoadCount, Is.EqualTo(loadsBefore), "reopening the active chat must not reload it");
        }

        [Test]
        public void OpenConversation_RecordsLastErrorWhenStoreFails()
        {
            var model = CreateModel();
            model.Run();
            _store.ThrowOnLoad = true;

            Assert.That(model.OpenConversation("a"), Is.False);
            Assert.That(model.LastError, Does.Contain("Failed to open chat"));
        }

        [Test]
        public void OpenConversation_DoesNotMarkDirtyWhenAskedForCleanState()
        {
            _store.Seed(StoredConversation("a", "First",
                new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "question" }));
            var model = CreateModel();
            model.Run();

            model.OpenConversation("a", markDirty: false);

            Assert.That(model.IsDirty, Is.False, "loading a saved conversation is not an edit");
        }

        [Test]
        public void DeleteConversation_RemovesItFromStoreAndHistory()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();

            var deleted = model.DeleteConversation("a");

            Assert.That(deleted, Is.True);
            Assert.That(_store.Peek("a"), Is.Null);
            Assert.That(model.History.Select(c => c.Id), Does.Not.Contain("a"));
        }

        [Test]
        public void DeleteConversation_ResetsActiveConversationWhenItIsDeleted()
        {
            _store.Seed(StoredConversation("a", "First", new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "q" }));
            var model = CreateModel();
            model.Run();
            model.OpenConversation("a");

            model.DeleteConversation("a");

            Assert.That(model.ActiveConversationId, Is.Not.EqualTo("a"));
            Assert.That(model.Messages, Is.Empty);
        }

        [Test]
        public void DeleteConversation_KeepsActiveConversationWhenAnotherIsDeleted()
        {
            _store.Seed(StoredConversation("a", "First"));
            _store.Seed(StoredConversation("b", "Second"));
            var model = CreateModel();
            model.Run();
            model.OpenConversation("a");

            model.DeleteConversation("b");

            Assert.That(model.ActiveConversationId, Is.EqualTo("a"));
        }

        [Test]
        public void DeleteConversation_RecordsLastErrorWhenStoreFails()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            _store.ThrowOnDelete = true;

            Assert.That(model.DeleteConversation("a"), Is.False);
            Assert.That(model.LastError, Does.Contain("Failed to delete chat"));
        }

        [Test]
        public void ClearHistory_RemovesEveryConversation()
        {
            _store.Seed(StoredConversation("a", "First"));
            _store.Seed(StoredConversation("b", "Second"));
            var model = CreateModel();
            model.Run();

            var cleared = model.ClearHistory();

            Assert.That(cleared, Is.True);
            Assert.That(_store.Count, Is.Zero);
            Assert.That(model.History, Is.Empty);
            Assert.That(model.Messages, Is.Empty);
        }

        [Test]
        public void ClearHistory_RecordsLastErrorWhenStoreFails()
        {
            var model = CreateModel();
            model.Run();
            _store.ThrowOnDeleteAll = true;

            Assert.That(model.ClearHistory(), Is.False);
            Assert.That(model.LastError, Does.Contain("Failed to clear chat history"));
        }

        [Test]
        public void RefreshHistory_IsBestEffort()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            _store.ThrowOnList = true;

            Assert.DoesNotThrow(() => model.RefreshHistory());
            Assert.That(model.LastError, Is.Empty);
        }

        #endregion New chat / open / delete / clear

        #region Active conversation indicator

        [Test]
        public void OnlyTheActiveConversationIsMarkedActive()
        {
            _store.Seed(StoredConversation("a", "First"));
            _store.Seed(StoredConversation("b", "Second"));
            _store.Seed(StoredConversation("c", "Third"));
            var model = CreateModel();
            model.Run();

            model.OpenConversation("b");

            var active = model.History.Where(c => c.IsActive).Select(c => c.Id).ToArray();
            Assert.That(active, Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void SettingActiveConversationMovesTheIndicator()
        {
            _store.Seed(StoredConversation("a", "First"));
            _store.Seed(StoredConversation("b", "Second"));
            var model = CreateModel();
            model.Run();
            model.OpenConversation("a");

            model.OpenConversation("b");

            Assert.That(model.History.Single(c => c.IsActive).Id, Is.EqualTo("b"));
        }

        [Test]
        public void RefreshHistory_ClearsStaleIndicators()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            model.History[0].IsActive = true;

            model.RefreshHistory();

            Assert.That(model.History.Count(c => c.IsActive), Is.Zero);
        }

        [Test]
        public void ActiveChatConversation_RaisesPropertyChanged()
        {
            var model = CreateModel();
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.ActiveChatConversation = new ChatConversation { Id = "x" };

            Assert.That(changed, Does.Contain(nameof(ChatModel.ActiveChatConversation)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.ActiveConversationId)));
        }

        #endregion Active conversation indicator

        #region Pending queue

        [Test]
        public void Enqueue_IgnoresEmptyMessageWithoutAttachments()
        {
            var model = CreateModel();

            Assert.That(model.Enqueue("   "), Is.Null);
            Assert.That(model.Pending, Is.Empty);
        }

        [Test]
        public void Enqueue_KeepsAttachmentsOnlyMessage()
        {
            var model = CreateModel();
            var attachments = new List<ChatAttachment> { new ChatAttachment { FileName = "a.txt" } };

            var pending = model.Enqueue("", attachments);

            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.HasAttachments, Is.True);
            Assert.That(model.Pending, Has.Count.EqualTo(1));
        }

        [Test]
        public void DequeueNext_ReturnsHeadInFifoOrder()
        {
            var model = CreateModel();
            model.Enqueue("first");
            model.Enqueue("second");

            Assert.That(model.DequeueNext().Text, Is.EqualTo("first"));
            Assert.That(model.DequeueNext().Text, Is.EqualTo("second"));
            Assert.That(model.DequeueNext(), Is.Null);
        }

        [Test]
        public void DequeueNext_PrefersSteerItem()
        {
            var model = CreateModel();
            model.Enqueue("queued");
            var steer = model.Enqueue("steer me");
            steer.Delivery = PendingDelivery.Steer;

            Assert.That(model.DequeueNext(), Is.SameAs(steer));
            Assert.That(model.Pending, Has.Count.EqualTo(1));
        }

        #endregion Pending queue

        #region View mode and UI state

        [Test]
        public void ShowHistoryView_RefreshesListAndSwitchesMode()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();

            model.ShowHistoryView();

            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.History));
            Assert.That(model.IsHistoryView, Is.True);
            Assert.That(model.History, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ShowConversationView_SwitchesMode()
        {
            var model = CreateModel();
            model.ViewMode = ChatViewMode.History;

            model.ShowConversationView();

            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.Conversation));
            Assert.That(model.IsConversationView, Is.True);
        }

        [Test]
        public void PersistUiState_WritesModelState()
        {
            var model = CreateModel();
            model.Run();
            model.Mode = AgentMode.Act;
            model.ViewMode = ChatViewMode.History;

            model.PersistUiState(MainTab.Settings);

            Assert.That(_uiState.SaveCount, Is.EqualTo(1));
            Assert.That(_uiState.State.ChatViewMode, Is.EqualTo(ChatViewMode.History));
            Assert.That(_uiState.State.LastConversationId, Is.EqualTo(model.ActiveConversationId));
            Assert.That(_uiState.State.AgentMode, Is.EqualTo(AgentMode.Act));
            Assert.That(_uiState.State.ActiveTab, Is.EqualTo(MainTab.Settings));
        }

        [Test]
        public void LoadUiState_ReturnsPersistedState()
        {
            _uiState.Save(new UiStateData
            {
                ChatViewMode = ChatViewMode.History,
                LastConversationId = "a",
                AgentMode = AgentMode.Help,
                ActiveTab = MainTab.KnowledgeBase
            });
            var model = CreateModel();

            var state = model.LoadUiState();

            Assert.That(state.ChatViewMode, Is.EqualTo(ChatViewMode.History));
            Assert.That(state.LastConversationId, Is.EqualTo("a"));
            Assert.That(state.AgentMode, Is.EqualTo(AgentMode.Help));
            Assert.That(state.ActiveTab, Is.EqualTo(MainTab.KnowledgeBase));
        }

        [Test]
        public void LoadUiState_FallsBackToDefaultsWhenStoreFails()
        {
            _uiState.ThrowOnLoad = true;
            var model = CreateModel();

            var state = model.LoadUiState();

            Assert.That(state, Is.Not.Null);
            Assert.That(state.ChatViewMode, Is.EqualTo(ChatViewMode.Conversation));
            Assert.That(state.LastConversationId, Is.Empty);
        }

        [Test]
        public void ResolveConversationToOpen_PrefersTheRememberedId()
        {
            _store.Seed(StoredConversation("a", "First"));
            _store.Seed(StoredConversation("b", "Second"));
            var model = CreateModel();
            model.Run();

            var resolved = model.ResolveConversationToOpen("a");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Id, Is.EqualTo("a"));
        }

        [Test]
        public void ResolveConversationToOpen_FallsBackToMostRecentConversation()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();

            var resolved = model.ResolveConversationToOpen("deleted-id");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Id, Is.EqualTo("a"));
        }

        [Test]
        public void ResolveConversationToOpen_ReturnsNullWhenNothingIsStored()
        {
            var model = CreateModel();
            model.Run();

            Assert.That(model.ResolveConversationToOpen(""), Is.Null);
        }

        [Test]
        public void ModeAndPhase_RaisePropertyChanged()
        {
            var model = CreateModel();
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.Mode = AgentMode.Act;
            model.Phase = AgentPhase.Streaming;

            Assert.That(changed, Does.Contain(nameof(ChatModel.Mode)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.Phase)));
        }

        #endregion View mode and UI state

        #region Commands

        [Test]
        public void NewChatCommand_IsReachableThroughTheCommandDispatcher()
        {
            var model = CreateModel();
            model.Run();
            var previousId = model.ActiveConversationId;

            model.SendCommand("NewChatCommand");

            Assert.That(model.ActiveConversationId, Is.Not.EqualTo(previousId));
        }

        [Test]
        public void OpenConversationCommand_IsReachableThroughTheCommandDispatcher()
        {
            _store.Seed(StoredConversation("a", "First", new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "q" }));
            var model = CreateModel();
            model.Run();

            model.SendCommand(new List<object> { "OpenConversationCommand", "a" });

            Assert.That(model.ActiveConversationId, Is.EqualTo("a"));
        }

        [Test]
        public void DeleteConversationCommand_AcceptsAConversationInstance()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();
            var conversation = model.History[0];

            model.SendCommand(new List<object> { "DeleteConversationCommand", conversation });

            Assert.That(_store.Peek("a"), Is.Null);
        }

        [Test]
        public void CommandTargets_ArePublicParameterlessOrSingleParameter()
        {
            // BaseModel.SendCommand resolves the target by reflection: a private or overloaded
            // method would fail silently at runtime, so the surface is asserted here.
            var commandNames = new[]
            {
                "NewChatCommand",
                "OpenConversationCommand",
                "DeleteConversationCommand",
                "ClearHistoryCommand",
                "RefreshHistoryCommand"
            };

            foreach (var name in commandNames)
            {
                var methods = typeof(ChatModel).GetMethods()
                    .Where(m => m.Name == name && m.IsPublic && !m.IsStatic)
                    .ToArray();

                Assert.That(methods, Has.Length.EqualTo(1), name + " must be a single public instance method");

                var parameters = methods[0].GetParameters();
                Assert.That(parameters.Length, Is.LessThanOrEqualTo(1), name + " must take zero or one argument");
                if (parameters.Length == 0)
                    Assert.DoesNotThrow(() => methods[0].Invoke(CreateModel(), null),
                        name + " must be invocable without arguments (reflection does not apply default values)");
            }
        }

        #endregion Commands
    }
}
