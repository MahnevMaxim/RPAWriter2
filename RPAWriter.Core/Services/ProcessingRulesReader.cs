using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads markdown files containing spec-processing and task-formation rules from a directory.
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

            var files = Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(folder, "*.markdown", SearchOption.TopDirectoryOnly))
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
