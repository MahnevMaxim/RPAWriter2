using System.Text;

namespace RPAWriter.Core.Mcp
{
    /// <summary>Shared tool-name sanitization for MCP-provided agent tools.</summary>
    internal static class McpNames
    {
        /// <summary>Keeps only letters, digits, <c>-</c> and <c>_</c>; falls back to "server".</summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "server";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            var sanitized = sb.ToString();
            return sanitized.Length > 0 ? sanitized : "server";
        }
    }
}
