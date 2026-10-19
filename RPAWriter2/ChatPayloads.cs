using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RPAWriter2
{
    /// <summary>
    /// Base type for the typed content payload attached to a <see cref="ChatMessage"/>.
    /// The chat list renders each payload type via its own DataTemplate (same pattern as
    /// <c>MessageBlock</c> → <c>TextMessageBlock</c>/<c>CodeMessageBlock</c>).
    /// </summary>
    public abstract class ChatPayload
    {
    }

    /// <summary>Plain markdown text — the common case for user and agent messages.</summary>
    public sealed class TextPayload : ChatPayload
    {
        public TextPayload()
        {
        }

        public TextPayload(string text)
        {
            Text = text ?? "";
        }

        public string Text { get; set; } = "";
    }

    /// <summary>A tool invocation: executed or blocked (in Plan mode).</summary>
    public sealed class ToolCallPayload : ChatPayload
    {
        public string ToolName { get; set; } = "";
        public string Result { get; set; } = "";
        public bool Blocked { get; set; }
    }

    /// <summary>
    /// An interactive question asked by the agent, with answer options. The chat list
    /// renders it as a message with answer buttons; <see cref="Answer"/> is set when the
    /// user picks an option (or skips/cancels), which disables the buttons.
    /// </summary>
    public sealed class QuestionPayload : ChatPayload, INotifyPropertyChanged
    {
        private string _answer = "";

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The question text.</summary>
        public string Question { get; set; } = "";

        /// <summary>The answer options shown as buttons (2..5 items).</summary>
        public IReadOnlyList<string> Options { get; set; } = new List<string>();

        /// <summary>The option selected by the user, or empty while unanswered.</summary>
        public string Answer
        {
            get => _answer;
            set
            {
                if (_answer != value)
                {
                    _answer = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAnswered));
                }
            }
        }

        /// <summary>True once the user answered (or skipped/cancelled) the question.</summary>
        public bool IsAnswered => !string.IsNullOrEmpty(Answer);

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
