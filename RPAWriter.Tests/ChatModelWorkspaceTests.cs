using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Models;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ChatModelWorkspaceTests
    {
        private static ChatConversation Conversation(string id, string title) =>
            new ChatConversation { Id = id, Title = title };

        [Test]
        public void Run_SetsHistoryStoreWorkspaceIdFromProvider()
        {
            var store = new InMemoryChatHistoryStore();
            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new ChatModel(store, null, workspaceProvider: provider);

            model.Run();

            Assert.That(store.WorkspaceId, Is.EqualTo("ws1"));
        }

        [Test]
        public void Run_WithoutWorkspace_SetsEmptyScope()
        {
            var store = new InMemoryChatHistoryStore();
            var model = new ChatModel(store);

            model.Run();

            Assert.That(store.WorkspaceId, Is.Empty);
        }

        [Test]
        public void SetWorkspaceScope_ReloadsHistoryForNewScope()
        {
            var store = new InMemoryChatHistoryStore();
            store.Seed(Conversation("a", "Workspace A"), "ws1");
            store.Seed(Conversation("b", "Workspace B"), "ws2");

            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new ChatModel(store, null, workspaceProvider: provider);
            model.Run();

            Assert.That(model.History.Select(c => c.Id), Is.EqualTo(new[] { "a" }));

            model.SetWorkspaceScope("ws2");

            Assert.That(store.WorkspaceId, Is.EqualTo("ws2"));
            Assert.That(model.History.Select(c => c.Id), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void SetWorkspaceScope_ClosesActiveChat()
        {
            var store = new InMemoryChatHistoryStore();
            var model = new ChatModel(store);
            model.Run();

            model.StartNewChat();
            Assert.That(model.HasActiveConversation, Is.True);

            model.SetWorkspaceScope("ws1");

            Assert.That(model.HasActiveConversation, Is.False);
        }

        [Test]
        public void History_ShowsLegacyConversationsAlongsideWorkspaceOnes()
        {
            var store = new InMemoryChatHistoryStore();
            store.Seed(Conversation("legacy", "Legacy"), "");
            store.Seed(Conversation("ws1", "Workspace 1"), "ws1");

            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new ChatModel(store, null, workspaceProvider: provider);
            model.Run();

            Assert.That(model.History.Select(c => c.Id), Is.EquivalentTo(new[] { "legacy", "ws1" }));
        }

        [Test]
        public void SaveConversation_AssignsActiveWorkspace()
        {
            var store = new InMemoryChatHistoryStore();
            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new ChatModel(store, null, workspaceProvider: provider);
            model.Run();

            model.StartNewChat();
            var id = model.ActiveChatConversation.Id;
            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = "hello" });
            model.SaveHistoryNow();

            Assert.That(store.Peek(id), Is.Not.Null);
            Assert.That(model.History.Select(c => c.Id), Does.Contain(id));

            // The conversation belongs to ws1, not the legacy/global scope, so it is hidden in ws2.
            model.SetWorkspaceScope("ws2");
            Assert.That(model.History.Select(c => c.Id), Does.Not.Contain(id));
        }

        [Test]
        public void SetWorkspaceScope_SameIdIsANoOp_KeepsActiveChatAndView()
        {
            var store = new InMemoryChatHistoryStore();
            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new ChatModel(store, null, workspaceProvider: provider);
            model.Run();
            model.StartNewChat();
            model.AppendMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = "in progress" });
            var id = model.ActiveConversationId;

            // A spurious solution event that reports the same scope must not close the chat or
            // flip it back to the history view.
            model.SetWorkspaceScope("ws1");

            Assert.That(model.HasActiveConversation, Is.True);
            Assert.That(model.ActiveConversationId, Is.EqualTo(id));
            Assert.That(model.Messages.Select(m => m.Text), Is.EqualTo(new[] { "in progress" }));
            Assert.That(model.ViewMode, Is.EqualTo(ChatViewMode.Conversation));
        }
    }
}
