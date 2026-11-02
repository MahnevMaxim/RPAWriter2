using System.Windows;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// <see cref="IFileOverwritePrompter"/> backed by <see cref="MessageBox"/>. The three buttons
    /// (Yes / No / Cancel) map to <see cref="FileOverwriteChoice"/>.
    /// </summary>
    public sealed class MessageBoxFileOverwritePrompter : IFileOverwritePrompter
    {
        public FileOverwriteChoice AskOverwrite(string displayName)
        {
            var choice = MessageBox.Show(
                "File " + displayName + " already exists.\n\n" +
                "Yes — overwrite it entirely.\n" +
                "No — find discrepancies and propose a partial fix.\n" +
                "Cancel — leave it unchanged.",
                "File already exists",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    return FileOverwriteChoice.Overwrite;
                case MessageBoxResult.No:
                    return FileOverwriteChoice.ApplyFixes;
                default:
                    return FileOverwriteChoice.Keep;
            }
        }
    }
}
