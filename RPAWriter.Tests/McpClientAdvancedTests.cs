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
    public class McpClientAdvancedTests
    {
        private static JObject Result(JObject result) =>
            new JObject { ["jsonrpc"] = "2.0", ["result"] = result };

        private static JObject InitResult() =>
            new JObject { ["protocolVersion"] = McpClient.ProtocolVersion, ["capabilities"] = new JObject() };

        private static McpClient CreateClient(Func<JObject, JObject> responder)
        {
            var transport = new FakeMcpTransport(responder);
            var client = new McpClient(transport);
            client.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return client;
        }

        [Test]
        public async Task ReadResourceAsync_ParsesTextAndBlob()
        {
            var client = CreateClient(req =>
                req["method"].Value<string>() == "resources/read"
                    ? Result(new JObject
                    {
                        ["contents"] = new JArray(
                            new JObject { ["uri"] = "doc://a", ["text"] = "hello resource" },
                            new JObject { ["uri"] = "doc://img", ["mimeType"] = "image/png", ["blob"] = "aGk=" })
                    })
                    : Result(InitResult()));

            var content = await client.ReadResourceAsync("doc://a", CancellationToken.None);

            Assert.That(content.Contents.Count, Is.EqualTo(2));
            Assert.That(content.Contents[0].Text, Is.EqualTo("hello resource"));
            Assert.That(content.Contents[1].Type, Is.EqualTo("image"));
            Assert.That(content.ToText(), Does.Contain("hello resource"));
        }

        [Test]
        public async Task GetPromptAsync_ParsesMessages()
        {
            var client = CreateClient(req =>
                req["method"].Value<string>() == "prompts/get"
                    ? Result(new JObject
                    {
                        ["description"] = "a prompt",
                        ["messages"] = new JArray(
                            new JObject { ["role"] = "user", ["content"] = new JObject { ["type"] = "text", ["text"] = "What is X?" } },
                            new JObject { ["role"] = "assistant", ["content"] = new JObject { ["type"] = "text", ["text"] = "X is Y" } })
                    })
                    : Result(InitResult()));

            var prompt = await client.GetPromptAsync("explain", new JObject(), CancellationToken.None);

            Assert.That(prompt.Description, Is.EqualTo("a prompt"));
            Assert.That(prompt.Messages.Count, Is.EqualTo(2));
            Assert.That(prompt.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(prompt.Messages[1].Text, Is.EqualTo("X is Y"));
            Assert.That(prompt.ToText(), Does.Contain("[user]"));
        }

        [Test]
        public void Request_Timeout_SendsCancellationNotification()
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "ping" ? null : Result(InitResult()));

            var client = new McpClient(transport) { RequestTimeout = TimeSpan.FromMilliseconds(50) };

            Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await client.InitializeAsync(CancellationToken.None);
                await client.PingAsync(CancellationToken.None);
            });

            var last = JObject.Parse(transport.SentMessages[transport.SentMessages.Count - 1]);
            Assert.That(last["method"].Value<string>(), Is.EqualTo("notifications/cancelled"));
            Assert.That(last["params"]["reason"].Value<string>(), Is.EqualTo("timeout"));
        }
        [Test]
        public void ProgressNotification_RaisesEvent()
        {
            var transport = new FakeMcpTransport(req => Result(InitResult()));
            var client = new McpClient(transport);
            client.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            McpProgressEventArgs received = null;
            client.ProgressReceived += (s, e) => received = e;

            transport.RaiseMessage("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":\"t1\",\"progress\":5,\"total\":10}}");

            Assert.That(received, Is.Not.Null);
            Assert.That(received.ProgressToken, Is.EqualTo("t1"));
            Assert.That(received.Progress, Is.EqualTo(5));
            Assert.That(received.Total, Is.EqualTo(10));
        }
    }
}
