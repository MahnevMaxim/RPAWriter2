using System;
using System.Reflection;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class AiProviderFactoryTests
    {
        [Test]
        public void Create_NullSettings_ReturnsStub()
        {
            Assert.That(AiProviderFactory.Create(null), Is.InstanceOf<StubAiProvider>());
        }

        [Test]
        public void Create_EmptyOrWhitespaceApiKey_ReturnsStub()
        {
            Assert.That(AiProviderFactory.Create(new SettingsData { ApiKey = "" }), Is.InstanceOf<StubAiProvider>());
            Assert.That(AiProviderFactory.Create(new SettingsData { ApiKey = "   " }), Is.InstanceOf<StubAiProvider>());
        }

        [Test]
        public void Create_WithApiKey_ReturnsOpenAiProvider()
        {
            Assert.That(AiProviderFactory.Create(new SettingsData { ApiKey = "sk-test" }), Is.InstanceOf<OpenAiCompatibleProvider>());
        }

        [Test]
        public void Create_MapsSettingsToOptions()
        {
            var settings = new SettingsData
            {
                BaseUrl = "https://example.com/v1",
                ApiKey = "sk-test",
                Model = "test-model",
                ToolCallingMode = "Native",
                ContextWindowSize = 64000,
                TimeoutSeconds = 60,
                RetryTimeoutMinutes = 10,
                RetryBaseDelaySeconds = 2,
                RetryMaxDelaySeconds = 15,
                InputPricePerMillion = 0.5,
                CacheHitPricePerMillion = 0.1,
                OutputPricePerMillion = 2.0,
                Temperature = 0.7
            };

            var options = GetOptions(AiProviderFactory.Create(settings));

            Assert.That(options.BaseUrl, Is.EqualTo("https://example.com/v1"));
            Assert.That(options.ApiKey, Is.EqualTo("sk-test"));
            Assert.That(options.Model, Is.EqualTo("test-model"));
            Assert.That(options.ToolCallingMode, Is.EqualTo(ToolCallingMode.Native));
            Assert.That(options.ContextWindowSize, Is.EqualTo(64000));
            Assert.That(options.TimeoutSeconds, Is.EqualTo(60));
            Assert.That(options.RetryTimeout, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(options.RetryBaseDelay, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(options.RetryMaxDelay, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(options.InputPricePerMillion, Is.EqualTo(0.5));
            Assert.That(options.CacheHitPricePerMillion, Is.EqualTo(0.1));
            Assert.That(options.OutputPricePerMillion, Is.EqualTo(2.0));
            Assert.That(options.Temperature, Is.EqualTo(0.7));
        }

        [Test]
        public void Create_UsesDefaultsForBlankValues()
        {
            var settings = new SettingsData { ApiKey = "sk-test", BaseUrl = "", Model = "  " };

            var options = GetOptions(AiProviderFactory.Create(settings));

            Assert.That(options.BaseUrl, Is.EqualTo("https://api.openai.com/v1"));
            Assert.That(options.Model, Is.EqualTo("gpt-4o-mini"));
        }

        [Test]
        public void Create_ClampsTemperatureToRange()
        {
            var high = GetOptions(AiProviderFactory.Create(new SettingsData { ApiKey = "k", Temperature = 5.0 }));
            Assert.That(high.Temperature, Is.EqualTo(1.0));

            var low = GetOptions(AiProviderFactory.Create(new SettingsData { ApiKey = "k", Temperature = -3.0 }));
            Assert.That(low.Temperature, Is.EqualTo(0.0));
        }

        private static OpenAiOptions GetOptions(IAiProvider provider)
        {
            var field = typeof(OpenAiCompatibleProvider).GetField(
                "_options", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "OpenAiCompatibleProvider._options field not found");
            return (OpenAiOptions)field.GetValue(provider);
        }
    }
}
