using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Tests of the analysis scenario behind "Analyze logs - production servers":
    /// provider entry mapping, error/warning extraction and prompt formatting
    /// (see documentation/13-model-layer.md, section 18).
    /// </summary>
    [TestFixture]
    public class ProdLogAnalysisServiceTests
    {
        private const string Header = "PROD-LOGS-HEADER";

        private static ProdLogAnalysisService CreateService(string header = Header)
        {
            return new ProdLogAnalysisService(null, header, "OUTPUT-HEADER");
        }

        [Test]
        public void Map_CopiesAllFields()
        {
            var timestamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            var log = new FakeProdLog
            {
                Timestamp = timestamp,
                Level = LogLevel.Warning,
                Source = "SAP",
                Message = "Field missing",
                RawLine = "raw line",
                Parameters = new List<LogParameter> { new LogParameter { Key = "Table", Value = "VBAK" } }
            };

            var entry = CreateService().Map(log);

            Assert.That(entry.Timestamp, Is.EqualTo(timestamp));
            Assert.That(entry.Level, Is.EqualTo("Warning"));
            Assert.That(entry.Source, Is.EqualTo("SAP"));
            Assert.That(entry.Message, Is.EqualTo("Field missing"));
            Assert.That(entry.RawLine, Is.EqualTo("raw line"));
            Assert.That(entry.Parameters, Has.Count.EqualTo(1));
            Assert.That(entry.Parameters[0].Key, Is.EqualTo("Table"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Map_FallsBackToMessageWhenRawLineIsMissing(string rawLine)
        {
            var log = new FakeProdLog { Message = "only message", RawLine = rawLine, Level = LogLevel.Info };

            var entry = CreateService().Map(log);

            Assert.That(entry.RawLine, Is.EqualTo("only message"));
        }

        [Test]
        public void Map_ReturnsNullForNull()
        {
            Assert.That(CreateService().Map(null), Is.Null);
        }

        [Test]
        public void MapAll_PreservesOrderAndSkipsNulls()
        {
            var logs = new List<IProdLog>
            {
                FakeProdLog.Create(LogLevel.Info, "first"),
                null,
                FakeProdLog.Create(LogLevel.Error, "second")
            };

            var entries = CreateService().MapAll(logs);

            Assert.That(entries.Select(e => e.Message), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void MapAll_HandlesNullSequence()
        {
            Assert.That(CreateService().MapAll(null), Is.Empty);
        }

        [Test]
        public void Analyze_CollectsErrorsAndWarnings()
        {
            var logs = new List<IProdLog>
            {
                FakeProdLog.Create(LogLevel.Info, "info"),
                FakeProdLog.Create(LogLevel.Warning, "warn"),
                FakeProdLog.Create(LogLevel.Error, "error"),
                FakeProdLog.Create(LogLevel.Fatal, "fatal")
            };

            var analysis = CreateService().Analyze(logs);

            Assert.That(analysis.Entries, Has.Count.EqualTo(4));
            Assert.That(analysis.Errors.Select(e => e.Message), Is.EqualTo(new[] { "error", "fatal" }));
            Assert.That(analysis.Warnings.Select(e => e.Message), Is.EqualTo(new[] { "warn" }));
            Assert.That(analysis.Summary, Does.Contain("Total entries: 4"));
            Assert.That(analysis.Summary, Does.Contain("errors: 2"));
            Assert.That(analysis.Summary, Does.Contain("warnings: 1"));
        }

        [Test]
        public void Analyze_NullSequenceReturnsEmptySummary()
        {
            var analysis = CreateService().Analyze(null);

            Assert.That(analysis.Entries, Is.Empty);
            Assert.That(analysis.Summary, Does.Contain("Total entries: 0"));
        }

        [Test]
        public void BuildPrompt_StartsWithHeaderAndSummaryAndListsErrors()
        {
            var logs = new List<IProdLog>
            {
                FakeProdLog.Create(LogLevel.Error, "connection reset", "SAP", new DateTime(2024, 5, 6, 7, 8, 9)),
                FakeProdLog.Create(LogLevel.Info, "queue item received")
            };
            var service = CreateService();
            var analysis = service.Analyze(logs);

            var prompt = service.BuildPrompt(analysis, logs);

            Assert.That(prompt, Does.StartWith(Header));
            Assert.That(prompt, Does.Contain("Total entries: 2"));
            Assert.That(prompt, Does.Contain("Errors:"));
            Assert.That(prompt, Does.Contain("[2024-05-06 07:08:09] SAP: connection reset"));
            Assert.That(prompt, Does.Contain("Full log:"));
            Assert.That(prompt, Does.Contain("[Info] Robot: queue item received"));
        }

        [Test]
        public void BuildPrompt_ListsParametersOfALogLine()
        {
            var log = new FakeProdLog
            {
                Level = LogLevel.Info,
                Source = "SAP",
                Message = "Transaction started",
                Parameters = new List<LogParameter> { new LogParameter { Key = "Transaction", Value = "VA01" } }
            };
            var logs = new List<IProdLog> { log };
            var service = CreateService();

            var prompt = service.BuildPrompt(service.Analyze(logs), logs);

            Assert.That(prompt, Does.Contain("parameters: Transaction=VA01"));
        }

        [Test]
        public void BuildPrompt_OmitsErrorSectionWhenThereAreNoErrors()
        {
            var logs = new List<IProdLog> { FakeProdLog.Create(LogLevel.Info, "all good") };
            var service = CreateService();

            var prompt = service.BuildPrompt(service.Analyze(logs), logs);

            Assert.That(prompt, Does.Not.Contain("Errors:"));
            Assert.That(prompt, Does.Not.Contain("Warnings:"));
        }

        [Test]
        public void BuildPrompt_LimitsErrorSectionTo50Lines()
        {
            var logs = Enumerable.Range(1, 60)
                .Select(i => (IProdLog)FakeProdLog.Create(LogLevel.Error, "err-" + i))
                .ToList();
            var service = CreateService();

            var prompt = service.BuildPrompt(service.Analyze(logs), logs);

            var start = prompt.IndexOf("Errors:", StringComparison.Ordinal);
            var errorsSection = prompt.Substring(start, prompt.IndexOf("Full log:", StringComparison.Ordinal) - start);
            var listed = errorsSection.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));

            Assert.That(listed, Is.EqualTo(ProdLogAnalysisService.MaxSectionLines));
        }

        [Test]
        public void BuildPrompt_LimitsFullLogTo200Lines()
        {
            var logs = Enumerable.Range(1, 250)
                .Select(i => (IProdLog)FakeProdLog.Create(LogLevel.Info, "line-" + i))
                .ToList();
            var service = CreateService();

            var prompt = service.BuildPrompt(service.Analyze(logs), logs);

            var fullLog = prompt.Substring(prompt.IndexOf("Full log:", StringComparison.Ordinal));
            var listed = fullLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));

            Assert.That(listed, Is.EqualTo(ProdLogAnalysisService.MaxLogLines));
        }

        [Test]
        public void BuildPrompt_TruncatesLongPrompt()
        {
            var logs = Enumerable.Range(1, ProdLogAnalysisService.MaxLogLines)
                .Select(i => (IProdLog)FakeProdLog.Create(LogLevel.Info, new string('x', 400)))
                .ToList();
            var service = CreateService();

            var prompt = service.BuildPrompt(service.Analyze(logs), logs);

            Assert.That(prompt.Length,
                Is.EqualTo(ProdLogAnalysisService.MaxPromptChars + ProdLogAnalysisService.TruncatedMarker.Length));
            Assert.That(prompt, Does.EndWith(ProdLogAnalysisService.TruncatedMarker));
        }

        [Test]
        public void BuildPrompt_HandlesNullArguments()
        {
            var prompt = CreateService().BuildPrompt(null, null);

            Assert.That(prompt, Does.StartWith(Header));
            Assert.That(prompt, Does.Contain("Full log:"));
        }

        [Test]
        public void BuildOutputWindowPrompt_PutsHeaderBeforeText()
        {
            var prompt = CreateService().BuildOutputWindowPrompt("build output");

            Assert.That(prompt, Is.EqualTo("OUTPUT-HEADER\n\nbuild output"));
        }

        [Test]
        public void BuildOutputWindowPrompt_HandlesNullText()
        {
            Assert.That(CreateService().BuildOutputWindowPrompt(null), Is.EqualTo("OUTPUT-HEADER\n\n"));
        }

        [Test]
        public void FormatTimestamp_FormatsKnownValueAndQuestionMark()
        {
            Assert.That(ProdLogAnalysisService.FormatTimestamp(new DateTime(2024, 1, 2, 3, 4, 5)),
                Is.EqualTo("2024-01-02 03:04:05"));
            Assert.That(ProdLogAnalysisService.FormatTimestamp(null), Is.EqualTo("?"));
        }

        [Test]
        public void FormatParameters_JoinsPairsAndHandlesEmpty()
        {
            var parameters = new List<LogParameter>
            {
                new LogParameter { Key = "A", Value = "1" },
                new LogParameter { Key = "B", Value = "2" }
            };

            Assert.That(ProdLogAnalysisService.FormatParameters(parameters), Is.EqualTo("  parameters: A=1, B=2"));
            Assert.That(ProdLogAnalysisService.FormatParameters(new List<LogParameter>()), Is.Empty);
            Assert.That(ProdLogAnalysisService.FormatParameters(null), Is.Empty);
        }

        [Test]
        public void Constructor_UsesInjectedHeaders()
        {
            var service = new ProdLogAnalysisService(null, "HDR", "OUT");

            Assert.That(service.ProdLogsHeader, Is.EqualTo("HDR"));
            Assert.That(service.OutputWindowHeader, Is.EqualTo("OUT"));
        }
    }
}
