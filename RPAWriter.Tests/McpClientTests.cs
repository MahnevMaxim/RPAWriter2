using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.Mcp;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpClientTests
    {
        private static JObject Result(JObject result) =>
            new JObject { ["jsonrpc"] = "2.0", ["result"] = result };

        private static JObject Error(int code, string message) =>
            new JObject { ["jsonrpc"] = "2.0", ["error"] = new JObject { ["code"] = code, ["message"] = message } };

        private static JObject InitResult() =>
            new JObject { ["protocolVersion"] = McpClient.ProtocolVersion, ["capabilities"] = new JObject(), ["serverInfo"] = new JObject { ["name"] = "test", ["version"] = "1" } };

        [Test]
        public async Task InitializeAsync_SendsInitializeThenInitialized()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "initialize" ? Result(InitResult()) : Result(new JObject()));

            var client = new McpClient(transport);
            await client.InitializeAsync(CancellationToken.None);

            Assert.That(client.IsInitialized, Is.True);
            Assert.That(transport.SentMessages.Count, Is.EqualTo(2));
            Assert.That(JObject.Parse(transport.SentMessages[0])["method"].Value<string>(), Is.EqualTo("initialize"));
            Assert.That(JObject.Parse(transport.SentMessages[1])["method"].Value<string>(), Is.EqualTo("notifications/initialized"));
        }

        [Test]
        public async Task ListToolsAsync_ParsesTools()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "tools/list"
                    ? Result(new JObject
                    {
                        ["tools"] = new JArray(new JObject
                        {
                            ["name"] = "navigate",
                            ["description"] = "Go to a URL",
                            ["inputSchema"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject { ["url"] = new JObject { ["type"] = "string" } }
                            }
                        })
                    })
                    : Result(InitResult()));

            var client = new McpClient(transport);
            await client.InitializeAsync(CancellationToken.None);
            var tools = await client.ListToolsAsync(CancellationToken.None);

            Assert.That(tools.Count, Is.EqualTo(1));
            Assert.That(tools[0].Name, Is.EqualTo("navigate"));
            Assert.That(tools[0].Description, Is.EqualTo("Go to a URL"));
            Assert.That(tools[0].InputSchema["type"].Value<string>(), Is.EqualTo("object"));
        }

        [Test]
        public async Task CallToolAsync_ParsesContentAndErrorFlag()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "tools/call"
                    ? Result(new JObject
                    {
                        ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = "hello" }),
                        ["isError"] = false
                    })
                    : Result(InitResult()));

            var client = new McpClient(transport);
            await client.InitializeAsync(CancellationToken.None);
            var result = await client.CallToolAsync("navigate", new JObject { ["url"] = "https://example.com" }, CancellationToken.None);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content.Count, Is.EqualTo(1));
            Assert.That(result.Content[0].Type, Is.EqualTo("text"));
            Assert.That(result.Content[0].Text, Is.EqualTo("hello"));
        }

        [Test]
        public void CallToolAsync_ServerError_ThrowsMcpException()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "tools/call"
                    ? Error(-32600, "boom")
                    : Result(InitResult()));

            var client = new McpClient(transport);

            var ex = Assert.ThrowsAsync<McpException>(async () =>
            {
                await client.InitializeAsync(CancellationToken.None);
                await client.CallToolAsync("x", new JObject(), CancellationToken.None);
            });

            Assert.That(ex.Code, Is.EqualTo(-32600));
            Assert.That(ex.Message, Is.EqualTo("boom"));
        }

        [Test]
        public void Request_NoResponse_ThrowsTimeout()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "tools/list" ? null : Result(InitResult()));

            var client = new McpClient(transport) { RequestTimeout = TimeSpan.FromMilliseconds(50) };

            Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await client.InitializeAsync(CancellationToken.None);
                await client.ListToolsAsync(CancellationToken.None);
            });
        }

        [Test]
        public async Task PingAsync_Completes()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "ping" ? Result(new JObject()) : Result(InitResult()));

            var client = new McpClient(transport);
            await client.InitializeAsync(CancellationToken.None);

            Assert.DoesNotThrowAsync(async () => await client.PingAsync(CancellationToken.None));
        }
    }
}
