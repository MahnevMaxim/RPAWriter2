using System;
using System.Windows;
using System.Windows.Controls;
using Agent.Toolkit.Wpf.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Commit editor view: branch label, message box, commit/auto-generate buttons and a warning
    /// banner. Git/DTE/AI logic stays in the host, which drives this control through its public
    /// API and handles the raised events.
    /// </summary>
    public partial class CommitEditorControl : UserControl
    {
        public CommitEditorControl()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user clicks Commit; carries the trimmed message text.</summary>
        public event EventHandler<string> CommitRequested;

        /// <summary>Raised when the user clicks Auto-generate message.</summary>
        public event EventHandler AutoGenerateRequested;

        /// <summary>Raised when the user closes the editor or the warning banner.</summary>
        public event EventHandler CloseRequested;

        /// <summary>Raised when the user asks to send the warning text to the chat.</summary>
        public event EventHandler<string> SendToChatRequested;

        /// <summary>The current commit message text.</summary>
        public string Message => CommitMessageBox.Text;

        /// <summary>Shows the editor (hides the warning banner) with the given branch label.</summary>
        public void ShowEditor(string branch)
        {
            Root.Visibility = Visibility.Visible;
            CommitWarningBar.Visibility = Visibility.Collapsed;
            CommitEditorPanel.Visibility = Visibility.Visible;
            CommitBranchLabel.Text = "branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch);
            CommitMessageBox.Clear();
            CommitStatusText.Text = "";
        }

        /// <summary>Shows the warning banner (hides the editor) with the given message.</summary>
        public void ShowWarning(string message)
        {
            Root.Visibility = Visibility.Visible;
            CommitWarningBar.Visibility = Visibility.Visible;
            CommitEditorPanel.Visibility = Visibility.Collapsed;
            CommitWarningBar.Kind = NotificationKind.Warning;
            CommitWarningBar.Message = message ?? "";
        }

        /// <summary>Collapses the whole control.</summary>
        public void Close()
        {
            Root.Visibility = Visibility.Collapsed;
            CommitWarningBar.Visibility = Visibility.Collapsed;
            CommitEditorPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Enables or disables the commit/auto-generate buttons.</summary>
        public void SetBusy(bool busy)
        {
            CommitSendButton.IsEnabled = !busy;
            CommitAutoGenButton.IsEnabled = !busy;
        }

        /// <summary>Sets the status text next to the buttons.</summary>
        public void SetStatus(string text) => CommitStatusText.Text = text ?? "";

        /// <summary>Sets the commit message text.</summary>
        public void SetMessage(string text) => CommitMessageBox.Text = text ?? "";

        /// <summary>Moves keyboard focus to the message box.</summary>
        public void FocusMessage() => CommitMessageBox.Focus();

        private void OnCommitClick(object sender, RoutedEventArgs e)
            => CommitRequested?.Invoke(this, CommitMessageBox.Text.Trim());

        private void OnAutoGenerateClick(object sender, RoutedEventArgs e)
            => AutoGenerateRequested?.Invoke(this, EventArgs.Empty);

        private void OnCloseClick(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke(this, EventArgs.Empty);

        private void OnWarningClose(object sender, RoutedEventArgs e)
            => CloseRequested?.Invoke(this, EventArgs.Empty);

        private void OnWarningSendToChat(object sender, RoutedEventArgs e)
        {
            if (sender is NotificationBanner banner && !string.IsNullOrWhiteSpace(banner.Message))
                SendToChatRequested?.Invoke(this, banner.Message);
        }
    }
}
