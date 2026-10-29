using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SettingsStoreTests
    {
        [Test]
        public void Normalize_Null_DoesNothing()
        {
            Assert.DoesNotThrow(() => SettingsStore.Normalize(null));
        }

        [Test]
        public void Normalize_FillsEmptyRulesPath()
        {
            var settings = new SettingsData { RulesPath = "   " };

            SettingsStore.Normalize(settings);

            Assert.That(settings.RulesPath, Is.EqualTo(PromptPaths.ResolveRulesDirectory()));
        }

        [Test]
        public void Normalize_ClampsNonPositiveCounts()
        {
            var settings = new SettingsData
            {
                ContextWindowSize = 0,
                TimeoutSeconds = -1,
                RetryTimeoutMinutes = 0,
                RetryBaseDelaySeconds = -5,
                RetryMaxDelaySeconds = 0,
                KeepRecentMessages = -2,
                ToolResultMaxChars = 0,
                MaxIterations = -1,
                SubagentMaxIterations = 0
            };

            SettingsStore.Normalize(settings);

            Assert.That(settings.ContextWindowSize, Is.EqualTo(409600));
            Assert.That(settings.TimeoutSeconds, Is.EqualTo(300));
            Assert.That(settings.RetryTimeoutMinutes, Is.EqualTo(30));
            Assert.That(settings.RetryBaseDelaySeconds, Is.EqualTo(1));
            Assert.That(settings.RetryMaxDelaySeconds, Is.EqualTo(30));
            Assert.That(settings.KeepRecentMessages, Is.EqualTo(6));
            Assert.That(settings.ToolResultMaxChars, Is.EqualTo(12000));
            Assert.That(settings.MaxIterations, Is.EqualTo(8));
            Assert.That(settings.SubagentMaxIterations, Is.EqualTo(4));
        }

        [Test]
        public void Normalize_ClampsTemperature()
        {
            var tooHigh = new SettingsData { Temperature = 1.5 };
            SettingsStore.Normalize(tooHigh);
            Assert.That(tooHigh.Temperature, Is.EqualTo(0.2));

            var tooLow = new SettingsData { Temperature = -0.1 };
            SettingsStore.Normalize(tooLow);
            Assert.That(tooLow.Temperature, Is.EqualTo(0.2));

            var nan = new SettingsData { Temperature = double.NaN };
            SettingsStore.Normalize(nan);
            Assert.That(nan.Temperature, Is.EqualTo(0.2));
        }

        [Test]
        public void Normalize_ClampsNegativePrices()
        {
            var settings = new SettingsData
            {
                InputPricePerMillion = -1,
                CacheHitPricePerMillion = -2,
                OutputPricePerMillion = -3
            };

            SettingsStore.Normalize(settings);

            Assert.That(settings.InputPricePerMillion, Is.EqualTo(0.27));
            Assert.That(settings.CacheHitPricePerMillion, Is.EqualTo(0.07));
            Assert.That(settings.OutputPricePerMillion, Is.EqualTo(1.10));
        }

        [Test]
        public void Normalize_ClampsFractions()
        {
            var settings = new SettingsData { CompactThreshold = 2.0, ReserveFraction = 0.9 };

            SettingsStore.Normalize(settings);

            Assert.That(settings.CompactThreshold, Is.EqualTo(0.85));
            Assert.That(settings.ReserveFraction, Is.EqualTo(0.12));
        }

        [Test]
        public void Normalize_ClampsSubagentDepth()
        {
            var tooShallow = new SettingsData { SubagentMaxDepth = 0 };
            SettingsStore.Normalize(tooShallow);
            Assert.That(tooShallow.SubagentMaxDepth, Is.EqualTo(2));

            var tooDeep = new SettingsData { SubagentMaxDepth = 9 };
            SettingsStore.Normalize(tooDeep);
            Assert.That(tooDeep.SubagentMaxDepth, Is.EqualTo(5));
        }

        [Test]
        public void Normalize_ResetsInvalidEnumStrings()
        {
            var settings = new SettingsData
            {
                ToolCallingMode = "Bogus",
                MemoryBankMode = "Bogus",
                SecurityAuditMode = "Bogus",
                SpeechToTextProvider = "Bogus"
            };

            SettingsStore.Normalize(settings);

            Assert.That(settings.ToolCallingMode, Is.EqualTo("JsonChat"));
            Assert.That(settings.MemoryBankMode, Is.EqualTo("Disabled"));
            Assert.That(settings.SecurityAuditMode, Is.EqualTo("Full"));
            Assert.That(settings.SpeechToTextProvider, Is.EqualTo("Auto"));
        }

        [Test]
        public void Normalize_FillsEmptySpeechDefaults()
        {
            var settings = new SettingsData { SpeechToTextModel = "", SpeechToTextLanguage = " " };

            SettingsStore.Normalize(settings);

            Assert.That(settings.SpeechToTextModel, Is.EqualTo("whisper-1"));
            Assert.That(settings.SpeechToTextLanguage, Is.EqualTo("ru"));
        }

        [Test]
        public void Normalize_InitializesNullRobotNameHistory()
        {
            var settings = new SettingsData { RobotNameHistory = null };

            SettingsStore.Normalize(settings);

            Assert.That(settings.RobotNameHistory, Is.Not.Null);
            Assert.That(settings.RobotNameHistory, Is.Empty);
        }

        [Test]
        public void Normalize_PreservesValidValues()
        {
            var settings = new SettingsData
            {
                ContextWindowSize = 128000,
                Temperature = 0.5,
                TimeoutSeconds = 60,
                KeepRecentMessages = 10,
                ToolCallingMode = "Native",
                MemoryBankMode = "Tool",
                SecurityAuditMode = "CredentialsOnly",
                SpeechToTextProvider = "Vosk"
            };

            SettingsStore.Normalize(settings);

            Assert.That(settings.ContextWindowSize, Is.EqualTo(128000));
            Assert.That(settings.Temperature, Is.EqualTo(0.5));
            Assert.That(settings.TimeoutSeconds, Is.EqualTo(60));
            Assert.That(settings.KeepRecentMessages, Is.EqualTo(10));
            Assert.That(settings.ToolCallingMode, Is.EqualTo("Native"));
            Assert.That(settings.MemoryBankMode, Is.EqualTo("Tool"));
            Assert.That(settings.SecurityAuditMode, Is.EqualTo("CredentialsOnly"));
            Assert.That(settings.SpeechToTextProvider, Is.EqualTo("Vosk"));
        }

        [Test]
        public void Normalize_ResetsInvalidReasoningEffort()
        {
            var settings = new SettingsData { ReasoningEffort = "Bogus" };
            SettingsStore.Normalize(settings);
            Assert.That(settings.ReasoningEffort, Is.EqualTo(""));

            var valid = new SettingsData { ReasoningEffort = "max" };
            SettingsStore.Normalize(valid);
            Assert.That(valid.ReasoningEffort, Is.EqualTo("max"));
        }

        [Test]
        public void SettingsFile_EndsWithSettingsJson()
        {
            Assert.That(SettingsStore.SettingsFile, Does.EndWith("settings.json"));
        }
    }
}
