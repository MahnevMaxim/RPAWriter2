using System;
using System.Collections.Generic;
using System.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Converts chat entries to and from the persisted history shape
    /// (<see cref="ChatMessage"/> &lt;-&gt; <see cref="ChatHistoryMessage"/>).
    /// <para>
    /// The mapping is pure data transformation, so it lives in Core next to the domain types:
    /// both the chat model (headless, testable) and the view host use the same rules, and the
    /// round-trip is covered by <c>ChatMessageMapperTests</c>.
    /// </para>
    /// </summary>
    public static class ChatMessageMapper
    {
        /// <summary>
        /// Stored answer value meaning "the user dismissed the question". Kept as a string in the
        /// history so old conversations keep loading after the flag was introduced.
        /// </summary>
        public const string SkipAnswerText = "User skipped the question.";

        /// <summary>Maximum length of the generated conversation title.</summary>
        public const int TitleMaxLength = 60;

        /// <summary>
        /// Maps a live chat entry to the persisted history entry. Only data that cannot be
        /// recomputed is stored: checkpoint payloads keep the commit hash, the rest of the
        /// checkpoint indicators are rebuilt from the shadow git repository on reload.
        /// </summary>
        public static ChatHistoryMessage ToHistoryMessage(ChatMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (message.Kind == ChatMessageKind.Checkpoint && message.Payload is CheckpointPayload checkpoint)
            {
                return new ChatHistoryMessage
                {
                    Kind = message.Kind,
                    Text = "",
                    CheckpointHash = checkpoint.CommitHash ?? "",
                    Reasoning = message.Reasoning,
                    Mode = message.Mode
                };
            }

            if (message.Payload is QuestionPayload question)
            {
                return new ChatHistoryMessage
                {
                    Kind = message.Kind,
                    Text = question.Question ?? "",
                    ToolName = question.Answer ?? "",
                    Options = question.Options?.ToList() ?? new List<string>(),
                    Reasoning = message.Reasoning,
                    Mode = message.Mode
                };
            }

            var tool = message.Payload as ToolCallPayload;
            return new ChatHistoryMessage
            {
                Kind = message.Kind,
                Text = tool != null ? tool.Result : message.Text,
                ToolName = tool?.ToolName ?? "",
                Reasoning = message.Reasoning,
                Mode = message.Mode
            };
        }

        /// <summary>Maps a whole transcript, preserving order.</summary>
        public static List<ChatHistoryMessage> ToHistoryMessages(IEnumerable<ChatMessage> messages)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            return messages.Where(m => m != null).Select(ToHistoryMessage).ToList();
        }

        /// <summary>Rehydrates a persisted history entry into a live chat entry.</summary>
        public static ChatMessage FromHistoryMessage(ChatHistoryMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (message.Kind == ChatMessageKind.Checkpoint)
            {
                return new ChatMessage
                {
                    Kind = message.Kind,
                    Reasoning = message.Reasoning,
                    Mode = message.Mode,
                    Payload = new CheckpointPayload { CommitHash = message.CheckpointHash ?? "" }
                };
            }

            if (message.Kind == ChatMessageKind.Question)
            {
                return new ChatMessage
                {
                    Kind = message.Kind,
                    Payload = new QuestionPayload
                    {
                        Question = message.Text ?? "",
                        Answer = message.ToolName ?? "",
                        IsSkipped = string.Equals(message.ToolName, SkipAnswerText, StringComparison.Ordinal),
                        Options = message.Options ?? new List<string>()
                    }
                };
            }

            if (message.Kind == ChatMessageKind.ToolCall || message.Kind == ChatMessageKind.ToolBlocked)
            {
                return new ChatMessage
                {
                    Kind = message.Kind,
                    Payload = new ToolCallPayload
                    {
                        ToolName = message.ToolName ?? "",
                        Result = message.Text ?? "",
                        Blocked = message.Kind == ChatMessageKind.ToolBlocked
                    }
                };
            }

            return new ChatMessage { Kind = message.Kind, Text = message.Text, Reasoning = message.Reasoning, Mode = message.Mode };
        }

        /// <summary>
        /// Builds the title shown on a history card from the first user message:
        /// single line, trimmed, truncated to <see cref="TitleMaxLength"/> with an ellipsis.
        /// </summary>
        public static string BuildTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "New chat";

            var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= TitleMaxLength
                ? singleLine
                : singleLine.Substring(0, TitleMaxLength) + "…";
        }
    }
}
