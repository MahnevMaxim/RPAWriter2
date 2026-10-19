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
    /// Build feedback: one-click build with a given solution configuration, compiler diagnostics from the
    /// Visual Studio Error List, and text from the Output window build pane.
    /// </summary>
    public class BuildFeedbackService : IBuildFeedbackService
    {
        private static readonly Regex ErrorCodeRegex = new Regex(@"^\s*([A-Za-z]{2,}\d+)\s*:", RegexOptions.Compiled);

        private static readonly string[] OutputPaneNames = { "Build", "Debug", "General" };

        public async Task BuildAsync(string configuration, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                throw new InvalidOperationException("No open solution.");

            var build = dte.Solution.SolutionBuild;
            build?.SolutionConfigurations?.Item(configuration)?.Activate();
            try
            {
                build?.Build(true);
            }
            finally
            {
                // Always switch the solution back to Debug after a build.
                build?.SolutionConfigurations?.Item("Debug")?.Activate();
            }
        }

        public async Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                return false;

            var configurations = dte.Solution.SolutionBuild?.SolutionConfigurations;
            if (configurations == null)
                return false;

            try
            {
                return configurations.Item(configuration) != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                throw new InvalidOperationException("Visual Studio is not available.");
            if (dte.Solution == null || !dte.Solution.IsOpen)
                throw new InvalidOperationException("No open solution.");

            var errorItems = dte.ToolWindows.ErrorList?.ErrorItems;
            if (errorItems == null)
                throw new InvalidOperationException("The Visual Studio Error List is not available.");

            var result = new List<BuildError>();
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
                    Column = item.Column,
                    Project = item.Project ?? string.Empty,
                    Severity = MapSeverity(item.ErrorLevel),
                    ErrorLevel = (int)item.ErrorLevel,
                    ErrorLevelName = item.ErrorLevel.ToString()
                });
            }

            return result;
        }

        public async Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var panes = dte?.ToolWindows.OutputWindow?.OutputWindowPanes;
            if (panes == null)
                return string.Empty;

            foreach (var paneName in OutputPaneNames)
            {
                OutputWindowPane pane;
                try
                {
                    pane = panes.Item(paneName);
                }
                catch
                {
                    continue;
                }

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

        private static string ReadPaneText(OutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pane?.TextDocument == null)
                return string.Empty;

            try
            {
                var editPoint = pane.TextDocument.StartPoint.CreateEditPoint();
                return editPoint.GetText(pane.TextDocument.EndPoint) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
