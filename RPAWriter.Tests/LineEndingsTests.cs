using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class LineEndingsTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "rpawriter_le_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Test]
        public void Normalize_ToCrLf_UnifiesMixedEndings()
        {
            var input = "a\r\nb\nc\rd";
            Assert.That(LineEndings.Normalize(input, "\r\n"), Is.EqualTo("a\r\nb\r\nc\r\nd"));
        }

        [Test]
        public void Normalize_ToLf_UnifiesMixedEndings()
        {
            var input = "a\r\nb\nc\rd";
            Assert.That(LineEndings.Normalize(input, "\n"), Is.EqualTo("a\nb\nc\nd"));
        }

        [Test]
        public void Normalize_NullOrEmpty_IsSafe()
        {
            Assert.That(LineEndings.Normalize(null, "\r\n"), Is.EqualTo(string.Empty));
            Assert.That(LineEndings.Normalize("", "\r\n"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Detect_PrefersDominantEnding()
        {
            Assert.That(LineEndings.Detect("a\r\nb\r\nc\r\n"), Is.EqualTo("\r\n"));
            Assert.That(LineEndings.Detect("a\nb\nc"), Is.EqualTo("\n"));
        }

        [Test]
        public void Detect_Empty_FallsBackToEnvironmentNewLine()
        {
            Assert.That(LineEndings.Detect(""), Is.EqualTo(Environment.NewLine));
            Assert.That(LineEndings.Detect(null), Is.EqualTo(Environment.NewLine));
        }

        [Test]
        public void WriteAllText_NewFile_UsesEnvironmentNewLine()
        {
            var path = Path.Combine(_dir, "new.txt");
            LineEndings.WriteAllText(path, "a\nb\nc");

            var text = File.ReadAllText(path);
            Assert.That(text, Is.EqualTo("a" + Environment.NewLine + "b" + Environment.NewLine + "c"));
        }

        [Test]
        public void WriteAllText_ExistingFile_PreservesItsLineEnding()
        {
            var path = Path.Combine(_dir, "existing.txt");
            File.WriteAllText(path, "x\ny\n"); // LF file

            LineEndings.WriteAllText(path, "a\r\nb\r\nc");

            Assert.That(File.ReadAllText(path), Is.EqualTo("a\nb\nc"));
        }

        [Test]
        public void WriteAllText_ExistingCrLfFile_StaysCrLf()
        {
            var path = Path.Combine(_dir, "crlf.txt");
            File.WriteAllText(path, "x\r\ny\r\n"); // CRLF file

            LineEndings.WriteAllText(path, "a\nb\nc");

            Assert.That(File.ReadAllText(path), Is.EqualTo("a\r\nb\r\nc"));
        }
    }
}
