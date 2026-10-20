using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Resolves and reads the shipped user documentation (markdown files) that the
    /// Help chat mode loads into the context pipeline.
    /// </summary>
    public static class HelpDocumentation
    {
        /// <summary>Folder name that holds the user documentation next to the assemblies.</summary>
        public const string DocsFolderName = "Docs";

        /// <summary>
        /// Returns the deployed documentation directory. Candidates, in order:
        /// 1) next to this (Core) assembly, 2) next to the entry assembly,
        /// 3) %AppData%\RPAWriter2\Docs. Returns the first existing folder;
        /// otherwise the Core-adjacent path (best-effort default).
        /// </summary>
        public static string ResolveDocsDirectory()
        {
            var coreDir = Path.GetDirectoryName(typeof(HelpDocumentation).Assembly.Location);
            var candidates = new[]
            {
                CombineDir(coreDir),
                CombineDir(EntryAssemblyDir()),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RPAWriter2",
                    DocsFolderName)
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            return string.IsNullOrWhiteSpace(candidates[0]) ? candidates[2] : candidates[0];
        }

        /// <summary>
        /// Returns the concatenated content of every markdown documentation file in the
        /// docs directory, each prefixed with its file name. Returns "" when the directory
        /// is absent or empty.
        /// </summary>
        public static string ReadAll()
        {
            var dir = ResolveDocsDirectory();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return "";

            var files = new List<string>(Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var file in files)
            {
                try
                {
                    var relative = file;
                    if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        relative = file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    sb.Append("# ").Append(relative).AppendLine();
                    sb.AppendLine(File.ReadAllText(file, Encoding.UTF8).Trim());
                    sb.AppendLine();
                }
                catch
                {
                    // ignore unreadable files
                }
            }

            return sb.ToString().Trim();
        }

        private static string CombineDir(string baseDir)
        {
            return string.IsNullOrWhiteSpace(baseDir) ? "" : Path.Combine(baseDir, DocsFolderName);
        }

        private static string EntryAssemblyDir()
        {
            try
            {
                var entry = Assembly.GetEntryAssembly();
                if (entry == null) return "";
                return Path.GetDirectoryName(entry.Location);
            }
            catch
            {
                return "";
            }
        }
    }
}
