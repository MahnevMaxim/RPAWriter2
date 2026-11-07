using NUnit.Framework;
using RPAWriter.Core.Mcp;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpServerSettingsTests
    {
        [Test]
        public void Parse_ClineCompatibleJson_ProducesServers()
        {
            var json = @"
            {
              ""mcpServers"": {
                ""playwright"": { ""command"": ""npx"", ""args"": [""@playwright/mcp@latest""] },
                ""filesystem"": { ""command"": ""npx"", ""args"": [""-y"", ""@modelcontextprotocol/server-filesystem"", ""C:/robots""] },
                ""remote"": { ""url"": ""https://example.com/mcp"" }
              }
            }";

            var servers = McpServerSettings.Parse(json);

            Assert.That(servers.Count, Is.EqualTo(3));
            Assert.That(servers[0].Name, Is.EqualTo("playwright"));
            Assert.That(servers[0].Command, Is.EqualTo("npx"));
            Assert.That(servers[0].Args, Is.EquivalentTo(new[] { "@playwright/mcp@latest" }));
            Assert.That(servers[0].Type, Is.EqualTo(McpTransportType.Stdio));

            Assert.That(servers[2].Url, Is.EqualTo("https://example.com/mcp"));
            Assert.That(servers[2].Type, Is.EqualTo(McpTransportType.StreamableHttp));
        }

        [Test]
        public void Parse_DisabledAndTimeout_Read()
        {
            var json = @"{ ""mcpServers"": { ""a"": { ""command"": ""uvx"", ""args"": [""mcp-server-fetch""], ""disabled"": true, ""timeout"": 60 } } }";

            var servers = McpServerSettings.Parse(json);

            Assert.That(servers.Count, Is.EqualTo(1));
            Assert.That(servers[0].Disabled, Is.True);
            Assert.That(servers[0].Enabled, Is.False);
            Assert.That(servers[0].TimeoutSeconds, Is.EqualTo(60));
        }

        [Test]
        public void ToJson_RoundTrips()
        {
            var config = new McpServerConfig
            {
                Name = "playwright",
                Command = "npx",
                Args = new System.Collections.Generic.List<string> { "@playwright/mcp@latest" }
            };

            var json = McpServerSettings.ToJson(new[] { config });
            var parsed = McpServerSettings.Parse(json);

            Assert.That(parsed.Count, Is.EqualTo(1));
            Assert.That(parsed[0].Name, Is.EqualTo("playwright"));
            Assert.That(parsed[0].Command, Is.EqualTo("npx"));
            Assert.That(parsed[0].Args, Is.EquivalentTo(new[] { "@playwright/mcp@latest" }));
        }

        [Test]
        public void Parse_EmptyOrInvalid_ReturnsEmpty()
        {
            Assert.That(McpServerSettings.Parse(null).Count, Is.EqualTo(0));
            Assert.That(McpServerSettings.Parse("").Count, Is.EqualTo(0));
            Assert.That(McpServerSettings.Parse("not json").Count, Is.EqualTo(0));
        }
    }
}
