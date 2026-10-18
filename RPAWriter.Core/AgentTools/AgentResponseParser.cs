using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>A single tool invocation requested by the agent (name + arguments).</summary>
    public sealed class AgentToolCallSpec
    {
        public string ToolName { get; set; } = "";
        public JObject Arguments { get; set; } = new JObject();
    }

    /// <summary>Parsed agent response: either a final answer or one/more tool calls.</summary>
    public sealed class AgentResponse
    {
        public bool IsFinal { get; private set; }
        public string Text { get; private set; } = "";
        public IReadOnlyList<AgentToolCallSpec> ToolCalls { get; private set; } = new List<AgentToolCallSpec>();

        public static AgentResponse Final(string text) =>
            new AgentResponse { IsFinal = true, Text = text ?? "" };

        public static AgentResponse ToolCall(IReadOnlyList<AgentToolCallSpec> calls) =>
            new AgentResponse { IsFinal = false, ToolCalls = calls ?? new List<AgentToolCallSpec>() };
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
                // A top-level array means several tool calls in one turn.
                if (json.TrimStart().StartsWith("["))
                {
                    var calls = new List<AgentToolCallSpec>();
                    foreach (var item in JArray.Parse(json))
                    {
                        var call = ParseCall(item as JObject);
                        if (call != null)
                            calls.Add(call);
                    }
                    return calls.Count > 0
                        ? AgentResponse.ToolCall(calls)
                        : AgentResponse.Final(content.Trim());
                }

                var obj = JObject.Parse(json);
                var action = obj.Value<string>("action");
                var tool = obj.Value<string>("tool") ?? "";

                if (string.Equals(action, "tool_call", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(tool))
                {
                    var call = ParseCall(obj);
                    return call != null
                        ? AgentResponse.ToolCall(new List<AgentToolCallSpec> { call })
                        : AgentResponse.Final(content.Trim());
                }

                if (string.Equals(action, "final", StringComparison.OrdinalIgnoreCase))
                    return AgentResponse.Final(obj.Value<string>("text") ?? "");

                return AgentResponse.Final(obj.Value<string>("text") ?? content.Trim());
            }
            catch
            {
                return AgentResponse.Final(content.Trim());
            }
        }

        private static AgentToolCallSpec ParseCall(JObject obj)
        {
            if (obj == null) return null;
            var tool = obj.Value<string>("tool") ?? "";
            if (string.IsNullOrWhiteSpace(tool)) return null;
            return new AgentToolCallSpec
            {
                ToolName = tool,
                Arguments = obj["arguments"] as JObject ?? new JObject()
            };
        }

        private static string ExtractJson(string content)
        {
            var text = content.Trim();

            // Take the first balanced JSON value (object or array). Markdown code fences (```)
            // are ignored whether they wrap the JSON or appear inside a "text" value, because
            // braces/brackets inside string values are skipped by the scan below.
            var objStart = text.IndexOf('{');
            var arrStart = text.IndexOf('[');
            int start;
            if (objStart < 0) start = arrStart;
            else if (arrStart < 0) start = objStart;
            else start = Math.Min(objStart, arrStart);
            if (start < 0)
                return null;

            var open = text[start];
            var close = open == '{' ? '}' : ']';
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
                    else if (c == open) depth++;
                    else if (c == close)
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
