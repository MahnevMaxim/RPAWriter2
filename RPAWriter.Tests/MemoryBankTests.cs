using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MemoryBankTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "rpawriter_mb_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Test]
        public void EnsureInitialized_CreatesAllCoreFiles()
        {
            MemoryBankService.EnsureInitialized(_root);

            var dir = MemoryBankService.DirectoryPath(_root);
            Assert.That(Directory.Exists(dir), Is.True);
            foreach (var file in MemoryBankFiles.Core)
                Assert.That(File.Exists(Path.Combine(dir, file)), Is.True, file);
        }

        [Test]
        public void ReadAll_ReturnsConcatenatedContent()
        {
            MemoryBankService.EnsureInitialized(_root);
            MemoryBankService.WriteFile(_root, "activeContext.md", "## Focus\nCurrent work.");

            var text = MemoryBankService.ReadAll(_root);

            Assert.That(text, Does.Contain("activeContext.md"));
            Assert.That(text, Does.Contain("Current work."));
        }

        [Test]
        public void WriteFile_UpdatesSpecificFile()
        {
            MemoryBankService.EnsureInitialized(_root);

            MemoryBankService.WriteFile(_root, "progress.md", "## Progress\nDone.");

            var path = Path.Combine(MemoryBankService.DirectoryPath(_root), "progress.md");
            Assert.That(File.ReadAllText(path), Is.EqualTo("## Progress" + Environment.NewLine + "Done."));
        }

        [Test]
        public void WriteFile_InvalidFileName_Throws()
        {
            MemoryBankService.EnsureInitialized(_root);

            Assert.Throws<ArgumentException>(() => MemoryBankService.WriteFile(_root, "notes.md", "x"));
            Assert.Throws<ArgumentException>(() => MemoryBankService.WriteFile(_root, "../secret.txt", "x"));
        }

        [Test]
        public void IsAllowed_AllowsOnlyCoreFiles()
        {
            Assert.That(MemoryBankService.IsAllowed("activeContext.md"), Is.True);
            Assert.That(MemoryBankService.IsAllowed("ACTIVECONTEXT.MD"), Is.True);
            Assert.That(MemoryBankService.IsAllowed("notes.md"), Is.False);
            Assert.That(MemoryBankService.IsAllowed(""), Is.False);
        }
    }
}
