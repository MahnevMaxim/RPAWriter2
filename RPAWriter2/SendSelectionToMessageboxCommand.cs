using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RPAWriter2
{
    /// <summary>
    /// Handles the "Send to RPA Writer" command in the Visual Studio code editor context menu.
    /// Reads the selected text from the active editor and appends it to the agent chat input box.
    /// </summary>
    internal sealed class SendSelectionToMessageboxCommand
    {
        /// <summary>Command ID (must match the VSCT IDSymbol).</summary>
        public const int CommandId = 4132;

        /// <summary>Command menu group (command set GUID).</summary>
        public static readonly Guid CommandSet = new Guid("d648562d-8124-4a1f-9574-4874fa421738");

        private readonly AsyncPackage package;

        private SendSelectionToMessageboxCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var commandId = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, commandId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Gets the singleton instance of the command.</summary>
        public static SendSelectionToMessageboxCommand Instance { get; private set; }

        /// <summary>Initializes the singleton instance of the command.</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new SendSelectionToMessageboxCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            this.package.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    await SendSelectedTextAsync();
                }
                catch (Exception ex)
                {
                    await ShowErrorAsync(ex);
                }
            });
        }

        private async Task SendSelectedTextAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                throw new InvalidOperationException("Visual Studio is not available.");

            var doc = dte.ActiveDocument;
            if (doc == null)
            {
                await NotifyAsync("Send to RPA Writer: no document is open in the editor.");
                return;
            }

            var textDoc = doc.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                await NotifyAsync("Send to RPA Writer: the active document is not a text document.");
                return;
            }

            var selectedText = ReadSelectedText(textDoc);
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                await NotifyAsync("Send to RPA Writer: no text is selected in the editor.");
                return;
            }

            var relativePath = GetRelativePath(GetSolutionDirectory(dte), doc.FullName);
            var message = BuildMessage(relativePath, selectedText);

            var window = await this.package.ShowToolWindowAsync(typeof(ToolWindow1), 0, true, this.package.DisposalToken) as ToolWindow1;
            var control = window?.Control;
            if (control == null)
                throw new InvalidOperationException("Cannot create the RPA Writer window.");

            control.AppendSelectionToInput(message);
        }

        /// <summary>Returns the open solution's directory, or null when no solution is open.</summary>
        private static string? GetSolutionDirectory(DTE2 dte)
        {
            try
            {
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;
                var path = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads the selected text from the editor. When nothing is selected, falls back to
        /// the current line so the command still produces a useful snippet.
        /// </summary>
        private static string ReadSelectedText(TextDocument textDoc)
        {
            var selection = textDoc.Selection;
            if (selection == null)
                return string.Empty;

            var text = selection.Text;
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            try
            {
                var line = selection.TopPoint.Line;
                var editPoint = selection.TopPoint.CreateEditPoint();
                var lineText = editPoint.GetLines(line, line + 1);
                return lineText?.TrimEnd('\r', '\n') ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Computes the document path relative to the solution root (forward slashes), falling
        /// back to the bare file name when the solution is unavailable or the path cannot be normalized.
        /// </summary>
        private static string GetRelativePath(string? solutionDir, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "(untitled)";

            try
            {
                if (!string.IsNullOrWhiteSpace(solutionDir))
                {
                    var baseFull = Path.GetFullPath(solutionDir)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var fileFull = Path.GetFullPath(filePath);
                    if (fileFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                        return fileFull.Substring(baseFull.Length).Replace('\\', '/');
                }
            }
            catch
            {
                // Fall back to the file name when the path cannot be normalized.
            }

            return Path.GetFileName(filePath);
        }

        private static string BuildMessage(string relativePath, string selectedText)
        {
            var sb = new StringBuilder();
            sb.Append('\'').Append(relativePath).Append("' (see below for file content)").AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(selectedText);
            sb.Append("```");
            return sb.ToString();
        }

        private async Task NotifyAsync(string message)
        {
            try
            {
                var window = await this.package.ShowToolWindowAsync(typeof(ToolWindow1), 0, true, this.package.DisposalToken) as ToolWindow1;
                window?.Control?.AppendSystemMessage(message);
            }
            catch
            {
                // The notice is best-effort; never break the command on failure.
            }
        }

        private async Task ShowErrorAsync(Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);
            try
            {
                var window = await this.package.ShowToolWindowAsync(typeof(ToolWindow1), 0, true, this.package.DisposalToken) as ToolWindow1;
                window?.Control?.AppendSystemMessage("\u26A0 Failed to send the selection to RPA Writer: " + ex.Message);
            }
            catch
            {
                // Last-resort fallback: write to the activity log.
                ActivityLog.LogError("RPA Writer", "Failed to send editor selection: " + ex);
            }
        }
    }
}
