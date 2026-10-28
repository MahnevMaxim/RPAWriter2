using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using RPAWriter.Core.Checkpoints;

namespace RPAWriter2
{
    /// <summary>
    /// Opens the <see cref="DiffToolWindow"/> from the checkpoint Compare action and
    /// pushes the computed diff into it. No menu command is registered; the window is
    /// shown programmatically.
    /// </summary>
    internal sealed class DiffToolWindowCommand
    {
        private readonly AsyncPackage _package;

        private DiffToolWindowCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>Gets the package-owned singleton instance.</summary>
        public static DiffToolWindowCommand? Instance { get; private set; }

        /// <summary>Initializes the singleton instance of the command.</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            Instance = new DiffToolWindowCommand(package);
        }

        /// <summary>Shows the diff tool window and renders the given files.</summary>
        public void ShowWindow(IReadOnlyList<DiffFileModel> files, string title)
        {
            _package.JoinableTaskFactory.RunAsync(async delegate
            {
                var window = await _package.ShowToolWindowAsync(typeof(DiffToolWindow), 0, true, _package.DisposalToken) as DiffToolWindow;
                if (window?.Frame is null)
                    throw new NotSupportedException("Cannot create tool window");

                window.Control?.ShowDiff(files, title);
            });
        }
    }
}
