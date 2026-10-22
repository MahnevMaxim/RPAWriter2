using System;
using System.Runtime.InteropServices;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Shows the modern Windows folder picker (IFileDialog with FOS_PICKFOLDERS), which
    /// has an editable path field at the top. Unlike <see cref="System.Windows.Forms.FolderBrowserDialog"/>,
    /// the user can type or paste a path directly.
    /// </summary>
    public static class FolderPicker
    {
        /// <summary>
        /// Opens the folder picker and returns the selected path, or null when cancelled.
        /// </summary>
        public static string PickFolder(string title, string initialDirectory, IntPtr owner)
        {
            var dialog = (IFileDialog)new FileOpenDialogRcw();

            var options = dialog.GetOptions();
            dialog.SetOptions(options | Fos.PickFolders | Fos.ForceFileSystem | Fos.PathMustExist);

            if (!string.IsNullOrWhiteSpace(title))
                dialog.SetTitle(title);

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                var item = CreateShellItem(initialDirectory);
                if (item != null)
                {
                    try { dialog.SetFolder(item); }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }

            if (dialog.Show(owner) != 0)
                return null;

            dialog.GetResult(out IShellItem result);
            if (result == null)
                return null;

            try
            {
                result.GetDisplayName(Sigdn.FilesysPath, out var namePtr);
                if (namePtr == IntPtr.Zero)
                    return null;

                try
                {
                    return Marshal.PtrToStringUni(namePtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(namePtr);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(result);
            }
        }

        private static IShellItem CreateShellItem(string path)
        {
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, IidIShellItem, out IShellItem item);
                return item;
            }
            catch
            {
                return null;
            }
        }

        private static readonly Guid IidIShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);

            void SetFileTypeIndex(uint iFileType);

            void GetFileTypeIndex(out uint piFileType);

            void Advise(IntPtr pfde, out uint pdwCookie);

            void Unadvise(uint dwCookie);

            void SetOptions(Fos fos);

            Fos GetOptions();

            void SetDefaultFolder(IShellItem psi);

            void SetFolder(IShellItem psi);

            void GetFolder(out IShellItem ppsi);

            void GetCurrentSelection(out IShellItem ppsi);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

            void GetResult(out IShellItem ppsi);

            void AddPlace(IShellItem psi, int fdap);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

            void Close([MarshalAs(UnmanagedType.Error)] int hr);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

            void GetParent(out IShellItem ppsi);

            void GetDisplayName(Sigdn sigdnName, out IntPtr ppszName);

            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [Flags]
        private enum Fos : uint
        {
            PickFolders = 0x20,
            ForceFileSystem = 0x40,
            PathMustExist = 0x800
        }

        private enum Sigdn : uint
        {
            FilesysPath = 0x80058000
        }
    }
}
