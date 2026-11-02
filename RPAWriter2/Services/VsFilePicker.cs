using Microsoft.Win32;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Shows the WPF <see cref="OpenFileDialog"/> as an <see cref="IFilePicker"/>, so models can
    /// request a file without referencing the dialog types.
    /// </summary>
    public sealed class VsFilePicker : IFilePicker
    {
        public string? PickFile(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
