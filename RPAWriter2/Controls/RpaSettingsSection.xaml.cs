using System;
using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>RPA-domain settings section (host-specific, injected into the shared settings panel).</summary>
    public partial class RpaSettingsSection : UserControl
    {
        public RpaSettingsSection()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user asks to open the converter tool window.</summary>
        public event EventHandler OpenConverterRequested;

        private void OnOpenConverterClick(object sender, RoutedEventArgs e)
        {
            OpenConverterRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
