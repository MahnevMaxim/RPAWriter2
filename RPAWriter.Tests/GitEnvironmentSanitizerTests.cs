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
    }
}
