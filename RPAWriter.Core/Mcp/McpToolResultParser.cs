using System;
using System.Text;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Flattens an MCP tool result into the plain text string that the agent loop feeds back
    /// to the model. Text blocks are concatenated; image blocks are reported as a placeholder
    /// (the current tool loop is text-only); an <c>isError</c> result is prefixed accordingly.
    /// </summary>
    public static class McpToolResultParser
    {
        public static string ToText(McpCallToolResult result)
        {
            if (result == null)
                return "(empty MCP result)";

            var sb = new StringBuilder();

            foreach (var block in result.Content)
            {
                switch (block.Type)
                {
                    case "text":
                        if (!string.IsNullOrEmpty(block.Text))
                            sb.Append(block.Text);
                        break;
                    case "image":
                        sb.Append("[image");
                        if (!string.IsNullOrEmpty(block.MimeType))
                            sb.Append(": ").Append(block.MimeType);
                        sb.Append(" (base64, ").Append((block.Data ?? "").Length).Append(" chars)]");
                        break;
                    case "resource":
                        sb.Append("[resource: ").Append(block.ResourceUri ?? "").Append("]");
                        break;
                    default:
                        if (!string.IsNullOrEmpty(block.Text))
                            sb.Append(block.Text);
                        break;
                }

                if (!string.IsNullOrEmpty(block.Text) && block.Type == "text")
                    sb.Append('\n');
            }

            var text = sb.ToString().TrimEnd();
            if (string.IsNullOrEmpty(text))
                text = result.IsError ? "(MCP tool returned an error with no content)" : "(no content)";

            if (result.IsError)
                text = "[MCP error] " + text;

            return text;
        }
    }
}
