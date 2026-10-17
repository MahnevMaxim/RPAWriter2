using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Writes RPA Writer diagnostics/progress into a dedicated Visual Studio
    /// Output window pane named "RPA Writer".
    /// </summary>
    public class OutputWindowService
    {
        public const string PaneName = "RPA Writer";

        private static readonly Guid PaneGuid = new Guid("9A6B2F1C-3E4D-4B5C-9D8E-7F6A5B4C3D2E");

        private IVsOutputWindowPane? _pane;

        /// <summary>Creates an IProgress&lt;string&gt; that writes each reported line to the pane.</summary>
        public IProgress<string> CreateProgress() => new Progress<string>(WriteLine);

        /// <summary>Shows/activates the "RPA Writer" pane.</summary>
        public void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsurePane()?.Activate();
        }

        /// <summary>Writes one line (with a trailing newline) to the pane.</summary>
        public void WriteLine(string message)
        {
            if (!ThreadHelper.CheckAccess())
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    WriteLineCore(message);
                });
                return;
            }

            WriteLineCore(message);
        }

        private void WriteLineCore(string message)
        {
            EnsurePane()?.OutputString((message ?? string.Empty) + Environment.NewLine);
        }

        private IVsOutputWindowPane? EnsurePane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane != null)
                return _pane;

            var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return null;

            var guid = PaneGuid;
            // CreatePane is idempotent on the GUID: S_OK when created, S_FALSE when it already exists.
            int hr = outputWindow.CreatePane(ref guid, PaneName, fInitVisible: 1, fClearWithSolution: 0);
            if (hr >= 0)
                outputWindow.GetPane(ref guid, out _pane);

            return _pane;
        }
    }
}
