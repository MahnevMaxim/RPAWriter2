using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Models;
using RPAWriter.Mvvm;
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

        private ChatModel CreateModel(bool withUiState = true, bool withActiveChat = true)
        {
            var model = new ChatModel(_store, withUiState ? _uiState : null);

            // Most scenarios start from an open chat; the "closed" state (no active conversation) is the
            // model's state right after construction and is asserted by its own tests.
            if (withActiveChat)
                model.EnsureActiveConversation();

            return model;
        }

        private ChatModel CreateModelWithClock(Func<DateTime> utcNow, bool withActiveChat = true)
        {
            var model = new ChatModel(_store, _uiState, null, null, null, utcNow);

            if (withActiveChat)
                model.EnsureActiveConversation();

            return model;
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
            var model = CreateModel(withActiveChat: false);

            Assert.That(model.ViewModel, Is.Null, "a model must start without a view model");
            Assert.That(model.Messages, Is.Empty);
            Assert.That(model.Pending, Is.Empty);
            Assert.That(model.Attachments, Is.Empty);
            Assert.That(model.History, Is.Empty);
            Assert.That(model.ActiveChatConversation, Is.Null, "a chat starts closed: no conversation is active");
            Assert.That(model.ActiveConversationId, Is.Empty);
            Assert.That(model.HasActiveConversation, Is.False);
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
            var model = CreateModel(withActiveChat: false);
            model.Run();
            model.AppendMessage(UserText("hello"));

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

            Assert.That(model.ActiveChatConversation, Is.Null, "deleting the active chat leaves no active chat");
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

        #region Closing the chat / reopening it

        [Test]
        public void CloseActiveChat_SavesTheTranscriptBeforeClosing()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("closed chat"));
            var id = model.ActiveConversationId;

            model.CloseActiveChat();

            var stored = _store.Peek(id);
            Assert.That(stored, Is.Not.Null, "closing must flush the transcript first");
            Assert.That(stored.Messages.Select(m => m.Text), Is.EqualTo(new[] { "closed chat" }));
        }

        [Test]
        public void CloseActiveChat_LeavesNoActiveConversation()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));
            model.Enqueue("queued");

            model.CloseActiveChat();

            Assert.That(model.ActiveChatConversation, Is.Null, "closing is expressed by null and nothing else");
            Assert.That(model.ActiveConversationId, Is.Empty);
            Assert.That(model.HasActiveConversation, Is.False);
            Assert.That(model.Messages, Is.Empty, "a closed chat owns no transcript");
            Assert.That(model.PinnedMessage, Is.Null);
            Assert.That(model.Pending, Is.Empty);
            Assert.That(model.IsDirty, Is.False);
        }

        [Test]
        public void CloseActiveChat_KeepsTheChatInHistoryAndDropsItsIndicator()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));
            var id = model.ActiveConversationId;
            model.SaveHistoryNow();
            Assert.That(model.History.Single(c => c.Id == id).IsActive, Is.True);

            model.CloseActiveChat();

            Assert.That(model.History.Select(c => c.Id), Contains.Item(id), "the closed chat stays reopenable");
            Assert.That(model.History.Count(c => c.IsActive), Is.Zero);
        }

        [Test]
        public void CloseActiveChat_SwitchesToTheHistoryView()
        {
            var model = CreateModel();

            model.CloseActiveChat();

            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.History));
            Assert.That(model.IsHistoryView, Is.True);
        }

        [Test]
        public void CloseActiveChat_WritesAnEmptyRememberedId()
        {
            var model = CreateModel();
            model.Run();

            model.CloseActiveChat();
            model.PersistUiState(MainTab.Chat);

            Assert.That(_uiState.State.LastConversationId, Is.Empty,
                "a closed chat must not be restored on the next start");
            Assert.That(_uiState.State.ChatViewMode, Is.EqualTo(ChatViewMode.History));
        }

        [Test]
        public void ShowHistoryView_KeepsTheConversationActive()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("hello"));
            var id = model.ActiveConversationId;

            model.ShowHistoryView();
            model.AppendMessage(AgentText("answer"));

            Assert.That(model.ActiveConversationId, Is.EqualTo(id), "browsing history is not closing the chat");

            model.SaveHistoryNow();
            Assert.That(_store.Peek(id).Messages.Select(m => m.Text), Is.EqualTo(new[] { "hello", "answer" }),
                "messages keep landing in the active chat while the history list is open");
        }

        [Test]
        public void EnsureActiveConversation_CreatesOneConversationOnly()
        {
            var model = CreateModel(withActiveChat: false);

            var first = model.EnsureActiveConversation();
            var second = model.EnsureActiveConversation();

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(model.HasActiveConversation, Is.True);
        }

        [Test]
        public void EnsureActiveConversation_ReopensAClosedChatWithAFreshConversation()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(UserText("old"));
            var closedId = model.ActiveConversationId;
            model.CloseActiveChat();

            var reopened = model.EnsureActiveConversation();

            Assert.That(reopened, Is.Not.Null);
            Assert.That(reopened.Id, Is.Not.EqualTo(closedId));
            Assert.That(model.Messages, Is.Empty);
        }

        [Test]
        public void EnsureActiveConversation_DropsWhatWasLeftWhileTheChatWasClosed()
        {
            var model = CreateModel(withActiveChat: false);
            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "leftover" });

            model.EnsureActiveConversation();

            Assert.That(model.Messages, Is.Empty, "a closed chat owns no transcript");
            Assert.That(model.IsDirty, Is.False);
        }

        #endregion Closing the chat / reopening it

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
        public void ResolveConversationToOpen_EmptyIdReturnsNullEvenWhenHistoryIsNotEmpty()
        {
            _store.Seed(StoredConversation("a", "First"));
            var model = CreateModel();
            model.Run();

            Assert.That(model.ResolveConversationToOpen(""), Is.Null,
                "an empty id means the chat was closed: nothing is guessed");
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

        [Test]
        public void CanResume_RequiresABreakpointAndAnIdlePhase()
        {
            var model = CreateModel();

            model.HasResumableRun = true;
            model.Phase = AgentPhase.Streaming;
            Assert.That(model.CanResume, Is.False, "an active turn hides the Resume button");

            model.Phase = AgentPhase.Idle;
            Assert.That(model.CanResume, Is.True);

            model.Phase = AgentPhase.Error;
            Assert.That(model.CanResume, Is.False, "after a failure the turn cannot be resumed");

            model.Phase = AgentPhase.Idle;
            model.HasResumableRun = false;
            Assert.That(model.CanResume, Is.False, "without a breakpoint there is nothing to resume");
        }

        [Test]
        public void CanResume_RaisesPropertyChangedWhenThePhaseChanges()
        {
            var model = CreateModel();
            model.HasResumableRun = true;
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.Phase = AgentPhase.Streaming;

            Assert.That(changed, Does.Contain(nameof(ChatModel.CanResume)));
        }

        [Test]
        public void HasResumableRun_DoesNotChangeThePhase()
        {
            var model = CreateModel();
            model.Phase = AgentPhase.Error;

            model.HasResumableRun = true;

            Assert.That(model.Phase, Is.EqualTo(AgentPhase.Error));
        }

        [Test]
        public void SwitchMode_RecordsTheSwitchInTheTranscript()
        {
            var model = CreateModel();
            model.Run();

            model.SwitchMode(AgentMode.Act);

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Act));
            var entry = model.Messages.Last();
            Assert.That(entry.Kind, Is.EqualTo(ChatMessageKind.ModeSwitch));
            Assert.That(entry.Text, Is.EqualTo("Mode: Act"));
            Assert.That(entry.Mode, Is.EqualTo("Act"));
            Assert.That(model.IsDirty, Is.True);
        }

        [Test]
        public void SwitchMode_RecordsTheSwitchWhenTheBindingAppliedTheModeFirst()
        {
            var model = CreateModel();
            model.Run();

            // The two-way toggle binding may write Mode before the host handles the event.
            model.Mode = AgentMode.Help;
            model.SwitchMode(AgentMode.Help);

            Assert.That(model.Messages.Count(m => m.Kind == ChatMessageKind.ModeSwitch), Is.EqualTo(1));
            Assert.That(model.Messages.Last().Text, Is.EqualTo("Mode: Help"));
        }

        [Test]
        public void SwitchMode_DoesNotTouchThePhase()
        {
            var model = CreateModel();
            model.Phase = AgentPhase.Streaming;

            model.SwitchMode(AgentMode.Help);

            Assert.That(model.Phase, Is.EqualTo(AgentPhase.Streaming));
        }

        [Test]
        public async Task DoItCommand_SwitchesToActAndSendsTheCommand()
        {
            var sink = new RecordingChatSink();
            var model = new ChatModel(_store, _uiState, null, sink);
            model.Run();

            await model.DoItCommand("");

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Act));
            Assert.That(sink.Prompts, Does.Contain("Do it!!!"));
            Assert.That(model.Messages.Count(m => m.Kind == ChatMessageKind.ModeSwitch), Is.EqualTo(1));
            Assert.That(model.Messages.Last(m => m.Kind == ChatMessageKind.ModeSwitch).Text, Is.EqualTo("Mode: Act"));
        }

        [Test]
        public async Task DoItCommand_PrependsInputTextToTheCommand()
        {
            var sink = new RecordingChatSink();
            var model = new ChatModel(_store, _uiState, null, sink);
            model.Run();

            await model.DoItCommand("fix the bug");

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Act));
            Assert.That(sink.Prompts, Does.Contain("fix the bug" + Environment.NewLine + "Do it!!!"));
        }

        [Test]
        public async Task DoItCommand_SendsBareCommandOnWhitespaceOnlyInput()
        {
            var sink = new RecordingChatSink();
            var model = new ChatModel(_store, _uiState, null, sink);
            model.Run();

            await model.DoItCommand("   ");

            Assert.That(sink.Prompts, Does.Contain("Do it!!!"));
        }

        [Test]
        public async Task DoItCommand_DoesNotRecordADuplicateWhenAlreadyAct()
        {
            var sink = new RecordingChatSink();
            var model = new ChatModel(_store, _uiState, null, sink);
            model.Run();

            model.SwitchMode(AgentMode.Act); // the user already switched to Act once
            await model.DoItCommand("");

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Act));
            Assert.That(model.Messages.Count(m => m.Kind == ChatMessageKind.ModeSwitch), Is.EqualTo(1));
            Assert.That(sink.Prompts, Does.Contain("Do it!!!"));
        }

        [Test]
        public async Task DoItCommand_SendsNothingWithoutASink()
        {
            var model = CreateModel();

            Assert.DoesNotThrowAsync(async () => await model.DoItCommand(""));

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Act));
            Assert.That(model.Messages.Count(m => m.Kind == ChatMessageKind.ModeSwitch), Is.EqualTo(1));
        }

        [Test]
        public async Task DoItCommand_ReportsSendingFailuresThroughNotify()
        {
            var sink = new RecordingChatSink { SendError = new Exception("boom") };
            var model = new ChatModel(_store, _uiState, null, sink);
            model.Run();
            var notifications = new List<Notify>();
            model.Notify += (sender, notify) => notifications.Add(notify);

            await model.DoItCommand("");

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Message, Does.Contain("boom"));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Error));
        }

        [Test]
        public void RestoreMode_SetsTheModeWithoutRecordingATranscriptEntry()
        {
            var model = CreateModel();
            model.Run();

            model.RestoreMode(AgentMode.Help);

            Assert.That(model.Mode, Is.EqualTo(AgentMode.Help));
            Assert.That(model.Messages.Count(m => m.Kind == ChatMessageKind.ModeSwitch), Is.EqualTo(0));
        }

        #region Turn status timer

        [Test]
        public void StartStatus_SetsStreamingPhaseAndInitialStatus()
        {
            var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = CreateModelWithClock(() => now);
            try
            {
                model.StartStatus();

                Assert.That(model.Phase, Is.EqualTo(AgentPhase.Streaming));
                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(0));
                Assert.That(model.StatusText, Is.EqualTo("Sending request… (0 s)"));
                Assert.That(model.StatusErrorText, Is.Empty);
            }
            finally
            {
                model.StopStatus();
            }
        }

        [Test]
        public void StopStatus_ReturnsToIdleAndClearsTheStatus()
        {
            var model = CreateModelWithClock(() => DateTime.UtcNow);
            model.StartStatus();

            model.StopStatus();

            Assert.That(model.Phase, Is.EqualTo(AgentPhase.Idle));
            Assert.That(model.StatusElapsedSeconds, Is.EqualTo(0));
            Assert.That(model.StatusText, Is.Empty);
            Assert.That(model.StatusErrorText, Is.Empty);
        }

        [Test]
        public void RefreshStatus_CountsTheElapsedSeconds()
        {
            var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = CreateModelWithClock(() => now);
            try
            {
                model.StartStatus();
                now = now.AddSeconds(5);

                model.RefreshStatus();

                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(5));
                Assert.That(model.StatusText, Is.EqualTo("Thinking… (5 s)"));
            }
            finally
            {
                model.StopStatus();
            }
        }

        [Test]
        public void PauseStatus_FreezesTheElapsedTimeWhileWaitingForTheAnswer()
        {
            var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = CreateModelWithClock(() => now);
            try
            {
                model.StartStatus();
                now = now.AddSeconds(10);
                model.RefreshStatus();

                model.PauseStatus();

                Assert.That(model.Phase, Is.EqualTo(AgentPhase.Streaming));
                Assert.That(model.StatusText, Is.EqualTo("Waiting for answer…"));
                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(10));

                now = now.AddSeconds(60);
                model.RefreshStatus();

                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(10), "the timer must stay frozen while the question is pending");
            }
            finally
            {
                model.StopStatus();
            }
        }

        [Test]
        public void ResumeStatus_ContinuesFromThePausedElapsedTime()
        {
            var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = CreateModelWithClock(() => now);
            try
            {
                model.StartStatus();
                now = now.AddSeconds(10);
                model.RefreshStatus();
                model.PauseStatus();
                now = now.AddSeconds(60);

                model.ResumeStatus();

                Assert.That(model.Phase, Is.EqualTo(AgentPhase.Streaming));
                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(10), "the waiting time must not count into the elapsed");

                now = now.AddSeconds(3);
                model.RefreshStatus();
                Assert.That(model.StatusElapsedSeconds, Is.EqualTo(13));
            }
            finally
            {
                model.StopStatus();
            }
        }

        [Test]
        public void ShowStatusError_SetsErrorPhaseAndErrorText()
        {
            var model = CreateModelWithClock(() => DateTime.UtcNow);
            model.StartStatus();

            model.ShowStatusError("boom");

            Assert.That(model.Phase, Is.EqualTo(AgentPhase.Error));
            Assert.That(model.StatusErrorText, Is.EqualTo("Error: boom"));
            Assert.That(model.StatusText, Is.Empty);
            Assert.That(model.StatusElapsedSeconds, Is.EqualTo(0));
        }

        [Test]
        public void StatusProperties_RaisePropertyChanged()
        {
            var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var model = CreateModelWithClock(() => now);
            var changed = new List<string>();
            ((INotifyPropertyChanged)model).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.StartStatus();
            now = now.AddSeconds(3);
            model.RefreshStatus();
            model.PauseStatus();
            model.ResumeStatus();
            model.ShowStatusError("boom");
            model.StopStatus();

            Assert.That(changed, Does.Contain(nameof(ChatModel.StatusElapsedSeconds)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.StatusText)));
            Assert.That(changed, Does.Contain(nameof(ChatModel.StatusErrorText)));
        }

        #endregion Turn status timer

        #endregion View mode and UI state

        #region Context counters

        [Test]
        public void RecordTokenUsage_AccumulatesIntoActiveConversation()
        {
            var model = CreateModel();

            model.RecordTokenUsage(new TokenUsage
            {
                PromptTokens = 10,
                CompletionTokens = 5,
                TotalTokens = 15,
                CacheHitTokens = 4,
                CacheMissTokens = 6,
                EstimatedCostUsd = 0.001
            });

            Assert.That(model.ActiveChatConversation.Usage.TotalTokens, Is.EqualTo(15));
            Assert.That(model.ActiveChatConversation.Usage.CacheHitTokens, Is.EqualTo(4));
            Assert.That(model.ActiveChatConversation.Usage.EstimatedCostUsd, Is.EqualTo(0.001));
        }

        [Test]
        public void RecordTokenUsage_IsNoOpWhenChatIsClosed()
        {
            var model = CreateModel(withActiveChat: false);

            model.RecordTokenUsage(new TokenUsage { TotalTokens = 15 });

            Assert.That(model.ActiveChatConversation, Is.Null);
        }

        [Test]
        public void RecordTokenUsage_AccumulatesAcrossRequests()
        {
            var model = CreateModel();

            model.RecordTokenUsage(new TokenUsage { TotalTokens = 15 });
            model.RecordTokenUsage(new TokenUsage { TotalTokens = 7 });

            Assert.That(model.ActiveChatConversation.Usage.TotalTokens, Is.EqualTo(22));
        }

        [Test]
        public void SetEstimatedInputTokens_UpdatesActiveConversation()
        {
            var model = CreateModel();

            model.SetEstimatedInputTokens(1234);

            Assert.That(model.ActiveChatConversation.UsedTokens, Is.EqualTo(1234));
        }

        [Test]
        public void SetEstimatedInputTokens_IsNoOpWhenChatIsClosed()
        {
            var model = CreateModel(withActiveChat: false);

            model.SetEstimatedInputTokens(1234);

            Assert.That(model.ActiveChatConversation, Is.Null);
        }

        [Test]
        public void SetEstimatedInputTokens_ClampsNegativeToZero()
        {
            var model = CreateModel();

            model.SetEstimatedInputTokens(-5);

            Assert.That(model.ActiveChatConversation.UsedTokens, Is.Zero);
        }

        [Test]
        public void NewChat_StartsWithZeroCounters()
        {
            var model = CreateModel();

            model.RecordTokenUsage(new TokenUsage { TotalTokens = 42 });
            model.SetEstimatedInputTokens(999);
            model.StartNewChat();

            Assert.That(model.ActiveChatConversation.Usage.TotalTokens, Is.Zero);
            Assert.That(model.ActiveChatConversation.UsedTokens, Is.Zero);
        }

        [Test]
        public void SetContextWindow_MirrorsValuesIntoModel()
        {
            var model = CreateModel();

            model.SetContextWindow(409600, 348160, 360448);

            Assert.That(model.ContextWindowSize, Is.EqualTo(409600));
            Assert.That(model.CompactThresholdTokens, Is.EqualTo(348160));
            Assert.That(model.MaxInputTokens, Is.EqualTo(360448));
        }

        [Test]
        public void SetContextWindow_ClampsNegativeToZero()
        {
            var model = CreateModel();

            model.SetContextWindow(-1, -2, -3);

            Assert.That(model.ContextWindowSize, Is.Zero);
            Assert.That(model.CompactThresholdTokens, Is.Zero);
            Assert.That(model.MaxInputTokens, Is.Zero);
        }

        #endregion Context counters

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
                "RefreshHistoryCommand",
                "DoItCommand"
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
