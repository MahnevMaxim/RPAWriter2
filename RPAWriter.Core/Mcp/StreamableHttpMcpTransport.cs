using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Streamable HTTP MCP transport: each JSON-RPC message is POSTed to a single URL and the
    /// server replies with either a single <c>application/json</c> response or a
    /// <c>text/event-stream</c> of one or more messages (including server notifications).
    /// The <c>Mcp-Session-Id</c> response header is echoed back on subsequent requests.
    /// </summary>
    public sealed class StreamableHttpMcpTransport : IMcpTransport
    {
        private readonly string _url;
        private readonly HttpClient _http;
        private string _sessionId;
        private bool _disposed;

        public StreamableHttpMcpTransport(string url, HttpClient http = null)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url is required.", nameof(url));
            _url = url;
            _http = http ?? new HttpClient();
        }

        public event EventHandler<McpMessageEventArgs> MessageReceived;
#pragma warning disable CS0067
        public event EventHandler<McpMessageEventArgs> StderrLine;
#pragma warning restore CS0067

        public bool IsStarted => true;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task SendAsync(string json, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, _url))
            {
                request.Content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (!string.IsNullOrEmpty(_sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
                        _sessionId = sessionValues.FirstOrDefault();

                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (mediaType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (var data in ParseSseData(body))
                            RaiseMessage(data);
                    }
                    else
                    {
                        RaiseMessage(body);
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionId = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _http.Dispose(); } catch { /* best effort */ }
        }

        private void RaiseMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            var handler = MessageReceived;
            if (handler != null)
                handler(this, new McpMessageEventArgs { Json = json.Trim() });
        }

        /// <summary>Extracts the <c>data:</c> payloads from a server-sent-events stream.</summary>
        public static IEnumerable<string> ParseSseData(string sse)
        {
            if (string.IsNullOrEmpty(sse)) yield break;

            var data = new StringBuilder();
            foreach (var rawLine in sse.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine;
                if (line.Length == 0)
                {
                    if (data.Length > 0)
                    {
                        yield return data.ToString();
                        data.Clear();
                    }
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring(5);
                    if (value.StartsWith(" "))
                        value = value.Substring(1);
                    if (data.Length > 0)
                        data.Append('\n');
                    data.Append(value);
                }
                // event:, id: and retry: fields are ignored here — only the data payload matters.
            }

            if (data.Length > 0)
                yield return data.ToString();
        }
    }
}
