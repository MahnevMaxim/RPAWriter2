using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Deterministically assembles the Error List context text that is sent to the agent.
    /// The whole context is built in a single pass and returned as one string; the builder has
    /// no UI or chat side effects, so nothing reaches the chat until it has fully completed.
    /// </summary>
    public static class CompilationErrorContextBuilder
    {
        public const int DefaultMaxErrorsWithContext = 10;
        public const int DefaultContextLines = 15;

        /// <summary>Header for the context built by <see cref="BuildAsync"/>.</summary>
        public const string ErrorListHeader = "Errors, warnings, and messages from the Visual Studio Error List:";

        /// <summary>Header for the context built from Error List items selected by the user.</summary>
        public const string SelectedItemsHeader = "The user sent this from the Visual Studio Error List:";

        /// <summary>
        /// Builds the Error List context: every entry (errors, warnings and messages) plus, for
        /// each entry, a bounded window of source code read through <paramref name="readWindowAsync"/>
        /// (file, line, contextLines → snippet text, or null/throw when it cannot be read). Returns
        /// an empty string when there are no entries.
        /// </summary>
        public static Task<string> BuildAsync(
            IReadOnlyList<BuildError> errors,
            Func<string, int, int, Task<string>> readWindowAsync,
            int maxErrorsWithContext = DefaultMaxErrorsWithContext,
            int contextLines = DefaultContextLines)
        {
            return BuildCoreAsync(errors, readWindowAsync, maxErrorsWithContext, contextLines,
                header: ErrorListHeader, truncationNoun: "entries");
        }

        /// <summary>
        /// Builds the context for Error List items selected by the user. Uses a dedicated header;
        /// like <see cref="BuildAsync"/>, it includes entries of every severity.
        /// </summary>
        public static Task<string> BuildSelectedItemsAsync(
            IReadOnlyList<BuildError> errors,
            Func<string, int, int, Task<string>> readWindowAsync,
            int maxErrorsWithContext = DefaultMaxErrorsWithContext,
            int contextLines = DefaultContextLines)
        {
            return BuildCoreAsync(errors, readWindowAsync, maxErrorsWithContext, contextLines,
                header: SelectedItemsHeader, truncationNoun: "selected items");
        }

        private static async Task<string> BuildCoreAsync(
            IReadOnlyList<BuildError> errors,
            Func<string, int, int, Task<string>> readWindowAsync,
            int maxErrorsWithContext,
            int contextLines,
            string header,
            string truncationNoun)
        {
            var items = (errors ?? new List<BuildError>())
                .Where(e => e != null)
                .ToList();
            if (items.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine();

            var shownSnippets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in items.Take(maxErrorsWithContext))
            {
                var file = e.File ?? string.Empty;
                var line = e.Line;
                var severity = string.IsNullOrWhiteSpace(e.Severity) ? "Error" : e.Severity;

                sb.Append("[").Append(severity).Append("] ");
                if (!string.IsNullOrWhiteSpace(e.Code)) sb.Append(e.Code).Append(": ");
                sb.Append(e.Message ?? string.Empty);
                sb.AppendLine();

                if (!string.IsNullOrEmpty(file)) sb.AppendLine("    File: " + file);
                if (line > 0) sb.AppendLine("    Line: " + line);
                if (e.Column > 0) sb.AppendLine("    Column: " + e.Column);
                if (!string.IsNullOrWhiteSpace(e.Project)) sb.AppendLine("    Project: " + e.Project);
                sb.AppendLine("    Severity: " + FormatSeverity(e));
                if (!string.IsNullOrWhiteSpace(e.Category)) sb.AppendLine("    Category: " + e.Category);

                if (!string.IsNullOrEmpty(file) && line > 0 && readWindowAsync != null)
                {
                    var key = file + "\u0000" + line;
                    if (shownSnippets.Add(key))
                    {
                        string snippet = null;
                        string readError = null;
                        try
                        {
                            snippet = await readWindowAsync(file, line, contextLines);
                        }
                        catch (Exception ex)
                        {
                            readError = ex.Message;
                        }

                        if (!string.IsNullOrEmpty(snippet))
                        {
                            sb.AppendLine(file + " (around line " + line + "):");
                            sb.AppendLine(snippet);
                        }
                        else
                        {
                            sb.AppendLine(file + " (around line " + line + "): (could not read: " + (readError ?? "no lines available") + ")");
                        }
                    }
                }

                sb.AppendLine();
            }

            if (items.Count > maxErrorsWithContext)
                sb.AppendLine("(showing " + maxErrorsWithContext + " of " + items.Count + " " + truncationNoun + ")");

            return sb.ToString().TrimEnd();
        }

        private static string FormatSeverity(BuildError e)
        {
            var severity = string.IsNullOrWhiteSpace(e.Severity) ? "Error" : e.Severity;
            if (!string.IsNullOrWhiteSpace(e.ErrorLevelName) && e.ErrorLevel != 0)
                return severity + " (" + e.ErrorLevelName + " = " + e.ErrorLevel + ")";
            if (e.ErrorLevel != 0)
                return severity + " (level " + e.ErrorLevel + ")";
            return severity;
        }
    }
}