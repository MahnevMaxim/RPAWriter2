using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
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

        [Test]
        public void AnalyzeEntries_ClassifiesErrorsAndWarnings()
        {
            var entries = new[]
            {
                new LogEntry { Level = "INFO", Message = "start" },
                new LogEntry { Level = "ERROR", Message = "failed" },
                new LogEntry { Level = "FATAL", Message = "crashed" },
                new LogEntry { Level = "WARNING", Message = "careful" },
                new LogEntry { Level = "DEBUG", Message = "trace" }
            };

            var result = new LogAnalyzer().Analyze(entries);

            Assert.That(result.Entries.Count, Is.EqualTo(5));
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Summary, Does.Contain("errors: 2"));
            Assert.That(result.Summary, Does.Contain("warnings: 1"));
        }

        [Test]
        public void AnalyzeEntries_Null_ReturnsEmpty()
        {
            var result = new LogAnalyzer().Analyze((IEnumerable<LogEntry>)null);

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void AnalyzeEntries_Empty_ReturnsZeroSummary()
        {
            var result = new LogAnalyzer().Analyze(new LogEntry[0]);

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Summary, Does.Contain("Total entries: 0"));
        }
    }
}
