using System;
using System.IO;
using System.Reflection;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Resolves the deployed "Prompts" directory on disk. The directory is shipped
    /// next to the extension assemblies (copied to output and included in the VSIX),
    /// so it is located relative to the loaded assembly rather than hard-coded.
    /// </summary>
    public static class PromptPaths
    {
        /// <summary>Folder name that holds all prompt categories.</summary>
        public const string PromptsFolderName = "Prompts";

        /// <summary>Category folder that holds platform rule subfolders (mwf/pix/wr/uipath).</summary>
        public const string RulesFolderName = "Rules";

        /// <summary>
        /// Returns the deployed prompts directory. Candidates, in order:
        /// 1) next to this (Core) assembly, 2) next to the entry assembly,
        /// 3) %AppData%\RPAWriter2\Prompts. Returns the first existing folder;
        /// otherwise the Core-adjacent path (best-effort default).
        /// </summary>
        public static string ResolvePromptsDirectory()
        {
            var coreDir = Path.GetDirectoryName(typeof(PromptPaths).Assembly.Location);
            var candidates = new[]
            {
                CombineDir(coreDir),
                CombineDir(EntryAssemblyDir()),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RPAWriter2",
                    PromptsFolderName)
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            return string.IsNullOrWhiteSpace(candidates[0]) ? candidates[2] : candidates[0];
        }

        /// <summary>Returns the deployed rules directory (Prompts/Rules).</summary>
        public static string ResolveRulesDirectory()
        {
            return Path.Combine(ResolvePromptsDirectory(), RulesFolderName);
        }

        private static string CombineDir(string baseDir)
        {
            return string.IsNullOrWhiteSpace(baseDir) ? "" : Path.Combine(baseDir, PromptsFolderName);
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
