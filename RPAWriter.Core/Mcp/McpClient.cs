using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// A minimal MCP client: JSON-RPC 2.0 over an <see cref="IMcpTransport"/>, enough to talk
    /// to any stdio/HTTP MCP server. Implements the lifecycle handshake and the tools,
    /// resources and prompts primitives. Written against Newtonsoft.Json (already in
    /// RPAWriter.Core) so it runs on netstandard2.0 without the official SDK's dependency tree.
    /// </summary>
    /// <summary>A progress notification received from an MCP server (notifications/progress).</summary>
    public sealed class McpProgressEventArgs : EventArgs
    {
        public string ProgressToken { get; set; } = "";
        public double Progress { get; set; }
        public double Total { get; set; }
    }

    public sealed class McpClient : IDisposable
    {
        /// <summary>Protocol version we advertise. Servers negotiate down as needed.</summary>
        public const string ProtocolVersion = "2024-11-05";

        private readonly IMcpTransport _transport;
        private readonly object _sync = new object();
        private readonly Dictionary<long, TaskCompletionSource<JObject>> _pending =
            new Dictionary<long, TaskCompletionSource<JObject>>();
        private long _nextId = 1;
        private bool _initialized;
        private bool _disposed;

        public McpClient(IMcpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.MessageReceived += OnMessageReceived;
        }

        public string ClientName { get; set; } = "rpa-writer";
        public string ClientVersion { get; set; } = "1.0.0";

        /// <summary>Timeout applied to each request, used when the transport does not respond.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Raised for server progress notifications (notifications/progress).</summary>
        public event EventHandler<McpProgressEventArgs> ProgressReceived;

        public bool IsInitialized => _initialized;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_initialized) return;
            ThrowIfDisposed();

            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

            var init = BuildRequest("initialize", new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = ClientName,
                    ["version"] = ClientVersion
                }
            });
            var initResponse = await SendRequestAsync(init, cancellationToken).ConfigureAwait(false);
            if (initResponse == null)
                throw new McpException(-32000, "MCP server did not return an initialize result.");

            // Acknowledge initialization (a notification, no id, no response).
            var initialized = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await _transport.SendAsync(initialized.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

            _initialized = true;
        }

        public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("tools/list", new JObject()), cancellationToken)
                .ConfigureAwait(false);
            return McpTool.FromList(response?["tools"]);
        }

        public async Task<McpCallToolResult> CallToolAsync(string name, JObject arguments, CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("tools/call", new JObject
            {
                ["name"] = name,
                ["arguments"] = arguments ?? new JObject()
            }), cancellationToken).ConfigureAwait(false);
            return McpCallToolResult.FromJson(response);
        }

        public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("resources/list", new JObject()), cancellationToken)
                .ConfigureAwait(false);
            return McpResource.FromList(response?["resources"]);
        }

        public async Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("prompts/list", new JObject()), cancellationToken)
                .ConfigureAwait(false);
            return McpPrompt.FromList(response?["prompts"]);
        }

        public async Task PingAsync(CancellationToken cancellationToken)
        {
            EnsureInitialized();
            await SendRequestAsync(BuildRequest("ping", new JObject()), cancellationToken).ConfigureAwait(false);
        }

        public async Task<McpResourceContent> ReadResourceAsync(string uri, CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("resources/read", new JObject { ["uri"] = uri ?? "" }), cancellationToken)
                .ConfigureAwait(false);
            return McpResourceContent.FromJson(response);
        }

        public async Task<McpPromptResult> GetPromptAsync(string name, JObject arguments, CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var response = await SendRequestAsync(BuildRequest("prompts/get", new JObject
            {
                ["name"] = name ?? "",
                ["arguments"] = arguments ?? new JObject()
            }), cancellationToken).ConfigureAwait(false);
            return McpPromptResult.FromJson(response);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.MessageReceived -= OnMessageReceived;
            try { _transport.Dispose(); } catch { /* best effort */ }
        }

        private void EnsureInitialized()
        {
            ThrowIfDisposed();
            if (!_initialized)
                throw new InvalidOperationException("The MCP client has not been initialized.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McpClient));
        }

        private JObject BuildRequest(string method, JObject parameters)
        {
            long id;
            lock (_sync) { id = _nextId++; }
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new JObject()
            };
        }

        private async Task<JObject> SendRequestAsync(JObject request, CancellationToken cancellationToken)
        {
            var id = request["id"].Value<long>();
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync)
            {
                _pending[id] = tcs;
            }

            try
            {
                await _transport.SendAsync(request.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_sync) { _pending.Remove(id); }
                throw;
            }

            using (cancellationToken.Register(() => AbortRequest(id, new OperationCanceledException(cancellationToken), "cancelled")))
            {
                var timeout = Task.Delay(RequestTimeout, cancellationToken);
                var completed = await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(false);
                if (completed == timeout)
                {
                    AbortRequest(id, new TimeoutException("MCP request timed out after " + RequestTimeout.TotalSeconds + "s."), "timeout");
                }
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        private void OnMessageReceived(object sender, McpMessageEventArgs e)
        {
            JObject message;
            try
            {
                message = JObject.Parse(e.Json);
            }
            catch
            {
                return; // not a protocol message (should not happen on stdout)
            }

            var idToken = message["id"];
            if (idToken == null)
            {
                HandleNotification(message);
                return;
            }

            long id;
            try { id = idToken.Value<long>(); }
            catch { return; }

            TaskCompletionSource<JObject> tcs;
            lock (_sync)
            {
                if (!_pending.TryGetValue(id, out tcs)) return;
                _pending.Remove(id);
            }

            var error = message["error"];
            if (error != null && error.Type != JTokenType.Null)
                tcs.TrySetException(McpException.FromJson(error));
            else
                tcs.TrySetResult(message["result"] as JObject);
        }

        private void HandleNotification(JObject message)
        {
            var method = message["method"]?.Value<string>() ?? "";
            if (method == "notifications/progress")
            {
                var token = message["params"]?["progressToken"]?.Value<string>() ?? "";
                var progress = message["params"]?["progress"]?.Value<double>() ?? 0;
                var total = message["params"]?["total"]?.Value<double>() ?? 0;
                var handler = ProgressReceived;
                if (handler != null)
                    handler(this, new McpProgressEventArgs { ProgressToken = token, Progress = progress, Total = total });
            }
        }

        private void AbortRequest(long id, Exception ex, string reason)
        {
            TaskCompletionSource<JObject> tcs;
            lock (_sync)
            {
                if (!_pending.TryGetValue(id, out tcs)) return;
                _pending.Remove(id);
            }
            tcs.TrySetException(ex);

            // Tell the server it can stop working on the abandoned request.
            _ = NotifyCancellationAsync(id, reason);
        }

        private async Task NotifyCancellationAsync(long id, string reason)
        {
            try
            {
                var message = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/cancelled",
                    ["params"] = new JObject { ["requestId"] = id, ["reason"] = reason ?? "cancelled" }
                };
                await _transport.SendAsync(message.ToString(Formatting.None), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best effort: the transport may already be closed.
            }
        }
    }
}

