using System.Diagnostics;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class GitEnvironmentSanitizerTests
    {
        [Test]
        public void Sanitize_RemovesRedirectingGitVariables()
        {
            var psi = new ProcessStartInfo();
            foreach (var name in GitEnvironmentSanitizer.SanitizedVariables)
            {
                psi.Environment[name] = "test";
            }

            GitEnvironmentSanitizer.Sanitize(psi);

            foreach (var name in GitEnvironmentSanitizer.SanitizedVariables)
            {
                Assert.That(psi.Environment.ContainsKey(name), Is.False, "Should have removed " + name);
            }
        }

        [Test]
        public void Sanitize_LeavesUnrelatedVariablesIntact()
        {
            var psi = new ProcessStartInfo();
            psi.Environment["PATH"] = "unused";

            GitEnvironmentSanitizer.Sanitize(psi);

            Assert.That(psi.Environment.ContainsKey("PATH"), Is.True);
        }

        [Test]
        public void ForcedConfigArguments_ForceUtf8AndPlainPaths()
        {
            Assert.That(GitEnvironmentSanitizer.ForcedConfigArguments, Does.Contain("core.quotepath=false"));
            Assert.That(GitEnvironmentSanitizer.ForcedConfigArguments, Does.Contain("i18n.logOutputEncoding=UTF-8"));
            Assert.That(GitEnvironmentSanitizer.ForcedConfigArguments, Does.Contain("i18n.commitEncoding=UTF-8"));
        }

        [Test]
        public void WithForcedConfig_PrefixesGivenArguments()
        {
            Assert.That(
                GitEnvironmentSanitizer.WithForcedConfig("status --porcelain"),
                Is.EqualTo("-c core.quotepath=false -c i18n.logOutputEncoding=UTF-8 -c i18n.commitEncoding=UTF-8 status --porcelain"));
        }

        [Test]
        public void WithForcedConfig_NullOrWhitespace_ReturnsConfigOnly()
        {
            Assert.That(GitEnvironmentSanitizer.WithForcedConfig(null), Is.EqualTo(GitEnvironmentSanitizer.ForcedConfigArguments));
            Assert.That(GitEnvironmentSanitizer.WithForcedConfig(""), Is.EqualTo(GitEnvironmentSanitizer.ForcedConfigArguments));
            Assert.That(GitEnvironmentSanitizer.WithForcedConfig("   "), Is.EqualTo(GitEnvironmentSanitizer.ForcedConfigArguments));
        }
    }
}
