using System;
using System.IO;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Returns the directory of the currently open Visual Studio solution, or null when none is
    /// open. Implements <see cref="ISolutionPathProvider"/> so models never talk to DTE directly.
    /// </summary>
    public sealed class VsSolutionPathProvider : ISolutionPathProvider
    {
        public string? GetSolutionDirectory()
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;

                var slnPath = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(slnPath) ? null : Path.GetDirectoryName(slnPath);
            }
            catch
            {
                return null;
            }
        }
    }
}
