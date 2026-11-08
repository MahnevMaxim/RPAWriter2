using System;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ProviderErrorExceptionTests
    {
        [Test]
        public void Ctor_ExposesKindMessageStatusCodeBodyAndInner()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new ProviderErrorException(ProviderErrorKind.RateLimit, "too fast", 429, "body", inner);

            Assert.That(ex.Kind, Is.EqualTo(ProviderErrorKind.RateLimit));
            Assert.That(ex.Message, Is.EqualTo("too fast"));
            Assert.That(ex.StatusCode, Is.EqualTo(429));
            Assert.That(ex.Body, Is.EqualTo("body"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void Ctor_WithoutStatusCodeAndBody_LeavesThemNull()
        {
            var ex = new ProviderErrorException(ProviderErrorKind.Auth, "denied");

            Assert.That(ex.Kind, Is.EqualTo(ProviderErrorKind.Auth));
            Assert.That(ex.StatusCode, Is.Null);
            Assert.That(ex.Body, Is.Null);
            Assert.That(ex.InnerException, Is.Null);
        }

        [Test]
        public void ProviderErrorKind_ContainsExpectedValues()
        {
            var names = Enum.GetNames(typeof(ProviderErrorKind));

            Assert.That(
                names,
                Is.EquivalentTo(new[]
                {
                    "None", "Auth", "RateLimit", "ContextWindowExceeded",
                    "ContentFilter", "Transient", "Other"
                }));
        }

        [Test]
        public void FriendlyMessage_Auth_DescribesApiKeyOrAccess()
        {
            Assert.That(new ProviderErrorException(ProviderErrorKind.Auth, "x", 401).FriendlyMessage, Does.Contain("API key"));
            Assert.That(new ProviderErrorException(ProviderErrorKind.Auth, "x", 403).FriendlyMessage, Does.Contain("403"));
        }

        [Test]
        public void FriendlyMessage_Other400WithToolBody_HintsAtToolSync()
        {
            var ex = new ProviderErrorException(
                ProviderErrorKind.Other,
                "x",
                400,
                "Messages with role 'tool' must be a response to a preceding message with 'tool_calls'");

            Assert.That(ex.FriendlyMessage, Does.Contain("tool result is out of sync"));
        }

        [Test]
        public void FriendlyMessage_ContextWindowExceeded_HintsAtCompaction()
        {
            var ex = new ProviderErrorException(ProviderErrorKind.ContextWindowExceeded, "x");

            Assert.That(ex.FriendlyMessage, Does.Contain("Auto-compact"));
        }

        [Test]
        public void FriendlyMessage_RateLimit_Includes429()
        {
            var ex = new ProviderErrorException(ProviderErrorKind.RateLimit, "x", 429);

            Assert.That(ex.FriendlyMessage, Does.Contain("429"));
        }
    }
}
