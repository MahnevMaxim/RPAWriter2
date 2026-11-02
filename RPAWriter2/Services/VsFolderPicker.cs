using System;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Shows the modern Windows folder picker as an <see cref="IFolderPicker"/>. The owner window
    /// handle may be provided through a delegate; when absent, the dialog opens without an owner.
    /// </summary>
    public sealed class VsFolderPicker : IFolderPicker
    {
        private readonly Func<IntPtr>? _owner;

        public VsFolderPicker()
            : this(null)
        {
        }

        public VsFolderPicker(Func<IntPtr>? owner)
        {
            _owner = owner;
        }

        public string? PickFolder(string title, string initialDirectory)
        {
            var owner = _owner?.Invoke() ?? IntPtr.Zero;
            return FolderPicker.PickFolder(title, initialDirectory, owner);
        }
    }
}
