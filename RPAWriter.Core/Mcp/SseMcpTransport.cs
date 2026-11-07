using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Deprecated HTTP + Server-Sent Events MCP transport. The client opens the SSE endpoint
    /// (GET), receives an <c>event: endpoint</c> message that names the URL to POST JSON-RPC
    /// messages to, then keeps reading the stream for <c>event: message</c> responses.
    /// Kept for compatibility with older servers; prefer <see cref="StreamableHttpMcpTransport"/>.
    /// </summary>
    public sealed class SseMcpTransport : IMcpTransport
    {
        private readonly string _url;
        private readonly HttpClient _http;
        private readonly TaskCompletionSource<bool> _endpointReceived = new TaskCompletionSource<bool>();
        private string _messageEndpoint;
        private Stream _stream;
        private Task _readLoop;
        private bool _disposed;

        public SseMcpTransport(string url, HttpClient http = null)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url is required.", nameof(url));
            _url = url;
            _http = http ?? new HttpClient();
        }

        public event EventHandler<McpMessageEventArgs> MessageReceived;
#pragma warning disable CS0067
        public event EventHandler<McpMessageEventArgs> StderrLine;
#pragma warning restore CS0067

        public bool IsStarted => _stream != null;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            _readLoop = Task.Run(() => ReadLoopAsync(_stream), CancellationToken.None);

            // The server must announce its message endpoint before we can send anything.
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            if (await Task.WhenAny(_endpointReceived.Task, timeout).ConfigureAwait(false) == timeout)
                throw new TimeoutException("SSE transport did not receive the endpoint event.");
        }

        public async Task SendAsync(string json, CancellationToken cancellationToken)
        {
            var endpoint = _messageEndpoint;
            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException("SSE transport has no message endpoint.");

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
                using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    // 202 Accepted is expected: the real response arrives via the SSE stream.
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try { _stream?.Dispose(); } catch { /* best effort */ }
            _stream = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* best effort */ }
            try { _http.Dispose(); } catch { /* best effort */ }
        }

        private async Task ReadLoopAsync(Stream stream)
        {
            try
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var data = new StringBuilder();
                    var eventName = "";
                    while (true)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;

                        if (line.Length == 0)
                        {
                            ProcessEvent(eventName, data.ToString());
                            eventName = "";
                            data.Clear();
                            continue;
                        }

                        if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                            eventName = line.Substring(6).Trim();
                        else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = line.Substring(5);
                            if (value.StartsWith(" "))
                                value = value.Substring(1);
                            if (data.Length > 0)
                                data.Append('\n');
                            data.Append(value);
                        }
                    }
                }
            }
            catch
            {
                // The stream was closed (transport stopped); nothing to propagate.
            }
        }

        private void ProcessEvent(string eventName, string data)
        {
            if (string.IsNullOrEmpty(eventName) || string.IsNullOrWhiteSpace(data)) return;

            if (string.Equals(eventName, "endpoint", StringComparison.OrdinalIgnoreCase))
            {
                _messageEndpoint = ResolveEndpoint(data);
                _endpointReceived.TrySetResult(true);
                return;
            }

            if (string.Equals(eventName, "message", StringComparison.OrdinalIgnoreCase))
            {
                var handler = MessageReceived;
                if (handler != null)
                    handler(this, new McpMessageEventArgs { Json = data.Trim() });
            }
        }

        private string ResolveEndpoint(string endpoint)
        {
            var value = endpoint.Trim();
            if (Uri.TryCreate(_url, UriKind.Absolute, out var baseUri) &&
                Uri.TryCreate(baseUri, value, out var resolved))
                return resolved.ToString();
            return value;
        }
    }
}

