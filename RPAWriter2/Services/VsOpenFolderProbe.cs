using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Detects Visual Studio "Open Folder" mode and returns the opened folder path. DTE exposes
    /// no folder path when no solution is loaded (<c>Solution.IsOpen</c> is false and
    /// <c>Solution.FullName</c> is empty), so this goes through <c>IVsSolution</c>.
    /// </summary>
    internal static class VsOpenFolderProbe
    {
        /// <summary>Returns the opened folder path, or null when no folder is open.</summary>
        public static string GetOpenFolderPath()
        {
            try
            {
                var solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
                if (solution == null)
                    return null;

                if (!IsOpenFolderMode(solution))
                    return null;

                string directory;
                string file;
                string userOpts;
                if (solution.GetSolutionInfo(out directory, out file, out userOpts) != 0) // S_OK
                    return null;

                // In "Open Folder" mode GetSolutionInfo reports the folder as the "solution directory".
                return string.IsNullOrWhiteSpace(directory) ? null : directory;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsOpenFolderMode(IVsSolution solution)
        {
            object value;
            if (solution.GetProperty((int)__VSPROPID7.VSPROPID_IsInOpenFolderMode, out value) != 0) // S_OK
                return false;

            // VSPROPID_IsInOpenFolderMode is documented as VARIANT_TRUE, which marshals to bool;
            // some interop versions surface it as an int instead, so accept both.
            if (value is bool asBool)
                return asBool;
            if (value is int asInt)
                return asInt != 0;
            return false;
        }
    }
}
