using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>A single content block inside a tool call result (text, image, resource, …).</summary>
    public sealed class McpContentBlock
    {
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public string Data { get; set; } = "";
        public string MimeType { get; set; } = "";
        public string ResourceUri { get; set; } = "";

        public static McpContentBlock FromJson(JToken token)
        {
            var block = new McpContentBlock();
            if (token is JObject obj)
            {
                block.Type = obj["type"]?.Value<string>() ?? "";
                block.Text = obj["text"]?.Value<string>() ?? "";
                block.Data = obj["data"]?.Value<string>() ?? "";
                block.MimeType = obj["mimeType"]?.Value<string>() ?? "";
                block.ResourceUri = (obj["resource"]?["uri"] ?? obj["uri"])?.Value<string>() ?? "";
            }
            return block;
        }
    }

    /// <summary>The result of an MCP <c>tools/call</c>.</summary>
    public sealed class McpCallToolResult
    {
        public bool IsError { get; set; }
        public IReadOnlyList<McpContentBlock> Content { get; set; } = new List<McpContentBlock>();

        public static McpCallToolResult FromJson(JObject json)
        {
            var result = new McpCallToolResult();
            if (json == null) return result;

            result.IsError = json["isError"]?.Value<bool>() ?? false;

            var blocks = new List<McpContentBlock>();
            if (json["content"] is JArray contentArray)
            {
                foreach (var item in contentArray)
                    blocks.Add(McpContentBlock.FromJson(item));
            }
            else if (json["structuredContent"] != null)
            {
                blocks.Add(new McpContentBlock
                {
                    Type = "text",
                    Text = json["structuredContent"].ToString(Newtonsoft.Json.Formatting.Indented)
                });
            }
            result.Content = blocks;
            return result;
        }
    }

    /// <summary>An MCP resource as reported by <c>resources/list</c>.</summary>
    public sealed class McpResource
    {
        public string Uri { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string MimeType { get; set; } = "";

        public static IReadOnlyList<McpResource> FromList(JToken resources)
        {
            var list = new List<McpResource>();
            if (resources is JArray array)
            {
                foreach (var item in array)
                {
                    if (item is JObject obj)
                        list.Add(new McpResource
                        {
                            Uri = obj["uri"]?.Value<string>() ?? "",
                            Name = obj["name"]?.Value<string>() ?? "",
                            Description = obj["description"]?.Value<string>() ?? "",
                            MimeType = obj["mimeType"]?.Value<string>() ?? ""
                        });
                }
            }
            return list;
        }
    }

    /// <summary>An MCP prompt as reported by <c>prompts/list</c>.</summary>
    public sealed class McpPrompt
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        public static IReadOnlyList<McpPrompt> FromList(JToken prompts)
        {
            var list = new List<McpPrompt>();
            if (prompts is JArray array)
            {
                foreach (var item in array)
                {
                    if (item is JObject obj)
                        list.Add(new McpPrompt
                        {
                            Name = obj["name"]?.Value<string>() ?? "",
                            Description = obj["description"]?.Value<string>() ?? ""
                        });
                }
            }
            return list;
        }
    }

    /// <summary>An MCP JSON-RPC error (code + message), surfaced as an exception by <see cref="McpClient"/>.</summary>
    public sealed class McpException : Exception
    {
        public int Code { get; }

        public McpException(int code, string message)
            : base(string.IsNullOrWhiteSpace(message) ? "MCP error " + code : message)
        {
            Code = code;
        }

        public static McpException FromJson(JToken error)
        {
            if (error is JObject obj)
            {
                var code = obj["code"]?.Value<int>() ?? -1;
                var message = obj["message"]?.Value<string>() ?? "";
                return new McpException(code, message);
            }
            return new McpException(-1, error?.ToString() ?? "unknown MCP error");
        }
    }
}
