using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Thin Visual Studio layer over the live <see cref="VisualStudioWorkspace"/>.
    /// Unlike an external <c>MSBuildWorkspace</c>, this sees the user's unsaved editor
    /// buffers and the current compilation, so the semantic tools reflect what the user
    /// actually has in the IDE. The analysis itself lives in the pure
    /// <see cref="RPAWriter.Roslyn.RoslynAnalysis"/> layer.
    /// </summary>
    public sealed class RoslynWorkspaceService
    {
        private static readonly object Gate = new object();
        private static VisualStudioWorkspace _workspace;
        private static bool _resolved;

        /// <summary>
        /// Resolves the singleton VisualStudioWorkspace once, on the VS UI thread.
        /// Idempotent and safe to call from any thread via JoinableTaskFactory.
        /// </summary>
        public async Task EnsureResolvedAsync(CancellationToken cancellationToken = default)
        {
            if (_resolved)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (_resolved)
                return;

            try
            {
                var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                _workspace = componentModel?.GetService<VisualStudioWorkspace>();
            }
            catch
            {
                _workspace = null;
            }
            _resolved = true;
        }

        private VisualStudioWorkspace Workspace => _workspace;

        /// <summary>Returns the live Roslyn document for the given absolute file path, or null.</summary>
        public Task<Document?> GetDocumentAsync(string fullPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || _workspace == null)
                return Task.FromResult<Document?>(null);

            var solution = _workspace.CurrentSolution;
            var documentIds = solution.GetDocumentIdsWithFilePath(fullPath);
            if (documentIds.Length == 0)
                return Task.FromResult<Document?>(null);

            return Task.FromResult(solution.GetDocument(documentIds[0]));
        }

        /// <summary>Returns the live solution of the open workspace, or null when unavailable.</summary>
        public Solution GetSolution()
        {
            return _workspace?.CurrentSolution;
        }
    }
}
