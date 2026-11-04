using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Models;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Guards the chat-history persistence invariants: a saved conversation is never silently
    /// truncated, compaction replaces only the visible transcript, and opening a conversation does
    /// not clobber the active one.
    /// </summary>
    [TestFixture]
    public class ChatModelPersistenceTests
    {
        private InMemoryChatHistoryStore _store;
        private InMemoryUiStateStore _uiState;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryChatHistoryStore();
            _uiState = new InMemoryUiStateStore();
        }

        private ChatModel CreateModel(bool withActiveChat = true)
        {
            var model = new ChatModel(_store, _uiState);
            if (withActiveChat)
                model.EnsureActiveConversation();
            return model;
        }

        private static ChatMessage User(string text) => new ChatMessage { Kind = ChatMessageKind.User, Text = text };
        private static ChatMessage Agent(string text) => new ChatMessage { Kind = ChatMessageKind.AgentText, Text = text };

        [Test]
        public void ApplyCompactedTranscript_ReplacesOnlyTheVisibleTranscript()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(User("first"));
            model.AppendMessage(Agent("answer one"));
            model.AppendMessage(User("second"));
            model.AppendMessage(Agent("answer two"));
            var id = model.ActiveConversationId;
            model.SaveHistoryNow();

            model.ApplyCompactedTranscript(new[] { User("summary"), Agent("recent") });

            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "summary", "recent" }));

            model.SaveHistoryNow();
            Assert.That(_store.Peek(id).Messages.Select(m => m.Text),
                Is.EqualTo(new[] { "first", "answer one", "second", "answer two" }),
                "compaction must not shrink the persisted history");
        }

        [Test]
        public void ApplyCompactedTranscript_WithNullIsANoOp()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(User("hello"));

            model.ApplyCompactedTranscript(null);

            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "hello" }));
        }

        [Test]
        public void SaveHistoryNow_DoesNotShrinkAStoredConversation()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(User("one"));
            model.AppendMessage(Agent("two"));
            var id = model.ActiveConversationId;
            model.SaveHistoryNow();
            Assert.That(_store.Peek(id).Messages.Count, Is.EqualTo(2));

            // A save after the visible transcript was replaced by a shorter one keeps the full tail.
            model.ApplyCompactedTranscript(new[] { Agent("recent") });
            model.SaveHistoryNow();

            Assert.That(_store.Peek(id).Messages.Count, Is.EqualTo(2));
            Assert.That(_store.Peek(id).Messages.Select(m => m.Text), Is.EqualTo(new[] { "one", "two" }));
        }

        [Test]
        public void OpenConversation_MissingTargetDoesNotClobberActiveChat()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(User("active message"));
            var id = model.ActiveConversationId;
            model.SaveHistoryNow();

            var opened = model.OpenConversation("missing-id");

            Assert.That(opened, Is.False);
            Assert.That(model.ActiveConversationId, Is.EqualTo(id));
            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "active message" }));
            Assert.That(_store.Peek(id).Messages.Select(m => m.Text), Is.EqualTo(new[] { "active message" }));
        }

        [Test]
        public void OpenConversation_SameIdIsANoOp()
        {
            var model = CreateModel();
            model.Run();
            model.AppendMessage(User("hello"));
            var id = model.ActiveConversationId;

            var opened = model.OpenConversation(id);

            Assert.That(opened, Is.False);
            Assert.That(model.ActiveConversationId, Is.EqualTo(id));
            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "hello" }));
        }

        [Test]
        public void OpenConversation_LoadsTargetMessagesIntoTheFullHistory()
        {
            var stored = new ChatConversation
            {
                Id = "target",
                Title = "Target",
                Messages = new System.Collections.Generic.List<ChatHistoryMessage>
                {
                    new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "stored user" },
                    new ChatHistoryMessage { Kind = ChatMessageKind.AgentText, Text = "stored answer" }
                }
            };
            _store.Seed(stored);

            var model = CreateModel();
            model.Run();
            model.OpenConversation("target");
            model.AppendMessage(Agent("appended after open"));
            model.SaveHistoryNow();

            Assert.That(_store.Peek("target").Messages.Select(m => m.Text),
                Is.EqualTo(new[] { "stored user", "stored answer", "appended after open" }));
        }
    }
}
