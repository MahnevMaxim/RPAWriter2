using System;
using System.IO;
using System.Reflection;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Resolves the deployed "ActivityLibrary" directory on disk. The directory ships
    /// next to the extension assemblies (copied to output and included in the VSIX),
    /// so it is located relative to the loaded assembly rather than hard-coded.
    /// </summary>
    public static class ActivityLibraryPaths
    {
        public const string ActivityLibraryFolderName = "ActivityLibrary";
        public const string WfSnippetsFolderName = "wf";
        public const string PixActivitiesFolderName = "pix";
        public const string MwfActivitiesFolderName = "mwf";

        /// <summary>
        /// Returns the deployed activity library directory. Candidates, in order:
        /// 1) next to this (Core) assembly, 2) next to the entry assembly,
        /// 3) %AppData%\RPAWriter2\&lt;version&gt;\ActivityLibrary.
        /// </summary>
        public static string ResolveActivityLibraryDirectory()
        {
            var coreDir = Path.GetDirectoryName(typeof(ActivityLibraryPaths).Assembly.Location);
            var candidates = new[]
            {
                CombineDir(coreDir),
                CombineDir(EntryAssemblyDir()),
                AppDataPaths.Resolve(ActivityLibraryFolderName)
            };

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            return string.IsNullOrWhiteSpace(candidates[0]) ? candidates[2] : candidates[0];
        }

        public static string ResolveWfSnippetsDirectory()
            => Path.Combine(ResolveActivityLibraryDirectory(), WfSnippetsFolderName);

        public static string ResolvePixActivitiesDirectory()
            => Path.Combine(ResolveActivityLibraryDirectory(), PixActivitiesFolderName);

        public static string ResolveMwfActivitiesDirectory()
            => Path.Combine(ResolveActivityLibraryDirectory(), MwfActivitiesFolderName);

        private static string CombineDir(string baseDir)
        {
            return string.IsNullOrWhiteSpace(baseDir) ? "" : Path.Combine(baseDir, ActivityLibraryFolderName);
        }

        private static string EntryAssemblyDir()
        {
            try
            {
                var entry = Assembly.GetEntryAssembly();
                return entry == null ? "" : Path.GetDirectoryName(entry.Location);
            }
            catch
            {
                return "";
            }
        }
    }
}
