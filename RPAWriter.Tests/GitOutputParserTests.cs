using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class GitOutputParserTests
    {
        // ---------- SplitLines ----------

        [Test]
        public void SplitLines_NullOrEmpty_ReturnsEmpty()
        {
            Assert.That(GitOutputParser.SplitLines(null), Is.Empty);
            Assert.That(GitOutputParser.SplitLines(""), Is.Empty);
            Assert.That(GitOutputParser.SplitLines("   "), Is.Not.Empty); // whitespace-only line is kept
        }

        [Test]
        public void SplitLines_SplitsOnCrLfCrlf()
        {
            var lines = GitOutputParser.SplitLines("a\r\nb\nc\rd\r\ne").ToList();

            Assert.That(lines, Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
        }

        [Test]
        public void SplitLines_DropsEmptyLines()
        {
            var lines = GitOutputParser.SplitLines("a\n\nb\n").ToList();

            Assert.That(lines, Is.EqualTo(new[] { "a", "b" }));
        }

        // ---------- ExtractChangedPath ----------

        [Test]
        public void ExtractChangedPath_ReturnsPathAfterStatusPrefix()
        {
            Assert.That(GitOutputParser.ExtractChangedPath(" M file.cs"), Is.EqualTo("file.cs"));
            Assert.That(GitOutputParser.ExtractChangedPath("?? new.txt"), Is.EqualTo("new.txt"));
            Assert.That(GitOutputParser.ExtractChangedPath("M  a/b/c.cs"), Is.EqualTo("a/b/c.cs"));
        }

        [Test]
        public void ExtractChangedPath_TooShortOrNull_ReturnsNull()
        {
            Assert.That(GitOutputParser.ExtractChangedPath(null), Is.Null);
            Assert.That(GitOutputParser.ExtractChangedPath(""), Is.Null);
            Assert.That(GitOutputParser.ExtractChangedPath(" M "), Is.Null); // length 3
            Assert.That(GitOutputParser.ExtractChangedPath("abc"), Is.Null); // length 3
        }

        // ---------- FormatLoggedOutput ----------

        [Test]
        public void FormatLoggedOutput_NullOrEmpty_ReturnsEmpty()
        {
            Assert.That(GitOutputParser.FormatLoggedOutput(null), Is.Empty);
            Assert.That(GitOutputParser.FormatLoggedOutput(""), Is.Empty);
        }

        [Test]
        public void FormatLoggedOutput_TrimsTrailingWhitespace()
        {
            Assert.That(GitOutputParser.FormatLoggedOutput("hello   \r\n"), Is.EqualTo("hello"));
        }

        [Test]
        public void FormatLoggedOutput_ShortText_ReturnedAsIs()
        {
            Assert.That(GitOutputParser.FormatLoggedOutput("short"), Is.EqualTo("short"));
        }

        [Test]
        public void FormatLoggedOutput_LongText_IsTruncatedWithNotice()
        {
            var text = new string('a', 100);

            var result = GitOutputParser.FormatLoggedOutput(text, 10);

            Assert.That(result, Does.StartWith(new string('a', 10)));
            Assert.That(result, Does.Contain("... (output truncated, 100 chars total)"));
        }
    }
}
