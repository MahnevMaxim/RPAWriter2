using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>An MCP tool as reported by <c>tools/list</c>.</summary>
    public sealed class McpTool
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public JObject InputSchema { get; set; } = new JObject();

        public static McpTool FromJson(JObject json)
        {
            if (json == null) return new McpTool();
            return new McpTool
            {
                Name = json["name"]?.Value<string>() ?? "",
                Description = json["description"]?.Value<string>() ?? "",
                InputSchema = (json["inputSchema"] as JObject) ?? new JObject()
            };
        }

        public static IReadOnlyList<McpTool> FromList(JToken tools)
        {
            var list = new List<McpTool>();
            if (tools is JArray array)
            {
                foreach (var item in array)
                    list.Add(FromJson(item as JObject));
            }
            return list;
        }
    }
}
