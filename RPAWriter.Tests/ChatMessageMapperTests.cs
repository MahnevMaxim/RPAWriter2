using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Covers the transcript persistence mapping used by <c>ChatModel</c>: what is stored for each
    /// message kind and what comes back when a conversation is reopened.
    /// </summary>
    [TestFixture]
    public class ChatMessageMapperTests
    {
        [Test]
        public void TextMessage_KeepsKindTextReasoningAndMode()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.AgentText,
                Text = "answer",
                Reasoning = "thinking",
                Mode = "Act"
            };

            var stored = ChatMessageMapper.ToHistoryMessage(message);

            Assert.That(stored.Kind, Is.EqualTo(ChatMessageKind.AgentText));
            Assert.That(stored.Text, Is.EqualTo("answer"));
            Assert.That(stored.Reasoning, Is.EqualTo("thinking"));
            Assert.That(stored.Mode, Is.EqualTo("Act"));
            Assert.That(stored.ToolName, Is.Empty);
        }

        [Test]
        public void CheckpointMessage_StoresOnlyTheCommitHash()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Checkpoint,
                Text = "derived text must not be stored",
                Payload = new CheckpointPayload { CommitHash = "abc123", Sequence = 3, HasDiff = true }
            };

            var stored = ChatMessageMapper.ToHistoryMessage(message);

            Assert.That(stored.CheckpointHash, Is.EqualTo("abc123"));
            Assert.That(stored.Text, Is.Empty);
        }

        [Test]
        public void QuestionMessage_StoresQuestionAsTextAndAnswerAsToolName()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload
                {
                    Question = "Which robot?",
                    Answer = "RPA-42",
                    Options = new List<string> { "RPA-41", "RPA-42" }
                }
            };

            var stored = ChatMessageMapper.ToHistoryMessage(message);

            Assert.That(stored.Text, Is.EqualTo("Which robot?"));
            Assert.That(stored.ToolName, Is.EqualTo("RPA-42"));
            Assert.That(stored.Options, Is.EqualTo(new[] { "RPA-41", "RPA-42" }));
        }

        [Test]
        public void QuestionMessage_RoundTripsAnswerOptions()
        {
            var original = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload
                {
                    Question = "Which robot?",
                    Answer = "RPA-42",
                    Options = new List<string> { "RPA-41", "RPA-42" }
                }
            };

            var restored = ChatMessageMapper.FromHistoryMessage(ChatMessageMapper.ToHistoryMessage(original));
            var payload = restored.Payload as QuestionPayload;

            Assert.That(payload, Is.Not.Null);
            Assert.That(payload.Question, Is.EqualTo("Which robot?"));
            Assert.That(payload.Answer, Is.EqualTo("RPA-42"));
            Assert.That(payload.IsSkipped, Is.False);
            Assert.That(payload.Options, Is.EqualTo(new[] { "RPA-41", "RPA-42" }));
        }

        [Test]
        public void SkippedQuestion_RoundTripsAsSkipped()
        {
            var original = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload
                {
                    Question = "Which robot?",
                    Answer = ChatMessageMapper.SkipAnswerText,
                    IsSkipped = true
                }
            };

            var payload = ChatMessageMapper.FromHistoryMessage(ChatMessageMapper.ToHistoryMessage(original)).Payload as QuestionPayload;

            Assert.That(payload, Is.Not.Null);
            Assert.That(payload.IsSkipped, Is.True);
        }

        [Test]
        public void ToolCallMessage_StoresResultAsTextAndToolNameSeparately()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.ToolCall,
                Payload = new ToolCallPayload { ToolName = "read_file", Result = "file content" }
            };

            var stored = ChatMessageMapper.ToHistoryMessage(message);

            Assert.That(stored.ToolName, Is.EqualTo("read_file"));
            Assert.That(stored.Text, Is.EqualTo("file content"));
        }

        [Test]
        public void ToolBlockedMessage_RoundTripsAsBlocked()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.ToolBlocked,
                Payload = new ToolCallPayload { ToolName = "write_file", Result = "blocked in Plan mode", Blocked = true }
            };

            var restored = ChatMessageMapper.FromHistoryMessage(ChatMessageMapper.ToHistoryMessage(message));
            var payload = restored.Payload as ToolCallPayload;

            Assert.That(restored.Kind, Is.EqualTo(ChatMessageKind.ToolBlocked));
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload.ToolName, Is.EqualTo("write_file"));
            Assert.That(payload.Blocked, Is.True);
        }

        [Test]
        public void CheckpointMessage_RoundTripsCommitHash()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Checkpoint,
                Payload = new CheckpointPayload { CommitHash = "deadbeef" }
            };

            var restored = ChatMessageMapper.FromHistoryMessage(ChatMessageMapper.ToHistoryMessage(message));

            Assert.That(restored.Kind, Is.EqualTo(ChatMessageKind.Checkpoint));
            Assert.That(((CheckpointPayload)restored.Payload).CommitHash, Is.EqualTo("deadbeef"));
        }

        [Test]
        public void ToHistoryMessages_PreservesOrderAndSkipsNulls()
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Kind = ChatMessageKind.User, Text = "one" },
                null,
                new ChatMessage { Kind = ChatMessageKind.AgentText, Text = "two" }
            };

            var stored = ChatMessageMapper.ToHistoryMessages(messages);

            Assert.That(stored.Select(m => m.Text), Is.EqualTo(new[] { "one", "two" }));
        }

        [Test]
        public void ToHistoryMessage_RejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => ChatMessageMapper.ToHistoryMessage(null));
            Assert.Throws<System.ArgumentNullException>(() => ChatMessageMapper.FromHistoryMessage(null));
            Assert.Throws<System.ArgumentNullException>(() => ChatMessageMapper.ToHistoryMessages(null));
        }

        [Test]
        public void BuildTitle_UsesFallbackForEmptyText()
        {
            Assert.That(ChatMessageMapper.BuildTitle(null), Is.EqualTo("New chat"));
            Assert.That(ChatMessageMapper.BuildTitle("   "), Is.EqualTo("New chat"));
        }

        [Test]
        public void BuildTitle_CollapsesLineBreaks()
        {
            Assert.That(ChatMessageMapper.BuildTitle("first line\r\nsecond line"), Is.EqualTo("first line  second line"));
            Assert.That(ChatMessageMapper.BuildTitle("  padded  "), Is.EqualTo("padded"));
        }

        [Test]
        public void BuildTitle_TruncatesToSixtyCharacters()
        {
            var title = ChatMessageMapper.BuildTitle(new string('a', 100));

            Assert.That(title, Has.Length.EqualTo(ChatMessageMapper.TitleMaxLength + 1));
            Assert.That(title, Does.EndWith("…"));
            Assert.That(title.StartsWith(new string('a', ChatMessageMapper.TitleMaxLength)), Is.True);
        }

        [Test]
        public void BuildTitle_KeepsTextThatFits()
        {
            var text = new string('a', ChatMessageMapper.TitleMaxLength);

            Assert.That(ChatMessageMapper.BuildTitle(text), Is.EqualTo(text));
        }
    }
}
