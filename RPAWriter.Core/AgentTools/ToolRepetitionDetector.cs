using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Detects consecutive identical tool calls (same tool name + same arguments) to stop the
    /// agent from burning budget on a stagnant loop. Mirrors Cline/Roo-Code's
    /// <c>ToolRepetitionDetector</c>: only the call signature is compared (a deterministic tool
    /// re-invoked with the same arguments returns the same result/error anyway).
    /// </summary>
    public sealed class ToolRepetitionDetector
    {
        private readonly int _limit;
        private string _previousSignature;
        private int _consecutiveCount;

        /// <param name="limit">Maximum number of consecutive identical tool calls allowed; 0 disables detection.</param>
        public ToolRepetitionDetector(int limit = 3)
        {
            _limit = limit;
        }

        /// <summary>
        /// Reports the tool call about to be executed and returns true when it should be blocked
        /// because the same call was already repeated too many times in a row.
        /// </summary>
        public bool IsBlocked(string toolName, JObject arguments)
        {
            if (_limit <= 0)
                return false;

            var signature = BuildSignature(toolName, arguments);

            if (string.Equals(_previousSignature, signature, StringComparison.Ordinal))
            {
                _consecutiveCount++;
            }
            else
            {
                _consecutiveCount = 0;
                _previousSignature = signature;
            }

            if (_consecutiveCount >= _limit)
            {
                // Reset so a user-guided change of approach can recover past this point.
                _consecutiveCount = 0;
                _previousSignature = null;
                return true;
            }

            return false;
        }

        /// <summary>Builds a canonical, key-order-independent signature for a tool call.</summary>
        public static string BuildSignature(string toolName, JObject arguments)
        {
            return (toolName ?? "") + "\u0000" + Canonicalize(arguments ?? new JObject());
        }

        private static string Canonicalize(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "null";

            if (token is JObject obj)
            {
                var sorted = new JObject();
                foreach (var prop in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(prop.Name, Canonicalize(prop.Value));
                return sorted.ToString(Formatting.None);
            }

            if (token is JArray arr)
                return new JArray(arr.Select(Canonicalize)).ToString(Formatting.None);

            return token.ToString(Formatting.None);
        }
    }
}
