using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Describes a single configured MCP server. The field names mirror the widely used
    /// <c>mcpServers</c> JSON shape (Cline / Roo) so an existing configuration can be pasted
    /// in unchanged: <c>{ "name": { "command", "args", "env", "url", "type", "disabled" } }</c>.
    /// </summary>
    public sealed class McpServerConfig
    {
        /// <summary>Stable key used to namespace the server's tools (e.g. "playwright").</summary>
        public string Name { get; set; } = "";

        /// <summary>Executable to launch for the stdio transport (e.g. "npx", "uvx", "node").</summary>
        public string Command { get; set; } = "";

        /// <summary>Arguments passed to <see cref="Command"/> for the stdio transport.</summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>Extra environment variables for the stdio transport.</summary>
        public Dictionary<string, string> Env { get; set; } = new Dictionary<string, string>();

        /// <summary>Server URL for the streamable HTTP / SSE transports.</summary>
        public string Url { get; set; } = "";

        /// <summary>Transport to use. Defaults to stdio when <see cref="Url"/> is empty.</summary>
        public McpTransportType Type { get; set; } = McpTransportType.Stdio;

        /// <summary>True when the server should be skipped without starting it.</summary>
        public bool Disabled { get; set; }

        /// <summary>Handshake/request timeout in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>Convenience inverse of <see cref="Disabled"/>.</summary>
        public bool Enabled => !Disabled;

        /// <summary>True when the server is reachable over a URL rather than a subprocess.</summary>
        public bool IsRemote => !string.IsNullOrWhiteSpace(Url);
    }
}
