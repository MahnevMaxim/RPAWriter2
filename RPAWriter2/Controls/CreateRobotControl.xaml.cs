using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Create-robot panel view: a digits-only number box and a Create button. The host performs
    /// the actual robot creation when <see cref="CreateRequested"/> is raised.
    /// </summary>
    public partial class CreateRobotControl : UserControl
    {
        public CreateRobotControl()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user clicks Create robot; carries the trimmed number text.</summary>
        public event EventHandler<string> CreateRequested;

        /// <summary>Shows or hides the panel.</summary>
        public void SetVisible(bool visible)
            => Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Clears the number box.</summary>
        public void Clear() => RobotNumberBox.Clear();

        private void OnCreateClick(object sender, RoutedEventArgs e)
            => CreateRequested?.Invoke(this, (RobotNumberBox?.Text ?? "").Trim());

        private void OnRobotNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = e.Text.Any(c => !char.IsDigit(c));
    }
}
