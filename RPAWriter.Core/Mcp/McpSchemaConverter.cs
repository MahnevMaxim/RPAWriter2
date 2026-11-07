using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Converts a standard MCP <c>inputSchema</c> (JSON Schema) into the informal parameter
    /// schema string used by <see cref="AgentTools.IAgentTool.ParametersSchema"/> — e.g.
    /// <c>{"url":"string","width?":"integer"}</c>. The informal form is what
    /// <see cref="AgentTools.ToolSchemaConverter"/> re-expands for native function calling and
    /// what the JSON-over-chat prompt prints, so MCP tools plug into the existing pipeline
    /// without changes. Nested objects/arrays are preserved structurally; enums collapse to
    /// an <c>a|b|c</c> string; descriptions/defaults are dropped (they are not representable
    /// in the informal format).
    /// </summary>
    public static class McpSchemaConverter
    {
        /// <summary>Converts a JSON Schema object to the informal schema string, or "{}" when empty.</summary>
        public static string ToInformal(JObject inputSchema)
        {
            if (inputSchema == null) return "{}";

            var properties = inputSchema["properties"] as JObject;
            if (properties == null || properties.Count == 0)
                return "{}";

            var required = new HashSet<string>();
            if (inputSchema["required"] is JArray requiredArray)
            {
                foreach (var item in requiredArray)
                    required.Add(item.Value<string>());
            }

            var informal = new JObject();
            foreach (var prop in properties.Properties())
            {
                var name = prop.Name;
                var optional = !required.Contains(name);
                if (optional)
                    name = name + "?";
                informal[name] = ConvertValue(prop.Value as JObject);
            }

            return informal.ToString(Formatting.None);
        }

        private static JToken ConvertValue(JObject schema)
        {
            if (schema == null) return "string";

            var type = (schema["type"]?.Value<string>() ?? "").Trim().ToLowerInvariant();

            switch (type)
            {
                case "integer":
                    return "integer";
                case "number":
                    return "number";
                case "boolean":
                    return "boolean";
                case "array":
                {
                    var items = schema["items"] as JObject;
                    return new JArray(ConvertValue(items));
                }
                case "object":
                {
                    var nestedProperties = schema["properties"] as JObject;
                    if (nestedProperties == null || nestedProperties.Count == 0)
                        return "object";
                    var obj = new JObject();
                    foreach (var prop in nestedProperties.Properties())
                        obj[prop.Name] = ConvertValue(prop.Value as JObject);
                    return obj;
                }
                case "string":
                default:
                {
                    if (schema["enum"] is JArray enumArray && enumArray.Count > 0)
                    {
                        var values = enumArray.Values<string>()
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Select(v => v.Trim());
                        return string.Join("|", values);
                    }
                    return "string";
                }
            }
        }
    }
}
