using System;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// DTE-backed workspace provider: reports the open solution (or the open folder in "Open Folder"
    /// mode) as a <see cref="WorkspaceInfo"/> and raises <see cref="WorkspaceChanged"/> when it opens,
    /// closes or switches, so the host can re-scope the storages on the fly.
    /// </summary>
    public sealed class VsWorkspaceProvider : IWorkspaceProvider
    {
        private DTE2 _dte;
        private bool _hooked;

        public event EventHandler WorkspaceChanged;

        /// <summary>The current workspace, or null when neither a solution nor a folder is open.</summary>
        public WorkspaceInfo GetCurrentWorkspace()
        {
            try
            {
                var dte = GetDte();
                var isSolutionOpen = dte?.Solution != null && dte.Solution.IsOpen;
                var solutionPath = isSolutionOpen ? dte.Solution.FullName : null;

                // "Open Folder" mode reports Solution.IsOpen == false and an empty
                // Solution.FullName, so ask IVsSolution for the folder path instead.
                var folderPath = isSolutionOpen ? null : VsOpenFolderProbe.GetOpenFolderPath();

                return WorkspaceResolver.Resolve(isSolutionOpen, solutionPath, folderPath);
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
