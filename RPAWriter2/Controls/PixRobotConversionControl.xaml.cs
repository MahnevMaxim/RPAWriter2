using System;
using System.Windows;
using System.Windows.Controls;
using RPAWriter2.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Inline panel that collects a PIX robot folder and a target MWF folder, then raises
    /// <see cref="StartRequested"/> so the host can run the folder → folder conversion.
    /// </summary>
    public partial class PixRobotConversionControl : UserControl
    {
        public PixRobotConversionControl()
        {
            InitializeComponent();
            RestoreLastUsedFolders();

            PixFolderBox.LostKeyboardFocus += (_, __) => PersistFolders();
            MwfFolderBox.LostKeyboardFocus += (_, __) => PersistFolders();
        }

        private void RestoreLastUsedFolders()
        {
            var state = PixRobotConversionStateStore.Load();
            PixFolder = state.PixFolder;
            MwfFolder = state.MwfFolder;
        }

        private void PersistFolders()
        {
            PixRobotConversionStateStore.Save(new PixRobotConversionState
            {
                PixFolder = PixFolder ?? string.Empty,
                MwfFolder = MwfFolder ?? string.Empty
            });
        }

        /// <summary>Raised when the user clicks "Start conversion".</summary>
        public event EventHandler StartRequested;

        /// <summary>Raised when the user clicks "Close"; the host hides the panel.</summary>
        public event EventHandler CloseRequested;

        /// <summary>Optional sink for progress/validation messages.</summary>
        public Action<string> Log { get; set; }

        /// <summary>Currently entered PIX robot folder.</summary>
        public string PixFolder
        {
            get => PixFolderBox.Text?.Trim();
            set => PixFolderBox.Text = value ?? string.Empty;
        }

        /// <summary>Currently entered target MWF folder.</summary>
        public string MwfFolder
        {
            get => MwfFolderBox.Text?.Trim();
            set => MwfFolderBox.Text = value ?? string.Empty;
        }

        /// <summary>Toggles the busy state: disables inputs and updates the start button label.</summary>
        public void SetBusy(bool busy)
        {
            StartButton.IsEnabled = !busy;
            PixBrowseButton.IsEnabled = !busy;
            MwfBrowseButton.IsEnabled = !busy;
            PixFolderBox.IsEnabled = !busy;
            MwfFolderBox.IsEnabled = !busy;
            StartButton.Content = busy ? "Converting..." : "Start conversion";
        }

        private void OnBrowsePixFolder(object sender, RoutedEventArgs e)
        {
            var folder = FolderPicker.PickFolder("Select the PIX robot folder", PixFolder, GetOwner());
            if (folder != null)
            {
                PixFolder = folder;
                PersistFolders();
            }
        }

        private void OnBrowseMwfFolder(object sender, RoutedEventArgs e)
        {
            var folder = FolderPicker.PickFolder("Select the MWF robot folder (empty template)", MwfFolder, GetOwner());
            if (folder != null)
            {
                MwfFolder = folder;
                PersistFolders();
            }
        }

        private void OnStart(object sender, RoutedEventArgs e)
        {
            var pix = PixFolder;
            var mwf = MwfFolder;

            if (string.IsNullOrWhiteSpace(pix))
            {
                Log?.Invoke("Please choose the PIX robot folder first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(mwf))
            {
                Log?.Invoke("Please choose the target MWF folder first.");
                return;
            }

            PersistFolders();
            StartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnClose(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke(this, EventArgs.Empty);

        private IntPtr GetOwner()
        {
            var window = Window.GetWindow(this);
            return window == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }
    }
}
