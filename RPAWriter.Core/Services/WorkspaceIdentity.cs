using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Identifies a Visual Studio solution as a workspace. The workspace id is a lowercase
    /// SHA-1 of the canonical solution directory, so it is stable across sessions, safe as a
    /// folder/file name and independent of special characters or path length.
    /// </summary>
    public static class WorkspaceIdentity
    {
        /// <summary>
        /// Computes the workspace id for a solution directory (or .sln path). The value is
        /// deterministic: the same path always yields the same id. Returns empty for a null,
        /// empty or whitespace path.
        /// </summary>
        public static string ComputeId(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(Canonicalize(path)));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Normalizes a path so that cosmetic differences (trailing separators, casing, forward
        /// vs. back slashes on Windows) do not change the computed id.
        /// </summary>
        private static string Canonicalize(string path)
        {
            var value = path.Trim();
            value = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            value = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            // Windows paths are case-insensitive; on other platforms ToLowerInvariant is still a
            // safe canonicalization because the id is only used as a local folder name.
            return value.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Description of the currently open workspace (solution). Pure data: computed by the host
    /// provider and consumed by the storage scopes.
    /// </summary>
    public class WorkspaceInfo
    {
        /// <summary>Stable lowercase SHA-1 id used as the workspace folder name.</summary>
        public string Id { get; set; } = "";

        /// <summary>Directory of the solution, or empty when none.</summary>
        public string Path { get; set; } = "";

        /// <summary>Human-readable name (solution file name without extension).</summary>
        public string Name { get; set; } = "";

        /// <summary>When the workspace was last opened (UTC).</summary>
        public DateTime LastOpenedUtc { get; set; }
    }
}
