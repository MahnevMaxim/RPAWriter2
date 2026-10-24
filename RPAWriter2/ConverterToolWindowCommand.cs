using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace RPAWriter2
{
    /// <summary>
    /// Command handler that opens the Converter tool window from View → Other Windows.
    /// </summary>
    internal sealed class ConverterToolWindowCommand
    {
        /// <summary>Command ID.</summary>
        public const int CommandId = 4134;

        /// <summary>Command menu group (command set GUID, shared with the other RPA Writer commands).</summary>
        public static readonly Guid CommandSet = new Guid("d648562d-8124-4a1f-9574-4874fa421738");

        /// <summary>VS Package that provides this command, not null.</summary>
        private readonly AsyncPackage package;

        /// <summary>Initializes a new instance of the <see cref="ConverterToolWindowCommand"/> class.</summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private ConverterToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Gets the instance of the command.</summary>
        public static ConverterToolWindowCommand Instance
        {
            get;
            private set;
        }

        /// <summary>Initializes the singleton instance of the command.</summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread — AddCommand requires the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new ConverterToolWindowCommand(package, commandService);
        }

        /// <summary>Shows the tool window when the menu item is clicked.</summary>
        private void Execute(object sender, EventArgs e)
        {
            ShowWindow();
        }

        /// <summary>Shows the Converter tool window (menu command and Settings tab shortcut).</summary>
        public void ShowWindow()
        {
            this.package.JoinableTaskFactory.RunAsync(async delegate
            {
                ToolWindowPane window = await this.package.ShowToolWindowAsync(typeof(ConverterToolWindow), 0, true, this.package.DisposalToken);
                if ((null == window) || (null == window.Frame))
                {
                    throw new NotSupportedException("Cannot create tool window");
                }
            });
        }
    }
}
