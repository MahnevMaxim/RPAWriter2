using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Mcp
{
    /// <summary>The result of an MCP <c>resources/read</c> call.</summary>
    public sealed class McpResourceContent
    {
        public IReadOnlyList<McpContentBlock> Contents { get; set; } = new List<McpContentBlock>();

        public static McpResourceContent FromJson(JObject json)
        {
            var result = new McpResourceContent();
            if (json == null) return result;

            var blocks = new List<McpContentBlock>();
            if (json["contents"] is JArray contents)
            {
                foreach (var item in contents)
                {
                    if (item is JObject obj)
                    {
                        var uri = obj["uri"]?.Value<string>() ?? "";
                        if (obj["text"] != null)
                        {
                            blocks.Add(new McpContentBlock { Type = "text", Text = obj["text"].Value<string>() ?? "", ResourceUri = uri });
                        }
                        else if (obj["blob"] != null)
                        {
                            blocks.Add(new McpContentBlock { Type = "image", Data = obj["blob"].Value<string>() ?? "", MimeType = obj["mimeType"]?.Value<string>() ?? "", ResourceUri = uri });
                        }
                        else
                        {
                            blocks.Add(new McpContentBlock { Type = "resource", ResourceUri = uri });
                        }
                    }
                }
            }
            result.Contents = blocks;
            return result;
        }

        /// <summary>Flattens the resource contents to plain text (binary blobs become placeholders).</summary>
        public string ToText() => McpToolResultParser.ToText(new McpCallToolResult { Content = Contents, IsError = false });
    }

    /// <summary>A single message returned by an MCP <c>prompts/get</c> call.</summary>
    public sealed class McpPromptMessage
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>The result of an MCP <c>prompts/get</c> call.</summary>
    public sealed class McpPromptResult
    {
        public string Description { get; set; } = "";
        public IReadOnlyList<McpPromptMessage> Messages { get; set; } = new List<McpPromptMessage>();

        public static McpPromptResult FromJson(JObject json)
        {
            var result = new McpPromptResult();
            if (json == null) return result;

            result.Description = json["description"]?.Value<string>() ?? "";

            var messages = new List<McpPromptMessage>();
            if (json["messages"] is JArray array)
            {
                foreach (var item in array)
                {
                    if (item is JObject msg)
                        messages.Add(new McpPromptMessage
                        {
                            Role = msg["role"]?.Value<string>() ?? "",
                            Text = ExtractText(msg["content"])
                        });
                }
            }
            result.Messages = messages;
            return result;
        }

        /// <summary>Flattens the prompt messages to a readable text form.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (var message in Messages)
            {
                if (!string.IsNullOrEmpty(message.Role))
                    sb.Append('[').Append(message.Role).Append("] ");
                sb.AppendLine(message.Text ?? "");
            }
            return sb.ToString().TrimEnd();
        }

        private static string ExtractText(JToken content)
        {
            if (content == null) return "";
            if (content.Type == JTokenType.String) return content.Value<string>();
            if (content is JObject obj && string.Equals(obj["type"]?.Value<string>(), "text", StringComparison.Ordinal))
                return obj["text"]?.Value<string>() ?? "";
            return content.ToString();
        }
    }
}
