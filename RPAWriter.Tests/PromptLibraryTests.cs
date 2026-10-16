using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PromptLibraryTests
    {
        [Test]
        public void Load_ReturnsFallbackWhenFileMissing()
        {
            var missing = Path.Combine(Path.GetTempPath(), "rpawriter_missing_" + Guid.NewGuid().ToString("N") + ".txt");
            Assert.That(PromptLibrary.Load(Path.GetTempPath(), "System", Path.GetFileName(missing), "FALLBACK"), Is.EqualTo("FALLBACK"));
        }

        [Test]
        public void Load_ReturnsTrimmedFileContentWhenPresent()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_prompts_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "System"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "System", "agent-system.txt"), "  Custom prompt  ");
                var result = PromptLibrary.Load(dir, "System", "agent-system.txt", "fallback");
                Assert.That(result, Is.EqualTo("Custom prompt"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void PromptPaths_ResolveReturnsNonEmptyPaths()
        {
            var prompts = PromptPaths.ResolvePromptsDirectory();
            var rules = PromptPaths.ResolveRulesDirectory();

            Assert.That(prompts, Is.Not.Empty);
            Assert.That(rules, Is.Not.Empty);
            Assert.That(rules.EndsWith(PromptPaths.RulesFolderName));
        }
    }
}
