using System;
using System.IO;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Decides what the current workspace is from the open solution/folder, and turns it into a
    /// <see cref="WorkspaceInfo"/>. Pure and deterministic so the decision logic is unit-testable
    /// without DTE; the VSIX provider feeds it the raw values it reads from Visual Studio.
    /// </summary>
    public static class WorkspaceResolver
    {
        /// <summary>
        /// Builds the workspace description, or null when neither a solution nor a folder is open.
        /// An open solution wins over a folder; empty/whitespace paths are treated as unavailable.
        /// </summary>
        public static WorkspaceInfo Resolve(bool isSolutionOpen, string solutionPath, string folderPath)
        {
            if (isSolutionOpen && !string.IsNullOrWhiteSpace(solutionPath))
            {
                var directory = Path.GetDirectoryName(solutionPath);
                if (string.IsNullOrWhiteSpace(directory))
                    return null;

                return new WorkspaceInfo
                {
                    Id = WorkspaceIdentity.ComputeId(directory),
                    Path = directory,
                    Name = Path.GetFileNameWithoutExtension(solutionPath),
                    LastOpenedUtc = DateTime.UtcNow
                };
            }

            var folder = TrimEndingSeparators(folderPath);
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            var name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name))
                name = folder;

            return new WorkspaceInfo
            {
                Id = WorkspaceIdentity.ComputeId(folder),
                Path = folder,
                Name = name,
                LastOpenedUtc = DateTime.UtcNow
            };
        }

        private static string TrimEndingSeparators(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
