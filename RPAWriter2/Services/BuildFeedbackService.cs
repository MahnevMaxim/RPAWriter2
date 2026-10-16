using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Build feedback: one-click Release build, compiler diagnostics from the
    /// Visual Studio Error List, and text from the Output window build pane.
    /// </summary>
    public class BuildFeedbackService : IBuildFeedbackService
    {
        private static readonly Regex ErrorCodeRegex = new Regex(@"^\s*([A-Za-z]{2,}\d+)\s*:", RegexOptions.Compiled);

        private static readonly Guid[] OutputPaneGuids =
        {
            VSConstants.GUID_OutWindowBuildPane,
            VSConstants.GUID_OutWindowDebugPane,
            VSConstants.GUID_OutWindowGeneralPane
        };

        public async Task BuildReleaseAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                throw new InvalidOperationException("No open solution.");
            dte.Solution.SolutionBuild?.SolutionConfigurations?.Item("Release")?.Activate();
            dte.Solution.SolutionBuild?.Build(true);
        }

        public async Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var result = new List<BuildError>();
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null)
                return result;

            var errorItems = dte.ToolWindows.ErrorList?.ErrorItems;
            if (errorItems == null)
                return result;

            for (var i = 1; i <= errorItems.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = errorItems.Item(i);
                if (item == null)
                    continue;

                result.Add(new BuildError
                {
                    Code = ExtractErrorCode(item.Description),
                    Message = item.Description ?? string.Empty,
                    File = item.FileName ?? string.Empty,
                    Line = item.Line,
                    Severity = MapSeverity(item.ErrorLevel)
                });
            }

            return result;
        }

        public async Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return string.Empty;

            foreach (var paneGuid in OutputPaneGuids)
            {
                var guid = paneGuid;
                IVsOutputWindowPane pane;
                if (outputWindow.GetPane(ref guid, out pane) != VSConstants.S_OK || pane == null)
                    continue;

                var text = ReadPaneText(pane);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            return string.Empty;
        }

        private static string MapSeverity(vsBuildErrorLevel level)
        {
            switch (level)
            {
                case vsBuildErrorLevel.vsBuildErrorLevelHigh:
                    return "Error";
                case vsBuildErrorLevel.vsBuildErrorLevelMedium:
                    return "Warning";
                case vsBuildErrorLevel.vsBuildErrorLevelLow:
                    return "Message";
                default:
                    return level.ToString();
            }
        }

        private static string ExtractErrorCode(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            var match = ErrorCodeRegex.Match(description);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ReadPaneText(IVsOutputWindowPane pane)
        {
            if (pane == null)
                return string.Empty;

            IVsTextLines textLines;
            if (pane.GetTextLines(out textLines) != VSConstants.S_OK || textLines == null)
                return string.Empty;

            int lastLine;
            int lastIndex;
            if (textLines.GetLastLineIndex(out lastLine, out lastIndex) != VSConstants.S_OK)
                return string.Empty;

            string text;
            if (textLines.GetLineText(0, 0, lastLine, lastIndex, out text) != VSConstants.S_OK)
                return string.Empty;

            return text ?? string.Empty;
        }
    }
}
