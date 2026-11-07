using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Bridges one MCP tool into the agent's tool pipeline as an <see cref="IAgentTool"/>.
    /// The machine name is namespaced (<c>mcp__server__tool</c>) so a server tool can never
    /// collide with a native tool, and the permission category is
    /// <see cref="AgentPermissions.Mcp"/>, so the existing Permissions gate applies unchanged.
    /// MCP tools are treated as mutating by default, which keeps them out of Plan mode.
    /// </summary>
    public sealed class McpToolAdapter : IAgentTool
    {
        private readonly string _serverName;
        private readonly string _toolName;
        private readonly McpClient _client;

        public McpToolAdapter(string serverName, McpTool tool, McpClient client)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serverName = serverName ?? "";
            _toolName = tool.Name ?? "";

            Name = "mcp__" + McpNames.Sanitize(_serverName) + "__" + McpNames.Sanitize(_toolName);
            Description = (tool.Description ?? "").Trim();
            ParametersSchema = McpSchemaConverter.ToInformal(tool.InputSchema);
            Mutates = true;
            Permission = AgentPermissions.Mcp;
            CompletesRun = false;
        }

        public string Name { get; }
        public string Description { get; }
        public string ParametersSchema { get; }
        public bool Mutates { get; }
        public AgentPermissions Permission { get; }
        public bool CompletesRun { get; }

        public async Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken)
        {
            McpCallToolResult result;
            try
            {
                result = await _client.CallToolAsync(_toolName, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "MCP tool \"" + _toolName + "\" failed: " + ex.Message;
            }

            return McpToolResultParser.ToText(result);
        }

        public Task<string> GetDisplayResultAsync(JObject arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }
    }
}
