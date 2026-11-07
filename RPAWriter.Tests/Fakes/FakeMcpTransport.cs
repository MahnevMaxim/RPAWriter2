using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Mcp;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="IMcpTransport"/> that answers each request through a scripted
    /// responder. The responder receives the parsed request and returns the response body
    /// (or null to leave the request unanswered, simulating a hang). Notifications (no id)
    /// are recorded but never answered, matching the MCP wire contract.
    /// </summary>
    public sealed class FakeMcpTransport : IMcpTransport
    {
        private readonly Func<JObject, JObject> _responder;

        public FakeMcpTransport(Func<JObject, JObject> responder)
        {
            _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        public List<string> SentMessages { get; } = new List<string>();

        public event EventHandler<McpMessageEventArgs> MessageReceived;
#pragma warning disable CS0067
        public event EventHandler<McpMessageEventArgs> StderrLine;
#pragma warning restore CS0067

        public bool IsStarted { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken cancellationToken)
        {
            SentMessages.Add(json);

            var request = JObject.Parse(json);
            if (request["id"] == null)
                return Task.CompletedTask; // notification — never answered

            var response = _responder(request);
            if (response == null)
                return Task.CompletedTask; // scripted hang

            if (response["id"] == null)
                response["id"] = request["id"];
            if (response["jsonrpc"] == null)
                response["jsonrpc"] = "2.0";

            var handler = MessageReceived;
            if (handler != null)
                handler(this, new McpMessageEventArgs { Json = response.ToString(Formatting.None) });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsStarted = false;
            return Task.CompletedTask;
        }

        /// <summary>Simulates a server-initiated message (notification) arriving on the wire.</summary>
        public void RaiseMessage(string json)
        {
            var handler = MessageReceived;
            if (handler != null)
                handler(this, new McpMessageEventArgs { Json = json });
        }

        public void Dispose()
        {
        }
    }
}
