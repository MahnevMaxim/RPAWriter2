using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Domain chat types moved from the VSIX project to RPAWriter.Core in phase B of the model
    /// layer refactor. They are pure data + formatting, so they are asserted here directly:
    /// no view model, no view, no Visual Studio (see documentation/13-model-layer.md).
    /// </summary>
    [TestFixture]
    public class ChatMessageTests
    {
        [Test]
        public void TextSetterCreatesTextPayload()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.User, Text = "hello" };

            Assert.That(message.Payload, Is.InstanceOf<TextPayload>());
            Assert.That(message.Text, Is.EqualTo("hello"));
        }

        [Test]
        public void LabelDependsOnKindAndMode()
        {
            Assert.That(new ChatMessage { Kind = ChatMessageKind.User }.Label, Is.EqualTo("You: "));
            Assert.That(new ChatMessage { Kind = ChatMessageKind.AgentText }.Label, Is.EqualTo("Agent: "));
            Assert.That(
                new ChatMessage { Kind = ChatMessageKind.ResultAnswerAct, Mode = "Act" }.Label,
                Is.EqualTo("Agent [Act]: "));
            Assert.That(new ChatMessage { Kind = ChatMessageKind.Error }.Label, Is.EqualTo("⚠ Extension error: "));
            Assert.That(new ChatMessage { Kind = ChatMessageKind.SystemNotice }.Label, Is.Empty);
        }

        [Test]
        public void DisplayTextPrependsLabelForTextMessages()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.User, Text = "hi" };

            Assert.That(message.DisplayText, Is.EqualTo("You: hi"));
            Assert.That(message.SingleLineDisplayText, Is.EqualTo("You: hi"));
        }

        [Test]
        public void DisplayTextCollapsesLineBreaksInSingleLineForm()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "a\r\n  b\nc" };

            Assert.That(message.SingleLineDisplayText, Does.Not.Contain("\n"));
            Assert.That(message.SingleLineDisplayText, Is.EqualTo("a    b c"));
        }

        [Test]
        public void DisplayTextDescribesToolCall()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.ToolCall,
                Payload = new ToolCallPayload { ToolName = "read_file", Result = "ok" }
            };

            Assert.That(message.DisplayText, Is.EqualTo("⚙ read_file → ok"));
            Assert.That(message.MessageTitle, Is.EqualTo("read_file"));
            Assert.That(message.ToolBodyText, Is.EqualTo("ok"));
        }

        [Test]
        public void DisplayTextDescribesBlockedToolCall()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.ToolBlocked,
                Payload = new ToolCallPayload { ToolName = "apply_fix", Result = "ignored" }
            };

            Assert.That(message.DisplayText, Is.EqualTo("⛔ apply_fix blocked in Plan mode."));
            Assert.That(message.MessageTitle, Is.EqualTo("apply_fix"));
            Assert.That(message.ToolBodyText, Is.EqualTo("blocked in Plan mode."));
        }

        [Test]
        public void DisplayTextDescribesQuestionPayload()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload { Question = "Continue?", Options = new[] { "Yes", "No" } }
            };

            Assert.That(message.DisplayText, Is.EqualTo("Continue?"));
        }

        [Test]
        public void DisplayTextDescribesSubagentPayload()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Subagent,
                Payload = new SubagentPayload { Item = new SubagentStatusItem { Prompt = "scan repo" } }
            };

            Assert.That(message.DisplayText, Is.EqualTo("⚙ spawn_agent → scan repo"));
        }

        [Test]
        public void DisplayTextDescribesCheckpointPayload()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Checkpoint,
                Payload = new CheckpointPayload
                {
                    CommitHash = "0123456789abcdef",
                    HasDiff = true,
                    HasChanges = true
                }
            };

            Assert.That(message.DisplayText, Is.EqualTo("checkpoint diff: 01234567 Restore"));
        }

        [Test]
        public void MessageTitleUsesFirstNonEmptyLineForUserMessages()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.User, Text = "\n\n  first  \nsecond" };

            Assert.That(message.MessageTitle, Is.EqualTo("first"));
        }

        [Test]
        public void MessageTitleIsReasoningForAgentText()
        {
            Assert.That(new ChatMessage { Kind = ChatMessageKind.AgentText }.MessageTitle, Is.EqualTo("Reasoning"));
        }

        [Test]
        public void ReasoningDisplayTextTrimsStreamingTail()
        {
            var message = new ChatMessage { Reasoning = new string('x', 9000), IsThinking = true };

            Assert.That(message.ReasoningDisplayText.Length, Is.EqualTo(ChatMessage.ReasoningStreamTailLength + 1));
            Assert.That(message.ReasoningDisplayText, Does.StartWith("…"));

            message.IsThinking = false;

            Assert.That(message.ReasoningDisplayText.Length, Is.EqualTo(9000));
            Assert.That(message.ShowReasoning, Is.True);
            Assert.That(message.ReasoningHeader, Is.EqualTo("Reasoning"));
        }

        [Test]
        public void ReasoningHeaderIsThinkingWhileStreaming()
        {
            var message = new ChatMessage { IsThinking = true };

            Assert.That(message.ReasoningHeader, Is.EqualTo("Thinking…"));
            Assert.That(message.HasReasoning, Is.False);
            Assert.That(message.ShowReasoning, Is.True);
        }

        [Test]
        public void BlocksSplitFencedCode()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.AgentText,
                Text = "text\n```csharp\nvar x = 1;\n```\nmore"
            };

            var blocks = message.TextBlocks;

            Assert.That(blocks, Has.Count.EqualTo(3));
            Assert.That(blocks[0], Is.InstanceOf<TextMessageBlock>());
            Assert.That(((TextMessageBlock)blocks[0]).Text.Trim(), Is.EqualTo("text"));
            Assert.That(blocks[1], Is.InstanceOf<CodeMessageBlock>());
            Assert.That(((CodeMessageBlock)blocks[1]).Language, Is.EqualTo("csharp"));
            Assert.That(((CodeMessageBlock)blocks[1]).Code, Is.EqualTo("var x = 1;"));
            Assert.That(blocks[2], Is.InstanceOf<TextMessageBlock>());
        }

        [Test]
        public void KindChangeRaisesPropertyChangedForDerivedText()
        {
            var message = new ChatMessage();
            var raised = new List<string>();
            message.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            message.Kind = ChatMessageKind.Error;

            Assert.That(raised, Contains.Item(nameof(ChatMessage.Label)));
            Assert.That(raised, Contains.Item(nameof(ChatMessage.DisplayText)));
            Assert.That(raised, Contains.Item(nameof(ChatMessage.KindName)));
        }

        [Test]
        public void TimestampToolTipContainsKindAndLocalTime()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.User,
                TimestampUtc = new DateTime(2024, 5, 6, 10, 0, 0, DateTimeKind.Utc)
            };

            Assert.That(message.TimestampToolTip, Does.StartWith("User · "));
        }
    }

    [TestFixture]
    public class ChatPayloadTests
    {
        [Test]
        public void QuestionPayloadTracksAnsweredState()
        {
            var payload = new QuestionPayload { Options = new[] { "Yes", "No" } };

            Assert.That(payload.IsAnswered, Is.False);
            Assert.That(payload.IsFreeTextAnswer, Is.False);

            payload.Answer = "Yes";

            Assert.That(payload.IsAnswered, Is.True);
            Assert.That(payload.IsFreeTextAnswer, Is.False);
        }

        [Test]
        public void QuestionPayloadDetectsFreeTextAnswer()
        {
            var payload = new QuestionPayload { Options = new[] { "Yes", "No" }, Answer = "Maybe" };

            Assert.That(payload.IsFreeTextAnswer, Is.True);
        }

        [Test]
        public void QuestionPayloadSkippedIsNotFreeText()
        {
            var payload = new QuestionPayload { Options = new[] { "Yes", "No" }, Answer = "Maybe", IsSkipped = true };

            Assert.That(payload.IsFreeTextAnswer, Is.False);
        }

        [Test]
        public void SubagentStatusTextReflectsStatusAndToolCalls()
        {
            var item = new SubagentStatusItem { Status = "running", ToolCalls = 2 };

            Assert.That(item.StatusText, Is.EqualTo("Running · 2 tool call(s)…"));
            Assert.That(item.HasOutput, Is.False);

            item.Status = "completed";

            Assert.That(item.StatusText, Is.EqualTo("Completed · 2 tool call(s)"));
            Assert.That(item.HasOutput, Is.True);
        }

        [Test]
        public void SubagentFailedReportsError()
        {
            var item = new SubagentStatusItem { Status = "failed", Error = "boom" };

            Assert.That(item.HasError, Is.True);
            Assert.That(item.StatusText, Is.EqualTo("Failed"));
        }

        [Test]
        public void CheckpointPayloadFormatsShortHashAndDetail()
        {
            var payload = new CheckpointPayload
            {
                CommitHash = "0123456789abcdef",
                CreatedAtUtc = new DateTime(2024, 5, 6, 10, 0, 0, DateTimeKind.Utc)
            };

            Assert.That(payload.ShortHash, Is.EqualTo("01234567"));
            Assert.That(payload.DisplayTitle, Is.EqualTo("Checkpoint"));
            Assert.That(payload.Detail, Does.StartWith("checkpoint 01234567 · "));
        }

        [Test]
        public void CheckpointPayloadRaisesChangeNotifications()
        {
            var payload = new CheckpointPayload();
            var raised = new List<string>();
            payload.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            payload.HasChanges = true;

            Assert.That(raised, Contains.Item(nameof(CheckpointPayload.HasChanges)));
        }
    }

    [TestFixture]
    public class ChatAttachmentTests
    {
        [Test]
        public void ImageAttachmentMapsToImagePart()
        {
            var attachment = new ChatAttachment
            {
                FileName = "screen.png",
                IsImage = true,
                MediaType = "image/png",
                DataBase64 = "AAAA"
            };

            Assert.That(attachment.DisplayText, Is.EqualTo("Image: screen.png"));

            var part = attachment.ToImagePart();

            Assert.That(part.MediaType, Is.EqualTo("image/png"));
            Assert.That(part.DataBase64, Is.EqualTo("AAAA"));
        }

        [Test]
        public void TextAttachmentDisplayTextMentionsFile()
        {
            var attachment = new ChatAttachment { FileName = "notes.txt", TextContent = "abc" };

            Assert.That(attachment.DisplayText, Is.EqualTo("File: notes.txt"));
        }
    }

    [TestFixture]
    public class ChatConversationTests
    {
        [Test]
        public void EmptyConversationUsesPlaceholders()
        {
            var conversation = new ChatConversation();

            Assert.That(conversation.CardTitle, Is.EqualTo("(empty)"));
            Assert.That(conversation.DisplayTitle, Is.EqualTo("(empty)"));
            Assert.That(conversation.DisplayDate, Is.Empty);
        }

        [Test]
        public void DisplayTitleCombinesTitleAndLocalDate()
        {
            var conversation = new ChatConversation
            {
                Title = "Refactor chat",
                UpdatedAtUtc = new DateTime(2024, 5, 6, 10, 0, 0, DateTimeKind.Utc)
            };

            Assert.That(conversation.DisplayTitle, Does.StartWith("Refactor chat  ·  "));
            Assert.That(conversation.CardTitle, Is.EqualTo("Refactor chat"));
            Assert.That(conversation.DisplayDate, Is.Not.Empty);
        }

        [Test]
        public void IsActiveRaisesPropertyChanged()
        {
            var conversation = new ChatConversation();
            var raised = new List<string>();
            conversation.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            conversation.IsActive = true;

            Assert.That(raised, Contains.Item(nameof(ChatConversation.IsActive)));
            Assert.That(conversation.IsActive, Is.True);
        }

        [Test]
        public void TitleChangeRefreshesDisplayText()
        {
            var conversation = new ChatConversation();
            var raised = new List<string>();
            conversation.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            conversation.Title = "New title";

            Assert.That(raised, Contains.Item(nameof(ChatConversation.DisplayTitle)));
            Assert.That(raised, Contains.Item(nameof(ChatConversation.CardTitle)));
        }

        [Test]
        public void MessagesStartEmpty()
        {
            Assert.That(new ChatConversation().Messages, Is.Not.Null);
            Assert.That(new ChatConversation().Messages, Is.Empty);
        }
    }

    [TestFixture]
    public class PendingMessageTests
    {
        [Test]
        public void PreviewCollapsesLineBreaks()
        {
            var message = new PendingMessage { Text = "a\r\n  b  " };

            Assert.That(message.Preview, Is.EqualTo("a    b"));
        }

        [Test]
        public void PreviewFallsBackToAttachmentHint()
        {
            Assert.That(new PendingMessage().Preview, Is.EqualTo("(attachments only)"));
        }

        [Test]
        public void AttachmentSummaryShowsCount()
        {
            var message = new PendingMessage
            {
                Attachments = new[] { new ChatAttachment { FileName = "a.png" } }
            };

            Assert.That(message.HasAttachments, Is.True);
            Assert.That(message.AttachmentSummary, Is.EqualTo("📎 1"));
        }

        [Test]
        public void NoAttachmentsProducesEmptySummary()
        {
            var message = new PendingMessage();

            Assert.That(message.HasAttachments, Is.False);
            Assert.That(message.AttachmentSummary, Is.Empty);
        }

        [Test]
        public void DeliveryDefaultsToQueueAndSwitchesToSteer()
        {
            var message = new PendingMessage();

            Assert.That(message.Delivery, Is.EqualTo(PendingDelivery.Queue));
            Assert.That(message.DeliveryToolTip, Does.StartWith("Queue:"));

            message.Delivery = PendingDelivery.Steer;

            Assert.That(message.DeliveryToolTip, Does.StartWith("Steer:"));
        }

        [Test]
        public void DeliveryChangeRaisesPropertyChanged()
        {
            var message = new PendingMessage();
            var raised = new List<string>();
            message.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            message.Delivery = PendingDelivery.Steer;

            Assert.That(raised, Contains.Item(nameof(PendingMessage.Delivery)));
            Assert.That(raised, Contains.Item(nameof(PendingMessage.DeliveryToolTip)));
        }

        [Test]
        public void EachMessageGetsUniqueId()
        {
            var ids = Enumerable.Range(0, 5).Select(_ => new PendingMessage().Id).ToList();

            Assert.That(ids.Distinct().Count(), Is.EqualTo(5));
        }
    }

    [TestFixture]
    public class UiStateDataTests
    {
        [Test]
        public void DefaultsAreFirstEnumMembers()
        {
            var state = new UiStateData();

            Assert.That(state.ChatViewMode, Is.EqualTo(ChatViewMode.Conversation));
            Assert.That(state.AgentMode, Is.EqualTo(AgentMode.Plan));
            Assert.That(state.ActiveTab, Is.EqualTo(MainTab.Chat));
            Assert.That(state.LastConversationId, Is.Empty);
        }

        [Test]
        public void SerializesEnumsAsNames()
        {
            var state = new UiStateData
            {
                ChatViewMode = ChatViewMode.History,
                AgentMode = AgentMode.Act,
                ActiveTab = MainTab.KnowledgeBase,
                LastConversationId = "abc"
            };

            var json = JsonConvert.SerializeObject(state);

            Assert.That(json, Does.Contain("\"History\""));
            Assert.That(json, Does.Contain("\"Act\""));
            Assert.That(json, Does.Contain("\"Knowledge base\""));
        }

        [Test]
        public void RoundTripsThroughJson()
        {
            var state = new UiStateData
            {
                ChatViewMode = ChatViewMode.History,
                AgentMode = AgentMode.Help,
                ActiveTab = MainTab.Packages,
                LastConversationId = "xyz"
            };

            var restored = JsonConvert.DeserializeObject<UiStateData>(JsonConvert.SerializeObject(state));

            Assert.That(restored.ChatViewMode, Is.EqualTo(ChatViewMode.History));
            Assert.That(restored.AgentMode, Is.EqualTo(AgentMode.Help));
            Assert.That(restored.ActiveTab, Is.EqualTo(MainTab.Packages));
            Assert.That(restored.LastConversationId, Is.EqualTo("xyz"));
        }
    }
}
