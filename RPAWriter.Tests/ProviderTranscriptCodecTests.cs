using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ProviderTranscriptCodecTests
    {
        [Test]
        public void RoundTrip_PreservesNativeToolCallsAndToolResults()
        {
            var transcript = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "read a.txt" },
                new ChatMessageData
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = new List<NativeToolCall>
                    {
                        new NativeToolCall { Id = "call_1", Name = "read_file", Arguments = JObject.Parse("{\"path\":\"a.txt\"}") }
                    }
                },
                new ChatMessageData { Role = "tool", Content = "Tool \"read_file\" returned:\nFILE", ToolCallId = "call_1" },
                new ChatMessageData { Role = "assistant", Content = "done" }
            };

            var restored = ProviderTranscriptCodec.Deserialize(ProviderTranscriptCodec.Serialize(transcript));

            Assert.That(restored.Count, Is.EqualTo(4));
            Assert.That(restored[1].ToolCalls.Count, Is.EqualTo(1));
            Assert.That(restored[1].ToolCalls[0].Id, Is.EqualTo("call_1"));
            Assert.That(restored[1].ToolCalls[0].Name, Is.EqualTo("read_file"));
            Assert.That(restored[1].ToolCalls[0].Arguments["path"].Value<string>(), Is.EqualTo("a.txt"));
            Assert.That(restored[2].Role, Is.EqualTo("tool"));
            Assert.That(restored[2].ToolCallId, Is.EqualTo("call_1"));
            Assert.That(restored[3].Content, Is.EqualTo("done"));
        }

        [Test]
        public void RoundTrip_PreservesImages()
        {
            var transcript = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    Role = "user",
                    Content = "what is this",
                    Images = new List<ChatImagePart>
                    {
                        new ChatImagePart { MediaType = "image/png", DataBase64 = "aGVsbG8=" }
                    }
                }
            };

            var restored = ProviderTranscriptCodec.Deserialize(ProviderTranscriptCodec.Serialize(transcript));

            Assert.That(restored[0].Images.Count, Is.EqualTo(1));
            Assert.That(restored[0].Images[0].MediaType, Is.EqualTo("image/png"));
            Assert.That(restored[0].Images[0].DataBase64, Is.EqualTo("aGVsbG8="));
        }

        [Test]
        public void Serialize_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.That(ProviderTranscriptCodec.Serialize(null), Is.Empty);
            Assert.That(ProviderTranscriptCodec.Serialize(new List<ChatMessageData>()), Is.Empty);
        }

        [Test]
        public void Deserialize_NullEmptyOrInvalid_ReturnsEmptyList()
        {
            Assert.That(ProviderTranscriptCodec.Deserialize(null), Is.Empty);
            Assert.That(ProviderTranscriptCodec.Deserialize(""), Is.Empty);
            Assert.That(ProviderTranscriptCodec.Deserialize("   "), Is.Empty);
            Assert.That(ProviderTranscriptCodec.Deserialize("{ not json"), Is.Empty);
        }
    }
}
