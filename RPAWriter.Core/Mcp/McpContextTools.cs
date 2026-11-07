using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Builds the read-only agent tools that expose an MCP server's resources and prompts
    /// (<c>list_resources</c>, <c>read_resource</c>, <c>list_prompts</c>, <c>get_prompt</c>).
    /// They are namespaced like the tool adapters (<c>mcp__server__...</c>) and gated by
    /// <see cref="AgentPermissions.Mcp"/>, but are not mutating, so they also work in Plan mode.
    /// </summary>
    public static class McpContextTools
    {
        public static IReadOnlyList<IAgentTool> Create(string serverName, McpClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            var prefix = "mcp__" + McpNames.Sanitize(serverName) + "__";

            return new List<IAgentTool>
            {
                new AgentTool(
                    prefix + "list_resources",
                    "List the resources exposed by the MCP server \"" + serverName + "\".",
                    "{}",
                    mutates: false,
                    async (args, ct) => FormatResources(await client.ListResourcesAsync(ct).ConfigureAwait(false)),
                    permission: AgentPermissions.Mcp),

                new AgentTool(
                    prefix + "read_resource",
                    "Read an MCP resource by its URI.",
                    "{\"uri\":\"string\"}",
                    mutates: false,
                    async (args, ct) =>
                    {
                        var uri = args?["uri"]?.Value<string>() ?? "";
                        var content = await client.ReadResourceAsync(uri, ct).ConfigureAwait(false);
                        return content.ToText();
                    },
                    permission: AgentPermissions.Mcp),

                new AgentTool(
                    prefix + "list_prompts",
                    "List the prompts exposed by the MCP server \"" + serverName + "\".",
                    "{}",
                    mutates: false,
                    async (args, ct) => FormatPrompts(await client.ListPromptsAsync(ct).ConfigureAwait(false)),
                    permission: AgentPermissions.Mcp),

                new AgentTool(
                    prefix + "get_prompt",
                    "Get an MCP prompt by name (optionally with arguments).",
                    "{\"name\":\"string\",\"arguments?\":\"object\"}",
                    mutates: false,
                    async (args, ct) =>
                    {
                        var name = args?["name"]?.Value<string>() ?? "";
                        var arguments = args?["arguments"] as JObject;
                        var prompt = await client.GetPromptAsync(name, arguments, ct).ConfigureAwait(false);
                        return prompt.ToText();
                    },
                    permission: AgentPermissions.Mcp)
            };
        }

        private static string FormatResources(IReadOnlyList<McpResource> resources)
        {
            if (resources == null || resources.Count == 0) return "(no resources)";
            var sb = new StringBuilder();
            foreach (var resource in resources)
            {
                sb.Append("- ").Append(resource.Uri);
                if (!string.IsNullOrEmpty(resource.Name))
                    sb.Append(" — ").Append(resource.Name);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatPrompts(IReadOnlyList<McpPrompt> prompts)
        {
            if (prompts == null || prompts.Count == 0) return "(no prompts)";
            var sb = new StringBuilder();
            foreach (var prompt in prompts)
            {
                sb.Append("- ").Append(prompt.Name);
                if (!string.IsNullOrEmpty(prompt.Description))
                    sb.Append(" — ").Append(prompt.Description);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
