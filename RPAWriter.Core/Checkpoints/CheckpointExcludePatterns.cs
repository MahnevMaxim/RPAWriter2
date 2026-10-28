using System;
using System.Collections.Generic;
using System.IO;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>
    /// File/directory patterns excluded from the shadow repository. Ported from the
    /// Roo-Code checkpoint service (src/services/checkpoints/excludes.ts) and written
    /// into the shadow repo's .git/info/exclude file during initialization.
    /// </summary>
    public static class CheckpointExcludePatterns
    {
        private static readonly string[] BuildArtifacts =
        {
            ".gradle/", ".idea/", ".parcel-cache/", ".pytest_cache/", ".next/", ".nuxt/",
            ".sass-cache/", ".terraform/", ".terragrunt-cache/", ".vs/", ".vscode/",
            "Pods/", "__pycache__/", "bin/", "build/", "bundle/", "coverage/", "deps/",
            "dist/", "env/", "node_modules/", "obj/", "out/", "pkg/", "pycache/",
            "target/dependency/", "temp/", "vendor/", "venv/",
        };

        private static readonly string[] MediaFiles =
        {
            "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.ico", "*.webp", "*.tiff",
            "*.tif", "*.raw", "*.heic", "*.avif", "*.eps", "*.psd", "*.3gp", "*.aac",
            "*.aiff", "*.asf", "*.avi", "*.divx", "*.flac", "*.m4a", "*.m4v", "*.mkv",
            "*.mov", "*.mp3", "*.mp4", "*.mpeg", "*.mpg", "*.ogg", "*.opus", "*.rm",
            "*.rmvb", "*.vob", "*.wav", "*.webm", "*.wma", "*.wmv",
        };

        private static readonly string[] CacheFiles =
        {
            "*.DS_Store", "*.bak", "*.cache", "*.crdownload", "*.dmp", "*.dump",
            "*.eslintcache", "*.lock", "*.log", "*.old", "*.part", "*.partial",
            "*.pyc", "*.pyo", "*.stackdump", "*.swo", "*.swp", "*.temp", "*.tmp",
            "*.Thumbs.db",
        };

        private static readonly string[] ConfigFiles = { "*.env*", "*.local", "*.development", "*.production" };

        private static readonly string[] LargeDataFiles =
        {
            "*.zip", "*.tar", "*.gz", "*.rar", "*.7z", "*.iso", "*.bin", "*.exe",
            "*.dll", "*.so", "*.dylib", "*.dat", "*.dmg", "*.msi",
        };

        private static readonly string[] DatabaseFiles =
        {
            "*.arrow", "*.accdb", "*.aof", "*.avro", "*.bson", "*.csv", "*.db",
            "*.dbf", "*.dmp", "*.frm", "*.ibd", "*.mdb", "*.myd", "*.myi", "*.orc",
            "*.parquet", "*.pdb", "*.rdb", "*.sql", "*.sqlite",
        };

        private static readonly string[] LogFiles =
        {
            "*.error", "*.log", "*.logs", "*.npm-debug.log*", "*.out", "*.stdout",
            "yarn-debug.log*", "yarn-error.log*",
        };

        /// <summary>Always-excluded entries plus the built-in categories.</summary>
        public static IReadOnlyList<string> Defaults()
        {
            var list = new List<string> { ".git/" };
            list.AddRange(BuildArtifacts);
            list.AddRange(MediaFiles);
            list.AddRange(CacheFiles);
            list.AddRange(ConfigFiles);
            list.AddRange(LargeDataFiles);
            list.AddRange(DatabaseFiles);
            list.AddRange(LogFiles);
            return list;
        }

        /// <summary>Defaults plus Git LFS patterns read from the workspace .gitattributes.</summary>
        public static IReadOnlyList<string> Build(string workspaceDir)
        {
            var list = new List<string>(Defaults());
            list.AddRange(ReadLfsPatterns(workspaceDir));
            return list;
        }

        private static IEnumerable<string> ReadLfsPatterns(string workspaceDir)
        {
            if (string.IsNullOrWhiteSpace(workspaceDir)) yield break;

            var attributesPath = Path.Combine(workspaceDir, ".gitattributes");
            if (!File.Exists(attributesPath)) yield break;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(attributesPath);
            }
            catch
            {
                yield break;
            }

            foreach (var line in lines)
            {
                if (line == null || line.IndexOf("filter=lfs", StringComparison.Ordinal) < 0) continue;

                var trimmed = line.Trim();
                var end = trimmed.IndexOfAny(new[] { ' ', '\t' });
                var pattern = end < 0 ? trimmed : trimmed.Substring(0, end);
                if (!string.IsNullOrWhiteSpace(pattern))
                    yield return pattern;
            }
        }
    }
}
