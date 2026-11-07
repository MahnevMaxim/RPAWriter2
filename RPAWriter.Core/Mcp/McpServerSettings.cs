using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Parses and serializes the widely used <c>mcpServers</c> JSON shape so a Cline/Roo
    /// configuration can be pasted in unchanged:
    /// <c>{ "mcpServers": { "name": { "command", "args", "env", "url", "type", "disabled" } } }</c>.
    /// A bare object without the <c>mcpServers</c> wrapper is also accepted.
    /// </summary>
    public static class McpServerSettings
    {
        public static IReadOnlyList<McpServerConfig> Parse(string json)
        {
            var list = new List<McpServerConfig>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch
            {
                return list;
            }

            var servers = root["mcpServers"] as JObject;
            if (servers == null)
            {
                // Tolerate a bare { "name": {...} } object without the wrapper.
                if (root.Properties().Any(p => p.Value is JObject))
                    servers = root;
            }

            if (servers == null)
                return list;

            foreach (var prop in servers.Properties())
            {
                var config = ParseServer(prop.Name, prop.Value as JObject);
                if (config != null)
                    list.Add(config);
            }

            return list;
        }

        public static string ToJson(IEnumerable<McpServerConfig> servers)
        {
            var serversObject = new JObject();
            if (servers != null)
            {
                foreach (var server in servers)
                {
                    if (server == null || string.IsNullOrWhiteSpace(server.Name))
                        continue;
                    serversObject[server.Name] = ToJsonServer(server);
                }
            }

            var root = new JObject { ["mcpServers"] = serversObject };
            return root.ToString(Formatting.Indented);
        }

        private static McpServerConfig ParseServer(string name, JObject obj)
        {
            if (obj == null)
                return null;

            var config = new McpServerConfig
            {
                Name = name,
                Command = obj["command"]?.Value<string>() ?? "",
                Url = obj["url"]?.Value<string>() ?? "",
                Disabled = obj["disabled"]?.Value<bool>() ?? false,
                TimeoutSeconds = obj["timeout"]?.Value<int>() ?? 30
            };

            if (obj["args"] is JArray args)
            {
                foreach (var item in args)
                    config.Args.Add(item.Value<string>() ?? "");
            }

            if (obj["env"] is JObject env)
            {
                foreach (var prop in env.Properties())
                    config.Env[prop.Name] = prop.Value?.Value<string>() ?? "";
            }

            config.Type = ResolveTransportType(obj["type"]?.Value<string>(), config.Url);
            return config;
        }

        private static JObject ToJsonServer(McpServerConfig server)
        {
            var obj = new JObject();

            if (!string.IsNullOrWhiteSpace(server.Command))
                obj["command"] = server.Command;
            if (server.Args != null && server.Args.Count > 0)
                obj["args"] = new JArray(server.Args);
            if (server.Env != null && server.Env.Count > 0)
                obj["env"] = JObject.FromObject(server.Env);
            if (!string.IsNullOrWhiteSpace(server.Url))
                obj["url"] = server.Url;
            if (server.Type != McpTransportType.Stdio)
                obj["type"] = server.Type == McpTransportType.Sse ? "sse" : "http";
            if (server.Disabled)
                obj["disabled"] = true;
            if (server.TimeoutSeconds != 30)
                obj["timeout"] = server.TimeoutSeconds;

            return obj;
        }

        private static McpTransportType ResolveTransportType(string type, string url)
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (type.Trim().Equals("sse", StringComparison.OrdinalIgnoreCase))
                    return McpTransportType.Sse;
                if (type.Trim().Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    type.Trim().Equals("streamablehttp", StringComparison.OrdinalIgnoreCase) ||
                    type.Trim().Equals("streamable-http", StringComparison.OrdinalIgnoreCase))
                    return McpTransportType.StreamableHttp;
                if (type.Trim().Equals("stdio", StringComparison.OrdinalIgnoreCase))
                    return McpTransportType.Stdio;
            }

            // A URL without an explicit type implies an HTTP transport.
            return string.IsNullOrWhiteSpace(url) ? McpTransportType.Stdio : McpTransportType.StreamableHttp;
        }
    }
}
