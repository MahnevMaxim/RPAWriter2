namespace RPAWriter2
{
    /// <summary>A chat message from the agent: role, text, and (optionally) the model's "reasoning".</summary>
    public class ChatMessage
    {
        /// <summary>Role: "user" | "agent" | "system".</summary>
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";

        public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

        public string Label
        {
            get
            {
                switch (Role)
                {
                    case "user": return "You: ";
                    case "agent": return "Agent: ";
                    default: return "";
                }
            }
        }

        public string DisplayText => string.IsNullOrEmpty(Label) ? (Text ?? "") : Label + (Text ?? "");
    }
}
