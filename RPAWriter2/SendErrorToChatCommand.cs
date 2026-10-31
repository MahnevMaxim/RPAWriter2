using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter2.Services;

namespace RPAWriter2
{
    /// <summary>
    /// Handles the "Send to RPA Writer" command in the Visual Studio Error List context menu.
    /// Reads the selected error item(s) and forwards them to the agent chat.
    /// </summary>
    internal sealed class SendErrorToChatCommand
    {
        /// <summary>Command ID (must match the VSCT IDSymbol).</summary>
        public const int CommandId = 4130;

        /// <summary>Command menu group (command set GUID).</summary>
        public static readonly Guid CommandSet = new Guid("d648562d-8124-4a1f-9574-4874fa421738");

        private static readonly Regex ErrorCodeRegex =
            new Regex(@"^\s*([A-Za-z]{2,}\d+)\s*:", RegexOptions.Compiled);

        private readonly AsyncPackage package;

        private SendErrorToChatCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var commandId = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, commandId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Gets the singleton instance of the command.</summary>
        public static SendErrorToChatCommand Instance { get; private set; }

        /// <summary>Initializes the singleton instance of the command.</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new SendErrorToChatCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            this.package.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    await SendSelectedErrorsAsync();
                }
                catch (Exception ex)
                {
                    await ShowErrorAsync(ex);
                }
            });
        }
        private async Task SendSelectedErrorsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                throw new InvalidOperationException("Visual Studio is not available.");

            var errors = ReadSelectedErrorListEntries(dte);

            var window = await this.package.ShowToolWindowAsync(typeof(MainWindowPane), 0, true, this.package.DisposalToken) as MainWindowPane;
            var control = window?.Control;
            if (control == null)
                throw new InvalidOperationException("Cannot create the RPA Writer window.");

            if (errors.Count == 0)
            {
                control.AppendSystemMessage("Send to RPA Writer: no error is selected in the Error List.");
                return;
            }

            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(
                errors,
                async (file, line, ctx) =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var doc = await VsDocumentService.OpenOrActivateAsync(dte, file).ConfigureAwait(true);
                    if (doc == null)
                        throw new InvalidOperationException("could not open in the editor");
                    return VsDocumentService.ReadDocumentWindow(doc, line, ctx);
                });

            await control.SubmitErrorToChatAsync(text);
        }

        /// <summary>
        /// Reads the entries currently selected in the Visual Studio Error List through the
        /// modern Error List table control (<see cref="IErrorList"/>). The Error List
        /// tool-window object returned by <c>dte.ToolWindows.ErrorList</c> implements
        /// <see cref="IErrorList"/>; unlike <c>EnvDTE.ErrorList.SelectedItems</c>, its
        /// <c>TableControl.SelectedEntries</c> reflects the rows the user actually
        /// highlighted in the Error List window.
        /// </summary>
        private static List<BuildError> ReadSelectedErrorListEntries(DTE2 dte)
        {
            var result = new List<BuildError>();

            var errorList = dte.ToolWindows?.ErrorList as IErrorList
                            ?? Package.GetGlobalService(typeof(SVsErrorList)) as IErrorList;
            var selectedEntries = errorList?.TableControl?.SelectedEntries;
            if (selectedEntries == null)
                return result;

            foreach (var handle in selectedEntries)
            {
                try
                {
                    if (handle == null || !handle.TryGetEntry(out var entry) || entry == null)
                        continue;

                    var message = GetString(entry, StandardTableKeyNames.Text)
                                  ?? GetString(entry, StandardTableKeyNames.FullText)
                                  ?? string.Empty;

                    var severityValue = GetObject(entry, StandardTableKeyNames.ErrorSeverity);

                    result.Add(new BuildError
                    {
                        Code = GetString(entry, StandardTableKeyNames.ErrorCode) ?? ExtractErrorCode(message),
                        Message = message,
                        File = GetString(entry, StandardTableKeyNames.DocumentName) ?? string.Empty,
                        Line = GetOneBasedLine(entry),
                        Column = GetOneBasedColumn(entry),
                        Project = GetString(entry, StandardTableKeyNames.ProjectName) ?? string.Empty,
                        Severity = MapSeverity(severityValue),
                        ErrorLevel = ToErrorLevel(severityValue),
                        ErrorLevelName = ToErrorLevelName(severityValue),
                        Category = GetErrorCategory(entry)
                    });
                }
                catch
                {
                    // Ignore an individual item that cannot be read.
                }
            }

            return result;
        }

        private static string? GetString(ITableEntry entry, string key)
            => entry.TryGetValue(key, out var value) ? value as string : null;

        private static object? GetObject(ITableEntry entry, string key)
            => entry.TryGetValue(key, out var value) ? value : null;

        private static int GetOneBasedLine(ITableEntry entry)
        {
            if (!entry.TryGetValue(StandardTableKeyNames.Line, out var value))
                return 0;

            int line;
            try
            {
                line = Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }

            // The Error List stores StandardTableKeyNames.Line as 0-based, while the editor
            // and CompilationErrorContextBuilder expect 1-based line numbers.
            return line < 0 ? 0 : line + 1;
        }

        private static int GetOneBasedColumn(ITableEntry entry)
        {
            if (!entry.TryGetValue(StandardTableKeyNames.Column, out var value))
                return 0;

            int column;
            try
            {
                column = Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }

            // Same 0-based convention as StandardTableKeyNames.Line (see GetOneBasedLine).
            return column < 0 ? 0 : column + 1;
        }

        private static int ToErrorLevel(object? value)
        {
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static string ToErrorLevelName(object? value)
        {
            return value?.ToString() ?? string.Empty;
        }

        private static string GetErrorCategory(ITableEntry entry)
        {
            var text = GetString(entry, StandardTableKeyNames.ErrorCategory);
            if (!string.IsNullOrEmpty(text))
                return text;
            return GetObject(entry, StandardTableKeyNames.ErrorCategory)?.ToString() ?? string.Empty;
        }

        private static string MapSeverity(object? value)
        {
            // StandardTableKeyNames.ErrorSeverity has the expected data type __VSERRORCATEGORY:
            // EC_ERROR = 0, EC_WARNING = 1, EC_MESSAGE = 2.
            int level;
            try
            {
                level = Convert.ToInt32(value);
            }
            catch
            {
                return "Message";
            }

            switch (level)
            {
                case 0:
                    return "Error";
                case 1:
                    return "Warning";
                default:
                    return "Message";
            }
        }

        private static string ExtractErrorCode(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            var match = ErrorCodeRegex.Match(description);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private async Task ShowErrorAsync(Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);
            try
            {
                var window = await this.package.ShowToolWindowAsync(typeof(MainWindowPane), 0, true, this.package.DisposalToken) as MainWindowPane;
                window?.Control?.AppendSystemMessage("⚠ Failed to send the error to RPA Writer: " + ex.Message);
            }
            catch
            {
                // Last-resort fallback: write to the activity log.
                ActivityLog.LogError("RPA Writer", "Failed to send Error List item: " + ex);
            }
        }
    }
}
