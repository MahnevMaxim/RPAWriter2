using System;
using System.IO;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// DTE-backed workspace provider: reports the open solution as a <see cref="WorkspaceInfo"/>
    /// and raises <see cref="WorkspaceChanged"/> when it opens, closes or switches, so the host can
    /// re-scope the storages on the fly. Reuses the same DTE solution probe as the host's
    /// <c>GetOpenSolutionDirectory()</c>.
    /// </summary>
    public sealed class VsWorkspaceProvider : IWorkspaceProvider
    {
        private DTE2 _dte;
        private bool _hooked;

        public event EventHandler WorkspaceChanged;

        /// <summary>The current workspace, or null when no solution is open.</summary>
        public WorkspaceInfo GetCurrentWorkspace()
        {
            try
            {
                var dte = GetDte();
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;

                var slnPath = dte.Solution.FullName;
                if (string.IsNullOrWhiteSpace(slnPath))
                    return null;

                var directory = Path.GetDirectoryName(slnPath);
                if (string.IsNullOrWhiteSpace(directory))
                    return null;

                return new WorkspaceInfo
                {
                    Id = WorkspaceIdentity.ComputeId(directory),
                    Path = directory,
                    Name = Path.GetFileNameWithoutExtension(slnPath),
                    LastOpenedUtc = DateTime.UtcNow
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Subscribes to DTE solution events once; failures leave the provider in polling mode.</summary>
        public void Initialize()
        {
            if (_hooked)
                return;

            var dte = GetDte();
            if (dte == null)
                return;

            try
            {
                dte.Events.SolutionEvents.Opened += () => RaiseChanged();
                dte.Events.SolutionEvents.AfterClosing += () => RaiseChanged();
                _hooked = true;
            }
            catch
            {
                // Solution events unavailable — the host can still poll GetCurrentWorkspace().
            }
        }

        private void RaiseChanged() => WorkspaceChanged?.Invoke(this, EventArgs.Empty);

        private DTE2 GetDte()
        {
            if (_dte != null)
                return _dte;

            try
            {
                _dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            }
            catch
            {
                _dte = null;
            }

            return _dte;
        }
    }
}
