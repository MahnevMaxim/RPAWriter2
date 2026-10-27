using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Pure helpers for parsing raw <c>git</c> output. Extracted from the VS-facing
    /// <c>GitService</c> so the parsing can be unit-tested without a git executable.
    /// </summary>
    public static class GitOutputParser
    {
        /// <summary>Default cap for log lines emitted by <c>GitService</c>.</summary>
        public const int DefaultMaxLoggedOutputChars = 20000;

        /// <summary>
        /// Splits text on CR/LF/CRLF, dropping empty lines. Returns an empty sequence
        /// for null or empty input instead of throwing.
        /// </summary>
        public static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Extracts the path from a single <c>git status --porcelain</c> line
        /// (two status characters followed by a space and the path). Returns null when
        /// the line is too short to contain a path.
        /// </summary>
        public static string ExtractChangedPath(string porcelainLine)
        {
            if (string.IsNullOrEmpty(porcelainLine) || porcelainLine.Length <= 3)
                return null;

            return porcelainLine.Substring(3);
        }

        /// <summary>
        /// Trims trailing whitespace and truncates long output with a trailing notice.
        /// Returns an empty string for null/empty input.
        /// </summary>
        public static string FormatLoggedOutput(string text, int maxChars = DefaultMaxLoggedOutputChars)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var trimmed = text.TrimEnd();
            if (trimmed.Length <= maxChars)
                return trimmed;

            return trimmed.Substring(0, maxChars) + Environment.NewLine
                + "... (output truncated, " + trimmed.Length + " chars total)";
        }
    }
}
