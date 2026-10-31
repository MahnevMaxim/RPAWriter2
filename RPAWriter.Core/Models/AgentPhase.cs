namespace RPAWriter.Core.Models
{
    /// <summary>
    /// Explicit phase of the current agent turn. Drives the footer buttons and the status
    /// indicator. It is part of the chat domain (not the view) because the chat model exposes it
    /// as state and the agent host only reflects it.
    /// </summary>
    public enum AgentPhase
    {
        /// <summary>No turn is running.</summary>
        Idle,

        /// <summary>The model is streaming an answer or running tools.</summary>
        Streaming,

        /// <summary>The last turn failed.</summary>
        Error
    }
}
