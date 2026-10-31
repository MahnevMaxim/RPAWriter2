using System;
using System.Collections.Generic;
using System.Linq;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Rules of the robot-name history kept in the settings and offered in the "Robot" box:
    /// most recent first, no duplicates regardless of case, bounded length, placeholder ignored.
    /// Extracted from the view so the rules are testable (see the log analysis model).
    /// </summary>
    public static class RobotNameHistory
    {
        /// <summary>Placeholder shown when no robot name was used yet; it is never remembered.</summary>
        public const string DefaultRobotName = "RPA-";

        /// <summary>Maximum number of remembered names.</summary>
        public const int MaxCount = 20;

        /// <summary>
        /// Remembers <paramref name="name"/>: trims it, ignores blanks and <see cref="DefaultRobotName"/>,
        /// removes an existing entry with the same name (case-insensitive), inserts it first and
        /// truncates the list to <paramref name="max"/>.
        /// </summary>
        /// <returns><c>true</c> when the list changed and therefore has to be persisted.</returns>
        public static bool Add(IList<string> history, string name, int max = MaxCount)
        {
            if (history == null)
                return false;

            var trimmed = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;
            if (string.Equals(trimmed, DefaultRobotName, StringComparison.OrdinalIgnoreCase))
                return false;

            var before = history.ToList();
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(history[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    history.RemoveAt(i);
            }

            history.Insert(0, trimmed);

            if (max > 0 && history.Count > max)
            {
                for (int i = history.Count - 1; i >= max; i--)
                    history.RemoveAt(i);
            }

            return !before.SequenceEqual(history, StringComparer.Ordinal);
        }

        /// <summary>
        /// Sanitises a history loaded from the settings: trims, drops blanks, removes duplicates
        /// keeping the first occurrence and enforces the length limit.
        /// </summary>
        public static List<string> Normalize(IEnumerable<string> names, int max = MaxCount)
        {
            var result = new List<string>();
            if (names == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in names)
            {
                var trimmed = (candidate ?? "").Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                if (!seen.Add(trimmed))
                    continue;

                result.Add(trimmed);
                if (max > 0 && result.Count >= max)
                    break;
            }

            return result;
        }

        /// <summary>
        /// Name shown in the "Robot" box: the most recent remembered name, or the placeholder.
        /// </summary>
        public static string ResolveCurrent(IList<string> history)
        {
            if (history == null || history.Count == 0)
                return DefaultRobotName;
            return string.IsNullOrWhiteSpace(history[0]) ? DefaultRobotName : history[0];
        }

        /// <summary>Entries offered as suggestions: every remembered name except the current one.</summary>
        public static List<string> ResolveSuggestions(IList<string> history)
        {
            if (history == null || history.Count <= 1)
                return new List<string>();
            return history.Skip(1).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
    }
}
