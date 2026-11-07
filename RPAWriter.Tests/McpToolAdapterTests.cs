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
    public class McpToolAdapterTests
    {
        private static McpTool Tool() => new McpTool
        {
            Name = "browser_navigate",
            Description = "Navigate to a URL",
            InputSchema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject { ["url"] = new JObject { ["type"] = "string" } },
                ["required"] = new JArray("url")
            }
        };

        private static McpClient CreateClient(string callResultText = "ok")
        {
            var transport = new FakeMcpTransport(req =>
                req["method"].Value<string>() == "tools/call"
                    ? new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["result"] = new JObject
                        {
                            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = callResultText })
                        }
                    }
                    : new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["result"] = new JObject { ["protocolVersion"] = McpClient.ProtocolVersion }
                    });

            var client = new McpClient(transport);
            client.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return client;
        }

        [Test]
        public void Ctor_NamespacesNameAndMapsPermissions()
        {
            var adapter = new McpToolAdapter("playwright", Tool(), CreateClient());

            Assert.That(adapter.Name, Is.EqualTo("mcp__playwright__browser_navigate"));
            Assert.That(adapter.Description, Is.EqualTo("Navigate to a URL"));
            Assert.That(adapter.Permission, Is.EqualTo(AgentPermissions.Mcp));
            Assert.That(adapter.Mutates, Is.True);
            Assert.That(adapter.CompletesRun, Is.False);
        }

        [Test]
        public void Ctor_ConvertsInputSchemaToInformal()
        {
            var adapter = new McpToolAdapter("playwright", Tool(), CreateClient());

            var schema = JObject.Parse(adapter.ParametersSchema);
            Assert.That(schema["url"].Value<string>(), Is.EqualTo("string"));
        }

        [Test]
        public void Ctor_SanitizesServerAndToolNames()
        {
            var tool = new McpTool { Name = "tool name!", Description = "", InputSchema = new JObject() };
            var adapter = new McpToolAdapter("my server", tool, CreateClient());

            Assert.That(adapter.Name, Is.EqualTo("mcp__my_server__tool_name_"));
        }

        [Test]
        public async Task ExecuteAsync_CallsToolAndParsesResult()
        {
            var adapter = new McpToolAdapter("playwright", Tool(), CreateClient("page loaded"));

            var result = await adapter.ExecuteAsync(new JObject { ["url"] = "https://example.com" }, CancellationToken.None);

            Assert.That(result, Is.EqualTo("page loaded"));
        }

        [Test]
        public void Adapter_IsAssignableToIAgentTool()
        {
            var adapter = new McpToolAdapter("playwright", Tool(), CreateClient());
            Assert.That(adapter, Is.InstanceOf<IAgentTool>());
        }
    }
}
