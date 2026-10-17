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
}
