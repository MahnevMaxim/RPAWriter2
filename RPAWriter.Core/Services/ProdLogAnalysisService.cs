using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Turns production log entries into a chat prompt: maps provider entries to
    /// <see cref="LogEntry"/>, runs <see cref="LogParser"/> over them and formats the prompt.
    /// Stateless and file-system free except for the default prompt header, which comes from the
    /// prompt library; pass <c>header</c> explicitly to keep a caller deterministic (tests).
    /// </summary>
    public class ProdLogAnalysisService
    {
        /// <summary>Hard limit of the produced prompt; longer text is cut with <see cref="TruncatedMarker"/>.</summary>
        public const int MaxPromptChars = 40000;

        /// <summary>Maximum number of error/warning lines listed separately.</summary>
        public const int MaxSectionLines = 50;

        /// <summary>Maximum number of raw log lines pasted into the prompt.</summary>
        public const int MaxLogLines = 200;

        /// <summary>Suffix appended when the prompt is longer than <see cref="MaxPromptChars"/>.</summary>
        public const string TruncatedMarker = "\n... (truncated)";

        private readonly LogParser _parser;
        private readonly string _prodLogsHeader;
        private readonly string _outputWindowHeader;

        /// <summary>
        /// Creates the service. <paramref name="parser"/> defaults to a new <see cref="LogParser"/>;
        /// the headers default to the prompt library (pass them explicitly to stay file-system free,
        /// which is what the tests and the model do).
        /// </summary>
        public ProdLogAnalysisService(LogParser parser = null, string prodLogsHeader = null, string outputWindowHeader = null)
        {
            _parser = parser ?? new LogParser();
            _prodLogsHeader = prodLogsHeader ?? DefaultProdLogsHeader;
            _outputWindowHeader = outputWindowHeader ?? DefaultOutputWindowHeader;
        }

        /// <summary>Header used by <see cref="BuildPrompt(LogAnalysis, IReadOnlyList{IProdLog})"/>.</summary>
        public string ProdLogsHeader => _prodLogsHeader;

        /// <summary>Header used by <see cref="BuildOutputWindowPrompt(string)"/>.</summary>
        public string OutputWindowHeader => _outputWindowHeader;

        /// <summary>Header of the production-log prompt (prompt library file, or the built-in default).</summary>
        public static string DefaultProdLogsHeader =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks,
                "analyze-prod-logs.txt", PromptDefaults.AnalyzeProdLogs);

        /// <summary>Header of the Output-window prompt (prompt library file, or the built-in default).</summary>
        public static string DefaultOutputWindowHeader =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks,
                "analyze-output-window.txt", PromptDefaults.AnalyzeOutputWindow);

        #region Mapping

        /// <summary>
        /// Maps one provider entry to a <see cref="LogEntry"/>; <c>RawLine</c> falls back to the
        /// message when the provider keeps no raw line. Returns <c>null</c> for a <c>null</c> input.
        /// </summary>
        public LogEntry Map(IProdLog log)
        {
            if (log == null)
                return null;

            return new LogEntry
            {
                Timestamp = log.Timestamp,
                Level = log.Level.ToString(),
                Source = log.Source,
                Message = log.Message,
                RawLine = string.IsNullOrWhiteSpace(log.RawLine) ? log.Message : log.RawLine,
                Parameters = log.Parameters?.ToList() ?? new List<LogParameter>()
            };
        }

        /// <summary>Maps a sequence of provider entries, preserving order.</summary>
        public List<LogEntry> MapAll(IEnumerable<IProdLog> logs)
        {
            var entries = new List<LogEntry>();
            if (logs == null)
                return entries;

            foreach (var log in logs)
            {
                var entry = Map(log);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }

        /// <summary>Maps the entries and analyzes them (errors, warnings, summary).</summary>
        public LogAnalysis Analyze(IEnumerable<IProdLog> logs)
        {
            return _parser.Analyze(MapAll(logs));
        }

        #endregion Mapping

        #region Prompt building

        /// <summary>Builds the production-log prompt with the header from the prompt library.</summary>
        public string BuildPrompt(LogAnalysis analysis, IReadOnlyList<IProdLog> logs)
        {
            return BuildPrompt(analysis, logs, _prodLogsHeader);
        }

        /// <summary>
        /// Builds the production-log prompt: header, analysis summary, the first
        /// <see cref="MaxSectionLines"/> errors and warnings, then the first <see cref="MaxLogLines"/>
        /// log lines with their parameters. The result is capped at <see cref="MaxPromptChars"/>.
        /// </summary>
        public string BuildPrompt(LogAnalysis analysis, IReadOnlyList<IProdLog> logs, string header)
        {
            var effectiveLogs = logs ?? new List<IProdLog>();
            var effectiveAnalysis = analysis ?? new LogAnalysis();

            var sb = new StringBuilder();
            sb.AppendLine((header ?? "").TrimEnd());
            sb.AppendLine();
            sb.AppendLine(effectiveAnalysis.Summary ?? "");
            sb.AppendLine();

            AppendSection(sb, "Errors:", effectiveAnalysis.Errors);
            AppendSection(sb, "Warnings:", effectiveAnalysis.Warnings);

            sb.AppendLine("Full log:");
            foreach (var log in effectiveLogs.Take(MaxLogLines))
            {
                sb.AppendLine("  [" + FormatTimestamp(log.Timestamp) + "] [" + log.Level + "] " +
                              (log.Source ?? "") + ": " + (log.Message ?? "") + FormatParameters(log.Parameters));
            }

            return Truncate(sb.ToString());
        }

        /// <summary>Builds the Output-window prompt with the header from the prompt library.</summary>
        public string BuildOutputWindowPrompt(string text)
        {
            return BuildOutputWindowPrompt(text, _outputWindowHeader);
        }

        /// <summary>Builds the Output-window prompt: header, blank line, then the window text.</summary>
        public string BuildOutputWindowPrompt(string text, string header)
        {
            return (header ?? "") + "\n\n" + (text ?? "");
        }

        private static void AppendSection(StringBuilder sb, string title, IEnumerable<LogEntry> entries)
        {
            var list = entries?.Where(e => e != null).Take(MaxSectionLines).ToList();
            if (list == null || list.Count == 0)
                return;

            sb.AppendLine(title);
            foreach (var entry in list)
                sb.AppendLine("  [" + FormatTimestamp(entry.Timestamp) + "] " + (entry.Source ?? "") + ": " + (entry.Message ?? ""));
            sb.AppendLine();
        }

        private static string Truncate(string text)
        {
            if (text == null)
                return "";
            if (text.Length <= MaxPromptChars)
                return text;
            return text.Substring(0, MaxPromptChars) + TruncatedMarker;
        }

        /// <summary>Timestamp format used inside the prompt ("?" when unknown).</summary>
        public static string FormatTimestamp(DateTime? timestamp)
        {
            return timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "?";
        }

        /// <summary>Renders log parameters as <c>  parameters: key=value, key=value</c>; empty when none.</summary>
        public static string FormatParameters(IReadOnlyList<LogParameter> parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return "";

            var pairs = new List<string>();
            foreach (var parameter in parameters)
                pairs.Add((parameter?.Key ?? "") + "=" + (parameter?.Value ?? ""));
            return "  parameters: " + string.Join(", ", pairs);
        }

        #endregion Prompt building
    }
}
