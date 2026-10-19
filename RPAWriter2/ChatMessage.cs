using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2
{
    /// <summary>
    /// A single chat entry: a <see cref="Kind"/> (what the entry is), a typed
    /// <see cref="Payload"/> (its content), and optionally the model's "reasoning".
    /// Rendered per-kind by the chat list.
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private ChatMessageKind _kind;
        private ChatPayload _payload = new TextPayload();
        private string _reasoning = "";
        private string _mode = "";
        private bool _isThinking;
        private bool _isPartial;
        private bool _isLastMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>What kind of entry this is; drives the label and the renderer.</summary>
        public ChatMessageKind Kind
        {
            get => _kind;
            set
            {
                if (_kind != value)
                {
                    _kind = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Label));
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(SingleLineDisplayText));
                    OnPropertyChanged(nameof(Blocks));
                    OnPropertyChanged(nameof(KindName));
                }
            }
        }

        /// <summary>String form of <see cref="Kind"/>, for XAML data triggers.</summary>
        public string KindName => Kind.ToString();

        /// <summary>Typed content payload. <see cref="TextPayload"/> for text entries, <see cref="ToolCallPayload"/> for tool calls.</summary>
        public ChatPayload Payload
        {
            get => _payload;
            set
            {
                if (!ReferenceEquals(_payload, value))
                {
                    _payload = value ?? new TextPayload();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Text));
                    OnPropertyChanged(nameof(Label));
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(SingleLineDisplayText));
                    OnPropertyChanged(nameof(Blocks));
                }
            }
        }

        /// <summary>Plain text of the entry (proxies into a <see cref="TextPayload"/>). Empty for tool calls.</summary>
        public string Text
        {
            get => (Payload as TextPayload)?.Text ?? "";
            set
            {
                if (Payload is TextPayload current)
                {
                    if (current.Text != value)
                    {
                        current.Text = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(DisplayText));
                        OnPropertyChanged(nameof(SingleLineDisplayText));
                        OnPropertyChanged(nameof(Blocks));
                    }
                }
                else
                {
                    Payload = new TextPayload(value);
                }
            }
        }

        public string Reasoning
        {
            get => _reasoning;
            set { if (_reasoning != value) { _reasoning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReasoning)); OnPropertyChanged(nameof(ShowReasoning)); } }
        }

        /// <summary>Agent mode ("Plan"/"Act") this message was produced in; used for the UI label.</summary>
        public string Mode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(Blocks)); } }
        }

        /// <summary>True while the model is still generating; the reasoning control shows "Thinking…".</summary>
        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ReasoningHeader));
                    OnPropertyChanged(nameof(ShowReasoning));
                }
            }
        }

        /// <summary>True while the entry's text is still being streamed in.</summary>
        public bool IsPartial
        {
            get => _isPartial;
            set { if (_isPartial != value) { _isPartial = value; OnPropertyChanged(); } }
        }

        /// <summary>True when this is the final message of a completed agent turn; drives the finish highlight.</summary>
        public bool IsLastMessage
        {
            get => _isLastMessage;
            set { if (_isLastMessage != value) { _isLastMessage = value; OnPropertyChanged(); } }
        }

        /// <summary>When this entry was created (UTC).</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

        /// <summary>True when the reasoning expander should be visible: while thinking or when reasoning exists.</summary>
        public bool ShowReasoning => IsThinking || HasReasoning;

        /// <summary>Header of the reasoning expander: "Thinking…" while streaming, "Reasoning" when done.</summary>
        public string ReasoningHeader => IsThinking ? "Thinking…" : "Reasoning";

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case ChatMessageKind.User: return "You: ";
                    case ChatMessageKind.AgentText: return string.IsNullOrWhiteSpace(Mode) ? "Agent: " : "Agent [" + Mode + "]: ";
                    case ChatMessageKind.Error: return "⚠ Extension error: ";
                    default: return "";
                }
            }
        }

        public string DisplayText
        {
            get
            {
                if (Kind == ChatMessageKind.ToolCall || Kind == ChatMessageKind.ToolBlocked)
                {
                    var p = Payload as ToolCallPayload;
                    var name = p?.ToolName ?? "";
                    if (Kind == ChatMessageKind.ToolBlocked)
                        return "⛔ " + name + " blocked in Plan mode.";
                    return "⚙ " + name + " → " + (p?.Result ?? "");
                }

                if (Kind == ChatMessageKind.Question)
                {
                    var q = Payload as QuestionPayload;
                    if (q == null) return Text ?? "";
                    return string.IsNullOrEmpty(q.Answer) ? q.Question : q.Question + " → " + q.Answer;
                }

                if (Kind == ChatMessageKind.Subagent)
                {
                    var s = Payload as SubagentPayload;
                    var prompt = s?.Item?.Prompt ?? "";
                    return "⚙ spawn_agent → " + (string.IsNullOrWhiteSpace(prompt) ? "(sub-agent)" : prompt);
                }

                var t = Text ?? "";
                return string.IsNullOrEmpty(Label) ? t : Label + t;
            }
        }

        /// <summary>Single-line form of <see cref="DisplayText"/> with line breaks collapsed to spaces; used by the collapsed pinned header.</summary>
        public string SingleLineDisplayText => DisplayText.Replace('\r', ' ').Replace('\n', ' ').Trim();

        /// <summary>Message content split into text and code blocks (for syntax-highlighted rendering).</summary>
        public IReadOnlyList<MessageBlock> Blocks => ChatMarkdown.SplitBlocks(DisplayText);

        /// <summary>Raw message text (without the role label) split into text and code blocks.</summary>
        public IReadOnlyList<MessageBlock> TextBlocks => ChatMarkdown.SplitBlocks(Text);

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
