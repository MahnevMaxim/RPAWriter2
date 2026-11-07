using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Mcp;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpHttpTransportTests
    {
        private sealed class FakeHttpTransportHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        private static HttpResponseMessage JsonResponse(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage SseResponse(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

        [Test]
        public async Task StreamableHttp_JsonResponse_RaisesMessage()
        {
            var handler = new FakeHttpTransportHandler { Responder = req => JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}") };
            var transport = new StreamableHttpMcpTransport("https://example.com/mcp", new HttpClient(handler));

            string received = null;
            transport.MessageReceived += (s, e) => received = e.Json;

            await transport.StartAsync(CancellationToken.None);
            await transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", CancellationToken.None);

            Assert.That(received, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}"));
        }

        [Test]
        public async Task StreamableHttp_SseResponse_RaisesEachDataEvent()
        {
            var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\n";
            var handler = new FakeHttpTransportHandler { Responder = req => SseResponse(sse) };
            var transport = new StreamableHttpMcpTransport("https://example.com/mcp", new HttpClient(handler));

            var received = new List<string>();
            transport.MessageReceived += (s, e) => received.Add(e.Json);

            await transport.SendAsync("{}", CancellationToken.None);

            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0], Is.EqualTo("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"));
            Assert.That(received[1], Is.EqualTo("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}"));
        }

        [Test]
        public void ParseSseData_ExtractsDataPayloads()
        {
            var sse = "data: a\n\nid: 1\ndata: b\n\nretry: 100\n\n";
            var result = StreamableHttpMcpTransport.ParseSseData(sse).ToList();

            Assert.That(result, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task StreamableHttp_EchoesSessionId()
        {
            var handler = new FakeHttpTransportHandler();
            handler.Responder = req =>
            {
                var resp = JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}");
                resp.Headers.Add("Mcp-Session-Id", "session-123");
                return resp;
            };
            var transport = new StreamableHttpMcpTransport("https://example.com/mcp", new HttpClient(handler));

            await transport.SendAsync("{}", CancellationToken.None);
            await transport.SendAsync("{}", CancellationToken.None);

            Assert.That(handler.Requests.Count, Is.EqualTo(2));
            Assert.That(handler.Requests[1].Headers.TryGetValues("Mcp-Session-Id", out var values), Is.True);
            Assert.That(values.First(), Is.EqualTo("session-123"));
        }

        [Test]
        public async Task Sse_StartDiscoversEndpoint_AndPostsToIt()
        {
            var handler = new FakeHttpTransportHandler();
            handler.Responder = req =>
                req.Method == HttpMethod.Get
                    ? SseResponse("event: endpoint\ndata: https://example.com/messages\n\n")
                    : new HttpResponseMessage(HttpStatusCode.Accepted);

            var transport = new SseMcpTransport("https://example.com/sse", new HttpClient(handler));

            await transport.StartAsync(CancellationToken.None);
            await transport.SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", CancellationToken.None);

            var post = handler.Requests.First(r => r.Method == HttpMethod.Post);
            Assert.That(post.RequestUri.ToString(), Is.EqualTo("https://example.com/messages"));
        }

        [Test]
        public async Task Sse_MessageEvent_RaisesMessage()
        {
            var handler = new FakeHttpTransportHandler();
            handler.Responder = req =>
                req.Method == HttpMethod.Get
                    ? SseResponse("event: endpoint\ndata: /messages\n\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}\n\n")
                    : new HttpResponseMessage(HttpStatusCode.Accepted);

            var transport = new SseMcpTransport("https://example.com/sse", new HttpClient(handler));

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.MessageReceived += (s, e) => received.TrySetResult(e.Json);

            await transport.StartAsync(CancellationToken.None);
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(message, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}"));
        }
    }
}

