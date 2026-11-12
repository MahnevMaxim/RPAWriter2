using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Serializes the canonical provider transcript (<see cref="ChatMessageData"/>) to a JSON string and
    /// back, preserving the native tool-calling structure (tool_call_id + assistant tool_calls with
    /// id/name/arguments) and attached image parts. Used to persist the transcript with a conversation so
    /// a reopened chat resumes with the exact prefix and keeps the provider's prompt cache hot.
    /// </summary>
    public static class ProviderTranscriptCodec
    {
        /// <summary>Serializes the transcript to JSON; null/empty yields an empty string.</summary>
        public static string Serialize(IReadOnlyList<ChatMessageData> messages)
        {
            if (messages == null || messages.Count == 0)
                return string.Empty;

            return JsonConvert.SerializeObject(messages);
        }

        /// <summary>Deserializes the transcript; null/empty/invalid input yields an empty list.</summary>
        public static List<ChatMessageData> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<ChatMessageData>();

            try
            {
                return JsonConvert.DeserializeObject<List<ChatMessageData>>(json) ?? new List<ChatMessageData>();
            }
            catch
            {
                return new List<ChatMessageData>();
            }
        }
    }
}
