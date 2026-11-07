using System;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Mcp
{
    /// <summary>Carries one raw JSON-RPC message received from an MCP server.</summary>
    public sealed class McpMessageEventArgs : EventArgs
    {
        public string Json { get; set; } = "";
    }

    /// <summary>
    /// A bidirectional, message-oriented pipe to an MCP server. The client sends JSON-RPC
    /// messages through <see cref="SendAsync"/> and receives them through the
    /// <see cref="MessageReceived"/> event. Keeping the wire mechanics behind this interface
    /// lets <see cref="McpClient"/> be tested against an in-memory fake transport.
    /// </summary>
    public interface IMcpTransport : IDisposable
    {
        /// <summary>True once the transport has been started and the server is reachable.</summary>
        bool IsStarted { get; }

        /// <summary>Opens the transport (for stdio: launches the server subprocess).</summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>Sends a single JSON-RPC message (request, response or notification).</summary>
        Task SendAsync(string json, CancellationToken cancellationToken);

        /// <summary>Raised for every complete JSON-RPC message received from the server.</summary>
        event EventHandler<McpMessageEventArgs> MessageReceived;

        /// <summary>Raised for server stderr lines (diagnostics only; never protocol data).</summary>
        event EventHandler<McpMessageEventArgs> StderrLine;

        /// <summary>Closes the transport and stops the server process, if any.</summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}
