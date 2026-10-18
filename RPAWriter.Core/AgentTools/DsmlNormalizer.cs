using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Normalizes tool-call markup (DSML/XML) emitted by some models into the
    /// JSON-over-chat protocol understood by <see cref="AgentResponseParser"/>.
    /// </summary>
    internal static class DsmlNormalizer
    {
        private static readonly Regex MarkupRegex = new Regex(
            @"<\s*[^>]{0,40}?\b(invoke|parameter|tool_call|function_call|dsml|calls|result)\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex InvokeRegex = new Regex(
            @"<(?:[^>]*\b)?invoke\b[^>]*>(.*?)</(?:[^>]*\b)?invoke\b[^>]*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex ParameterRegex = new Regex(
            @"<(?:[^>]*\b)?parameter\b[^>]*?name\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</(?:[^>]*\b)?parameter\b[^>]*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>True when the content contains tool-call markup rather than plain JSON.</summary>
        public static bool LooksLikeMarkup(string content)
        {
            return !string.IsNullOrWhiteSpace(content) && MarkupRegex.IsMatch(content);
        }

        /// <summary>
        /// Converts DSML/XML tool-call markup into a JSON array (or single object) of
        /// tool_call entries. Returns null when no tool call could be recovered.
        /// </summary>
        public static string Normalize(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var calls = new List<JObject>();
            foreach (Match invoke in InvokeRegex.Matches(content))
            {
                var tool = string.Empty;
                JObject arguments = null;

                foreach (Match parameter in ParameterRegex.Matches(invoke.Groups[1].Value))
                {
                    var name = (parameter.Groups[1].Value ?? string.Empty).Trim();
                    var value = (parameter.Groups[2].Value ?? string.Empty).Trim();

                    if (name.Equals("tool", StringComparison.OrdinalIgnoreCase))
                        tool = value;
                    else if (name.Equals("arguments", StringComparison.OrdinalIgnoreCase))
                        arguments = ParseArguments(value);
                }

                if (string.IsNullOrWhiteSpace(tool))
                    continue;

                calls.Add(new JObject
                {
                    ["action"] = "tool_call",
                    ["tool"] = tool,
                    ["arguments"] = arguments ?? new JObject()
                });
            }

            if (calls.Count == 0)
                return null;

            if (calls.Count == 1)
                return calls[0].ToString();

            var array = new JArray();
            foreach (var call in calls)
                array.Add(call);
            return array.ToString();
        }

        private static JObject ParseArguments(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new JObject();

            var json = value.Trim();
            if (json.StartsWith("{"))
            {
                try
                {
                    return JObject.Parse(json) ?? new JObject();
                }
                catch
                {
                    return new JObject();
                }
            }

            return new JObject { ["value"] = value };
        }
    }
}
