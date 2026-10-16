using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RPAWriter2
{
    /// <summary>A chat message from the agent: role, text, and (optionally) the model's "reasoning".</summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _role = "";
        private string _text = "";
        private string _reasoning = "";
        private string _mode = "";
        private bool _isThinking;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Role: "user" | "agent" | "system".</summary>
        public string Role
        {
            get => _role;
            set { if (_role != value) { _role = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(DisplayText)); } }
        }

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); } }
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
            set { if (_mode != value) { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(DisplayText)); } }
        }

        /// <summary>True while the model is still generating; the reasoning control shows "Thinking…".</summary>
        public bool IsThinking
        {
            get => _isThinking;
            set { if (_isThinking != value) { _isThinking = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningHeader)); OnPropertyChanged(nameof(ShowReasoning)); } }
        }

        public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

        /// <summary>True when the reasoning expander should be visible: while thinking or when reasoning exists.</summary>
        public bool ShowReasoning => IsThinking || HasReasoning;

        /// <summary>Header of the reasoning expander: "Thinking…" while streaming, "Reasoning" when done.</summary>
        public string ReasoningHeader => IsThinking ? "Thinking…" : "Reasoning";

        public string Label
        {
            get
            {
                switch (Role)
                {
                    case "user": return "You: ";
                    case "agent": return string.IsNullOrWhiteSpace(Mode) ? "Agent: " : "Agent [" + Mode + "]: ";
                    default: return "";
                }
            }
        }

        public string DisplayText => string.IsNullOrEmpty(Label) ? (Text ?? "") : Label + (Text ?? "");

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
