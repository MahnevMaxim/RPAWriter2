namespace RPAWriter2
{
    /// <summary>A chat message from the agent: role, text, and (optionally) the model's "reasoning".</summary>
    public class ChatMessage
    {
        /// <summary>Role: "user" | "agent" | "system".</summary>
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";

        /// <summary>Agent mode ("Plan"/"Act") this message was produced in; used for the UI label.</summary>
        public string Mode { get; set; } = "";

        public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

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
    }
}
