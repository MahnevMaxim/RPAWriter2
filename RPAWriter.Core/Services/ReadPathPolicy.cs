using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Strict, mode-independent path policy for the agent's read tools. A path is allowed only when
    /// it lies inside one of the allowed roots AND does not hit a denied service directory or match a
    /// sensitive/secret file name. Callers decide whether to apply the policy (e.g. only in Plan mode).
    /// </summary>
    public static class ReadPathPolicy
    {
        private static readonly HashSet<string> DeniedDirectoryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", ".git", ".vs", "node_modules", "packages",
                "TestResults", ".idea", ".vscode", "__pycache__"
            };

        private static readonly HashSet<string> DeniedExactFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".env", "web.config", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519"
            };

        private static readonly string[] DeniedFilePrefixes =
        {
            "secrets", ".env.", "id_rsa.", "id_dsa.", "id_ecdsa.", "id_ed25519."
        };

        private static readonly string[] DeniedFileSuffixes =
        {
            ".pem", ".key", ".pfx", ".p12", ".snk", ".rdp", ".user", ".secret"
        };

        /// <summary>
        /// Returns true when the path is inside one of <paramref name="allowedRoots"/> and is not a
        /// denied directory or a sensitive/secret file. Returns false for empty/invalid paths and when
        /// no roots are configured (secure by default).
        /// </summary>
        public static bool IsAllowed(string fullPath, IReadOnlyCollection<string> allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            string full;
            try
            {
                full = Path.GetFullPath(fullPath);
            }
            catch
            {
                return false;
            }

            // 1. Deny service/build directories anywhere along the path.
            if (GetPathSegments(full).Any(DeniedDirectoryNames.Contains))
                return false;

            // 2. Deny sensitive/secret file names.
            var fileName = Path.GetFileName(full);
            if (!string.IsNullOrWhiteSpace(fileName) && IsDeniedFileName(fileName))
                return false;

            // 3. Allow only inside one of the configured roots.
            if (allowedRoots == null || allowedRoots.Count == 0)
                return false;

            return allowedRoots.Any(root => !string.IsNullOrWhiteSpace(root) && IsUnder(root, full));
        }

        /// <summary>
        /// True when <paramref name="path"/> equals <paramref name="root"/> or is a descendant of it,
        /// using canonical full paths and a case-insensitive comparison (Windows file system).
        /// </summary>
        public static bool IsUnder(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                return false;

            string rootFull;
            string pathFull;
            try
            {
                rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                pathFull = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            return string.Equals(rootFull, pathFull, StringComparison.OrdinalIgnoreCase)
                || pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Mode-aware read gate. In Act mode the agent has full read access (any path, including
        /// secrets); in every other mode the strict <see cref="IsAllowed"/> policy is applied.
        /// </summary>
        public static bool IsReadAllowed(string fullPath, IReadOnlyCollection<string> allowedRoots, AgentMode mode)
        {
            if (mode == AgentMode.Act)
                return true;
            return IsAllowed(fullPath, allowedRoots);
        }

        private static IEnumerable<string> GetPathSegments(string fullPath)
        {
            var segments = new List<string>();
            var current = fullPath;
            while (!string.IsNullOrEmpty(current))
            {
                var leaf = Path.GetFileName(current);
                if (!string.IsNullOrEmpty(leaf))
                    segments.Add(leaf);

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                    break;
                current = parent;
            }
            return segments;
        }

        private static bool IsDeniedFileName(string fileName)
        {
            if (DeniedExactFileNames.Contains(fileName))
                return true;

            // appsettings.json / appsettings.Development.json / appsettings.Production.json ...
            if (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return true;

            if (DeniedFilePrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (DeniedFileSuffixes.Any(s => fileName.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return true;

            // *credentials* anywhere in the name (e.g. my-credentials.json, credentials.txt).
            if (fileName.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
