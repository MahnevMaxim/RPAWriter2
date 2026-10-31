using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Parses and analyzes log lines: extracts level, source, timestamp, and errors.</summary>
    public class LogParser
    {
        private static readonly Regex LevelPattern = new Regex(
            @"\b(?<level>ERROR|ERR|WARN|WARNING|INFO|DEBUG|TRACE|FATAL)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TimestampPattern = new Regex(
            @"(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)",
            RegexOptions.Compiled);

        public LogAnalysis Analyze(IEnumerable<string> lines)
        {
            if (lines == null) return new LogAnalysis();

            var entries = new List<LogEntry>();
            foreach (var line in lines)
            {
                var entry = ParseLine(line);
                if (entry != null) entries.Add(entry);
            }

            return AnalyzeEntries(entries);
        }

        /// <summary>Analyzes already-parsed log entries (e.g. structured entries from a log provider).</summary>
        public LogAnalysis Analyze(IEnumerable<LogEntry> entries)
        {
            return AnalyzeEntries(entries ?? new List<LogEntry>());
        }

        private static LogAnalysis AnalyzeEntries(IEnumerable<LogEntry> entries)
        {
            var analysis = new LogAnalysis();
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                analysis.Entries.Add(entry);
                var level = (entry.Level ?? "").ToUpperInvariant();
                if (level == "ERROR" || level == "ERR" || level == "FATAL")
                    analysis.Errors.Add(entry);
                else if (level == "WARN" || level == "WARNING")
                    analysis.Warnings.Add(entry);
            }

            analysis.Summary = string.Format(
                CultureInfo.InvariantCulture,
                "Total entries: {0}, errors: {1}, warnings: {2}",
                analysis.Entries.Count, analysis.Errors.Count, analysis.Warnings.Count);
            return analysis;
        }

        public LogEntry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var entry = new LogEntry { RawLine = line, Message = line };

            var ts = TimestampPattern.Match(line);
            if (ts.Success)
            {
                DateTime parsed;
                if (DateTime.TryParse(ts.Groups["ts"].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
                    entry.Timestamp = parsed;
            }

            var level = LevelPattern.Match(line);
            if (level.Success)
                entry.Level = level.Groups["level"].Value.ToUpperInvariant();

            var source = Regex.Match(line, @"\[(?<src>[^\[\]]+)\]");
            if (source.Success)
                entry.Source = source.Groups["src"].Value;

            return entry;
        }
    }
}
