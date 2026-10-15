using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Build feedback: one-click Release.
    /// Reads compiler diagnostics (IErrorList) and the output pane (IVsOutputWindow) —
    /// stubs, wired up later.
    /// </summary>
    public class BuildFeedbackService : IBuildFeedbackService
    {
        public async Task BuildReleaseAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                throw new InvalidOperationException("No open solution.");
            dte.Solution.SolutionBuild?.SolutionConfigurations?.Item("Release")?.Activate();
            dte.Solution.SolutionBuild?.Build(true);
        }

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
        {
            // TODO: IErrorList / SVsErrorList — wire up during VS integration.
            return Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());
        }

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
        {
            // TODO: IVsOutputWindow / SVsOutputWindow — wire up during VS integration.
            return Task.FromResult(string.Empty);
        }
    }
}
