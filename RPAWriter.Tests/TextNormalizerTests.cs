using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class TextNormalizerTests
    {
        [Test]
        public void Tokenize_NullOrWhitespace_ReturnsEmpty()
        {
            Assert.That(TextNormalizer.Tokenize(null), Is.Empty);
            Assert.That(TextNormalizer.Tokenize(""), Is.Empty);
            Assert.That(TextNormalizer.Tokenize("   "), Is.Empty);
        }

        [Test]
        public void Tokenize_RemovesStopWordsAndShortTokens()
        {
            var tokens = TextNormalizer.Tokenize("Как открыть браузер the page");

            Assert.That(tokens, Does.Not.Contain("как"));   // RU stop word
            Assert.That(tokens, Does.Not.Contain("the"));   // EN stop word
            Assert.That(tokens, Does.Contain("открыть"));
            Assert.That(tokens, Does.Contain("браузер"));
            Assert.That(tokens, Does.Contain("page"));
        }

        [Test]
        public void Tokenize_LowercasesTokens()
        {
            var tokens = TextNormalizer.Tokenize("OpenBrowser SAP VA01");

            Assert.That(tokens, Does.Contain("openbrowser"));
            Assert.That(tokens, Does.Contain("sap"));
            Assert.That(tokens, Does.Contain("va01"));
        }

        [Test]
        public void NormalizeKeywords_Null_ReturnsEmpty()
        {
            Assert.That(TextNormalizer.NormalizeKeywords(null), Is.Empty);
        }

        [Test]
        public void NormalizeKeywords_DeduplicatesCaseInsensitively()
        {
            var result = TextNormalizer.NormalizeKeywords(new[] { "Открыть браузер", "открыть браузер", "the page" });

            Assert.That(result, Is.Unique);
            Assert.That(result, Does.Contain("открыть"));
            Assert.That(result, Does.Contain("браузер"));
            Assert.That(result, Does.Contain("page"));
        }
    }
}
