using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class LogAnalyzerTests
    {
        [Test]
        public void Analyze_DetectsErrorsAndWarnings()
        {
            var analyzer = new LogAnalyzer();
            var lines = new[]
            {
                "2026-10-13 10:00:00 [Robot] INFO Robot start",
                "2026-10-13 10:00:01 [Robot] ERROR Could not open page",
                "2026-10-13 10:00:02 [Robot] WARN Field not found"
            };

            var result = analyzer.Analyze(lines);

            Assert.That(result.Entries.Count, Is.EqualTo(3));
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Summary, Does.Contain("errors: 1"));
        }

        [Test]
        public void ParseLine_ExtractsTimestampLevelAndSource()
        {
            var entry = new LogAnalyzer().ParseLine("2026-10-13 10:00:01 [Robot] ERROR Message");

            Assert.That(entry.Level, Is.EqualTo("ERROR"));
            Assert.That(entry.Source, Is.EqualTo("Robot"));
            Assert.That(entry.Timestamp, Is.Not.Null);
        }

        [Test]
        public void ParseLine_EmptyLine_ReturnsNull()
        {
            Assert.That(new LogAnalyzer().ParseLine("   "), Is.Null);
        }
    }
}
