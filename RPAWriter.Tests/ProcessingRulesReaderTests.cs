using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ProcessingRulesReaderTests
    {
        [Test]
        public void ReadAll_CombinesMarkdownFilesAndIgnoresOthers()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.md"), "Rule A");
                File.WriteAllText(Path.Combine(dir, "b.markdown"), "Rule B");
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "Not a rule");

                var text = ProcessingRulesReader.ReadAll(dir);

                Assert.That(text, Does.Contain("Rule A"));
                Assert.That(text, Does.Contain("Rule B"));
                Assert.That(text, Does.Not.Contain("Not a rule"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void ReadAll_ReturnsEmptyForMissingFolder()
        {
            var missing = Path.Combine(Path.GetTempPath(), "rpawriter_missing_" + Guid.NewGuid().ToString("N"));
            Assert.That(ProcessingRulesReader.ReadAll(missing), Is.Empty);
        }
    }
}
