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
    }
}
