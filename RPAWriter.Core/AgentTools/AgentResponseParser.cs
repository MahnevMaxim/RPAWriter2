using System;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Parsed agent response: either a final answer or a tool call.</summary>
    public sealed class AgentResponse
    {
        public bool IsFinal { get; private set; }
        public string Text { get; private set; } = "";
        public string ToolName { get; private set; } = "";
        public JObject Arguments { get; private set; } = new JObject();

        public static AgentResponse Final(string text) =>
            new AgentResponse { IsFinal = true, Text = text ?? "" };

        public static AgentResponse ToolCall(string tool, JObject arguments) =>
            new AgentResponse { IsFinal = false, ToolName = tool ?? "", Arguments = arguments ?? new JObject() };
    }

    /// <summary>Parses the model's JSON reply into final / tool_call.</summary>
    public static class AgentResponseParser
    {
        public static AgentResponse Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return AgentResponse.Final("");

            var json = ExtractJson(content);
            if (json == null)
                return AgentResponse.Final(content.Trim());

            try
            {
                var obj = JObject.Parse(json);
                var action = obj.Value<string>("action");
                var tool = obj.Value<string>("tool") ?? "";

                if (string.Equals(action, "tool_call", StringComparison.OrdinalIgnoreCase))
                    return AgentResponse.ToolCall(tool, obj["arguments"] as JObject ?? new JObject());

                if (string.Equals(action, "final", StringComparison.OrdinalIgnoreCase))
                    return AgentResponse.Final(obj.Value<string>("text") ?? "");

                // Fall back: a "tool" field implies a tool call, otherwise treat as final.
                if (!string.IsNullOrWhiteSpace(tool))
                    return AgentResponse.ToolCall(tool, obj["arguments"] as JObject ?? new JObject());

                return AgentResponse.Final(obj.Value<string>("text") ?? content.Trim());
            }
            catch
            {
                return AgentResponse.Final(content.Trim());
            }
        }

        private static string ExtractJson(string content)
        {
            var text = content.Trim();

            // Strip markdown code fences if present.
            var fenceIdx = text.IndexOf("```", StringComparison.Ordinal);
            if (fenceIdx >= 0)
            {
                var afterFence = text.Substring(fenceIdx + 3);
                var nl = afterFence.IndexOf('\n');
                if (nl >= 0)
                    afterFence = afterFence.Substring(nl + 1);
                var end = afterFence.LastIndexOf("```", StringComparison.Ordinal);
                if (end >= 0)
                    afterFence = afterFence.Substring(0, end);
                text = afterFence.Trim();
            }

            // Take the first balanced JSON object.
            var start = text.IndexOf('{');
            if (start < 0)
                return null;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                }
                else
                {
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return text.Substring(start, i - start + 1);
                    }
                }
            }

            return null;
        }
    }
}
