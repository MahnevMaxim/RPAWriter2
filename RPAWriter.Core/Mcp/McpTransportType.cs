using System;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// How an MCP server is reached. The MCP specification defines two standard transports
    /// (stdio and streamable HTTP); SSE is the deprecated HTTP+SSE variant that is still
    /// found in the wild and kept here for forward compatibility.
    /// </summary>
    public enum McpTransportType
    {
        /// <summary>The client launches the server as a subprocess and talks over stdin/stdout.</summary>
        Stdio = 0,

        /// <summary>The server runs as an HTTP service reachable at a URL (streamable HTTP).</summary>
        StreamableHttp = 1,

        /// <summary>Deprecated HTTP + Server-Sent Events transport.</summary>
        Sse = 2
    }
}
