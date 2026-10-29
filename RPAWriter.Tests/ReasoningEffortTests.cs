using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ReasoningEffortTests
    {
        [Test]
        public void Apply_Empty_OmitsReasoningParameters()
        {
            var payload = new JObject();
            ReasoningEffortMap.Apply(payload, "", "https://api.openai.com/v1", "gpt-4o-mini");

            Assert.That(payload.ContainsKey("reasoning_effort"), Is.False);
            Assert.That(payload.ContainsKey("thinking"), Is.False);
        }

        [TestCase("none", "none")]
        [TestCase("minimal", "minimal")]
        [TestCase("low", "low")]
        [TestCase("medium", "medium")]
        [TestCase("high", "high")]
        [TestCase("xhigh", "high")]
        [TestCase("max", "high")]
        public void Apply_OpenAi_MapsReasoningEffort(string effort, string expected)
        {
            var payload = new JObject();
            ReasoningEffortMap.Apply(payload, effort, "https://api.openai.com/v1", "gpt-4o-mini");

            Assert.That(payload["reasoning_effort"]?.ToString(), Is.EqualTo(expected));
        }

        [TestCase("minimal", "low")]
        [TestCase("low", "low")]
        [TestCase("medium", "high")]
        [TestCase("high", "high")]
        [TestCase("xhigh", "high")]
        [TestCase("max", "max")]
        public void Apply_DeepSeek_MapsReasoningEffort(string effort, string expected)
        {
            var payload = new JObject();
            ReasoningEffortMap.Apply(payload, effort, "https://api.deepseek.com", "deepseek-chat");

            Assert.That(payload["reasoning_effort"]?.ToString(), Is.EqualTo(expected));
            Assert.That(payload.ContainsKey("thinking"), Is.False);
        }

        [Test]
        public void Apply_DeepSeekNone_UsesThinkingToggle()
        {
            var payload = new JObject();
            ReasoningEffortMap.Apply(payload, "none", "https://api.deepseek.com", "deepseek-chat");

            Assert.That(payload["thinking"]?["type"]?.ToString(), Is.EqualTo("disabled"));
            Assert.That(payload.ContainsKey("reasoning_effort"), Is.False);
        }

        [Test]
        public void Apply_DeepSeekDetectedByModelName()
        {
            var payload = new JObject();
            ReasoningEffortMap.Apply(payload, "max", "https://example.com/v1", "deepseek-v4-pro");

            Assert.That(payload["reasoning_effort"]?.ToString(), Is.EqualTo("max"));
        }

        [Test]
        public void Normalize_InvalidValue_ReturnsDefault()
        {
            Assert.That(ReasoningEffortMap.Normalize(null), Is.EqualTo(""));
            Assert.That(ReasoningEffortMap.Normalize("bogus"), Is.EqualTo(""));
            Assert.That(ReasoningEffortMap.Normalize("high"), Is.EqualTo("high"));
            Assert.That(ReasoningEffortMap.Normalize("HIGH"), Is.EqualTo("high"));
        }

        [Test]
        public async Task ChatAsync_SendsReasoningEffort()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1",
                ApiKey = "sk-test",
                Model = "test-model",
                ReasoningEffort = "medium"
            }, new HttpClient(handler));

            await provider.ChatAsync("hello");

            var body = JObject.Parse(handler.LastBody);
            Assert.That(body["reasoning_effort"]?.ToString(), Is.EqualTo("medium"));
        }

        [Test]
        public async Task ChatAsync_DefaultReasoningEffort_OmitsParameter()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1",
                ApiKey = "sk-test",
                Model = "test-model",
                ReasoningEffort = ""
            }, new HttpClient(handler));

            await provider.ChatAsync("hello");

            var body = JObject.Parse(handler.LastBody);
            Assert.That(body.ContainsKey("reasoning_effort"), Is.False);
            Assert.That(body.ContainsKey("thinking"), Is.False);
        }

        private static string Completion()
        {
            var payload = new { choices = new[] { new { message = new { content = "ok" } } } };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }
    }
}
