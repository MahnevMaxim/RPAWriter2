using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads markdown files containing spec-processing and task-formation rules from a directory.
    /// Supports reading from a platform subdirectory (e.g. RulesPath/mwf) in addition to the top-level directory.
    /// </summary>
    public static class ProcessingRulesReader
    {
        /// <summary>
        /// Returns the concatenated text of all *.md / *.markdown files in a directory
        /// (no subdirectories), sorted by name. Empty directory/path — empty string.
        /// </summary>
        public static string ReadAll(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "";
            if (!Directory.Exists(folder)) return "";
            return ReadFromFolder(folder);
        }

        /// <summary>
        /// Returns the concatenated text of all *.md / *.markdown files in the platform
        /// subdirectory <c>folder/platformFolder</c>, sorted by name. Empty when the folder
        /// or the platform subdirectory does not exist.
        /// </summary>
        public static string ReadAll(string folder, string platformFolder)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(platformFolder)) return "";
            var platformDir = Path.Combine(folder, platformFolder);
            if (!Directory.Exists(platformDir)) return "";
            return ReadFromFolder(platformDir);
        }

        private static string ReadFromFolder(string dir)
        {
            var files = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(dir, "*.markdown", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) return "";

            var sb = new StringBuilder();
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file, Encoding.UTF8);
                }
                catch
                {
                    continue; // skip inaccessible files
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                sb.AppendLine("## Rules: " + Path.GetFileName(file));
                sb.AppendLine(text);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
