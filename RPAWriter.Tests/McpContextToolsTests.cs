using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Mcp;
using RPAWriter.Core.Models;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpContextToolsTests
    {
        private static JObject Result(JObject result) => new JObject { ["jsonrpc"] = "2.0", ["result"] = result };
        private static JObject InitResult() => new JObject { ["protocolVersion"] = McpClient.ProtocolVersion };

        private static McpClient CreateClient(Func<JObject, JObject> responder)
        {
            var client = new McpClient(new FakeMcpTransport(responder));
            client.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return client;
        }

        private static IAgentTool Find(IReadOnlyList<IAgentTool> tools, string suffix)
        {
            return tools.First(t => t.Name.EndsWith(suffix));
        }

        [Test]
        public void Create_ProducesFourReadOnlyMcpTools()
        {
            var tools = McpContextTools.Create("docs", CreateClient(req => Result(InitResult())));

            Assert.That(tools.Count, Is.EqualTo(4));
            Assert.That(tools.Select(t => t.Name), Is.EquivalentTo(new[]
            {
                "mcp__docs__list_resources",
                "mcp__docs__read_resource",
                "mcp__docs__list_prompts",
                "mcp__docs__get_prompt"
            }));
            Assert.That(tools.All(t => !t.Mutates), Is.True);
            Assert.That(tools.All(t => t.Permission == AgentPermissions.Mcp), Is.True);
        }

        [Test]
        public async Task ReadResource_ReturnsResourceText()
        {
            var client = CreateClient(req =>
                req["method"].Value<string>() == "resources/read"
                    ? Result(new JObject { ["contents"] = new JArray(new JObject { ["uri"] = "doc://a", ["text"] = "hello" }) })
                    : Result(InitResult()));

            var tool = Find(McpContextTools.Create("docs", client), "read_resource");
            var result = await tool.ExecuteAsync(new JObject { ["uri"] = "doc://a" }, CancellationToken.None);

            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public async Task ListResources_FormatsEntries()
        {
            var client = CreateClient(req =>
                req["method"].Value<string>() == "resources/list"
                    ? Result(new JObject { ["resources"] = new JArray(new JObject { ["uri"] = "doc://a", ["name"] = "Doc A" }) })
                    : Result(InitResult()));

            var tool = Find(McpContextTools.Create("docs", client), "list_resources");
            var result = await tool.ExecuteAsync(new JObject(), CancellationToken.None);

            Assert.That(result, Does.Contain("doc://a"));
            Assert.That(result, Does.Contain("Doc A"));
        }

        [Test]
        public async Task GetPrompt_ReturnsPromptText()
        {
            var client = CreateClient(req =>
                req["method"].Value<string>() == "prompts/get"
                    ? Result(new JObject
                    {
                        ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = new JObject { ["type"] = "text", ["text"] = "prompt body" } })
                    })
                    : Result(InitResult()));

            var tool = Find(McpContextTools.Create("docs", client), "get_prompt");
            var result = await tool.ExecuteAsync(new JObject { ["name"] = "explain" }, CancellationToken.None);

            Assert.That(result, Does.Contain("prompt body"));
            Assert.That(result, Does.Contain("[user]"));
        }
    }
}
