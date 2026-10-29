using System;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Maps the user-facing "reasoning effort" level (Cline-style) to provider-specific
    /// request parameters. OpenAI-compatible endpoints receive <c>reasoning_effort</c>;
    /// DeepSeek uses a <c>thinking</c> toggle plus a narrower <c>reasoning_effort</c> set
    /// (low/high/max).
    /// </summary>
    public static class ReasoningEffortMap
    {
        public const string Default = "";
        public const string None = "none";
        public const string Minimal = "minimal";
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string ExtraHigh = "xhigh";
        public const string Max = "max";

        /// <summary>All UI values in dropdown order (index matches the combo box).</summary>
        public static readonly string[] All =
        {
            Default, None, Minimal, Low, Medium, High, ExtraHigh, Max
        };

        /// <summary>True when the value is empty or one of the known effort levels.</summary>
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;
            foreach (var candidate in All)
            {
                if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Returns the canonical value when valid, otherwise <see cref="Default"/> (empty).</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return Default;
            foreach (var candidate in All)
            {
                if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return Default;
        }

        /// <summary>True when the configured endpoint is DeepSeek (detected by URL or model name).</summary>
        public static bool IsDeepSeek(string baseUrl, string model)
        {
            var haystack = (baseUrl ?? "") + " " + (model ?? "");
            return haystack.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Applies the effort level to the outgoing chat-completions payload.</summary>
        public static void Apply(JObject payload, string effort, string baseUrl, string model)
        {
            if (payload == null)
                return;

            var value = Normalize(effort);
            if (value.Length == 0)
                return; // "Default" → do not send any reasoning parameter.

            if (IsDeepSeek(baseUrl, model))
            {
                if (string.Equals(value, None, StringComparison.OrdinalIgnoreCase))
                {
                    // DeepSeek disables thinking via the "thinking" toggle, not reasoning_effort.
                    payload["thinking"] = new JObject { ["type"] = "disabled" };
                    return;
                }

                payload["reasoning_effort"] = MapDeepSeek(value);
                return;
            }

            payload["reasoning_effort"] = MapOpenAi(value);
        }

        private static string MapDeepSeek(string value)
        {
            // DeepSeek accepts low/high/max; normalize the finer levels to its canonical set.
            if (string.Equals(value, Minimal, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, Low, StringComparison.OrdinalIgnoreCase))
                return Low;
            if (string.Equals(value, Max, StringComparison.OrdinalIgnoreCase))
                return Max;
            return High; // medium / high / xhigh
        }

        private static string MapOpenAi(string value)
        {
            // OpenAI reasoning models accept none/minimal/low/medium/high.
            if (string.Equals(value, None, StringComparison.OrdinalIgnoreCase)) return None;
            if (string.Equals(value, Minimal, StringComparison.OrdinalIgnoreCase)) return Minimal;
            if (string.Equals(value, Low, StringComparison.OrdinalIgnoreCase)) return Low;
            if (string.Equals(value, Medium, StringComparison.OrdinalIgnoreCase)) return Medium;
            return High; // high / xhigh / max → high (OpenAI has no higher levels)
        }
    }
}
