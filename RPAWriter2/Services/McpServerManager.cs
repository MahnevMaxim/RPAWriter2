using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Mcp;

namespace RPAWriter2.Services
{
    /// <summary>Result of connecting to the configured MCP servers.</summary>
    public sealed class McpConnectResult
    {
        public IReadOnlyList<McpClient> Clients { get; set; } = new List<McpClient>();
        public IReadOnlyList<IAgentTool> Tools { get; set; } = new List<IAgentTool>();
        public IReadOnlyList<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Wires the Core MCP layer into the VSIX: parses the <c>mcpServers</c> configuration,
    /// launches each enabled server, discovers its tools and wraps them (plus the read-only
    /// resource/prompt context tools) as agent tools. Failures are collected per server and
    /// never throw, so a broken MCP server cannot break the rest of the agent.
    /// </summary>
    public static class McpServerManager
    {
        public static async Task<McpConnectResult> ConnectAsync(string mcpServersJson, CancellationToken ct)
        {
            var result = new McpConnectResult();
            var clients = new List<McpClient>();
            var tools = new List<IAgentTool>();
            var errors = new List<string>();

            var configs = McpServerSettings.Parse(mcpServersJson);
            foreach (var config in configs)
            {
                if (!config.Enabled)
                    continue;

                McpClient client = null;
                try
                {
                    var transport = CreateTransport(config);
                    client = new McpClient(transport)
                    {
                        RequestTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 30)
                    };

                    await client.InitializeAsync(ct).ConfigureAwait(false);
                    var serverTools = await client.ListToolsAsync(ct).ConfigureAwait(false);
                    foreach (var tool in serverTools)
                        tools.Add(new McpToolAdapter(config.Name, tool, client));
                    foreach (var contextTool in McpContextTools.Create(config.Name, client))
                        tools.Add(contextTool);
                    clients.Add(client);
                }
                catch (Exception ex)
                {
                    if (client != null)
                        client.Dispose();
                    errors.Add("MCP server \"" + config.Name + "\" failed to start: " + ex.Message);
                }
            }

            result.Clients = clients;
            result.Tools = tools;
            result.Errors = errors;
            return result;
        }

        private static IMcpTransport CreateTransport(McpServerConfig config)
        {
            switch (config.Type)
            {
                case McpTransportType.StreamableHttp:
                    return new StreamableHttpMcpTransport(config.Url);
                case McpTransportType.Sse:
                    return new SseMcpTransport(config.Url);
                default:
                    return new StdioMcpTransport(config);
            }
        }
    }
}
