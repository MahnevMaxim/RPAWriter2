using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Agent.Core.Models;
using Agent.Toolkit.Wpf.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Domain "Spec" tab: deploy/analyze-log buttons, spec import progress, spec transcript and the
    /// production-log analysis panel. Pure view logic (copy output, notice close, robot-name enter)
    /// lives here; shell services (deploy, log analysis, notifications) are reached through events.
    /// </summary>
    public partial class SpecTabControl : UserControl
    {
        public SpecTabControl()
        {
            InitializeComponent();
        }

        // ---- Elements the shell needs to drive (documentation/20-tabcontrol-extraction.md, stage 4). ----

        public ChatMessagesControl SpecMessages => SpecMessagesControl;
        public NotificationBanner NoticeBar => NoticeBarControl;
        public NotificationBanner DeployToast => DeployToastControl;
        public Grid AnalyzeLogsPanel => AnalyzeLogsPanelControl;
        public Grid ProdLogsPanel => ProdLogsPanelControl;
        public ComboBox MinLevelCombo => MinLevelComboControl;
        public DatePicker FromDatePicker => FromDatePickerControl;
        public DatePicker ToDatePicker => ToDatePickerControl;
        public ComboBox RobotNameBox => RobotNameBoxControl;
        public DataGrid TasksGrid => TasksGridControl;

        // ---- Shell-service callbacks. ----

        public event EventHandler<RoutedEventArgs>? DeployRequested;
        public event EventHandler<RoutedEventArgs>? AnalyzeLogsRequested;
        public event EventHandler<RoutedEventArgs>? CloseAnalyzeLogsRequested;
        public event EventHandler<RoutedEventArgs>? AnalyzeLogsFromOutputWindowRequested;
        public event EventHandler<RoutedEventArgs>? AnalyzeLogsFromProdServersRequested;
        public event EventHandler<RoutedEventArgs>? LoadProdTasksRequested;
        public event EventHandler<RoutedEventArgs>? AnalyzeProdLogsRequested;
        public event EventHandler<RoutedEventArgs>? NotificationSendToChatRequested;
        public event EventHandler<RoutedEventArgs>? DeployToastCloseRequested;
        public event EventHandler? RobotNameEnterPressed;
        public event EventHandler<SelectionChangedEventArgs>? TaskSelectionChanged;
        public event EventHandler<SelectionChangedEventArgs>? MinLevelSelectionChanged;
        public event EventHandler<SelectionChangedEventArgs>? DateRangeSelectionChanged;

        // ---- View handlers forwarded to the shell. ----

        private void OnDeploy(object sender, RoutedEventArgs e) => DeployRequested?.Invoke(this, e);
        private void OnAnalyzeLogs(object sender, RoutedEventArgs e) => AnalyzeLogsRequested?.Invoke(this, e);
        private void OnCloseAnalyzeLogs(object sender, RoutedEventArgs e) => CloseAnalyzeLogsRequested?.Invoke(this, e);
        private void OnAnalyzeLogsFromOutputWindow(object sender, RoutedEventArgs e) => AnalyzeLogsFromOutputWindowRequested?.Invoke(this, e);
        private void OnAnalyzeLogsFromProdServers(object sender, RoutedEventArgs e) => AnalyzeLogsFromProdServersRequested?.Invoke(this, e);
        private void OnLoadProdTasks(object sender, RoutedEventArgs e) => LoadProdTasksRequested?.Invoke(this, e);
        private void OnAnalyzeProdLogs(object sender, RoutedEventArgs e) => AnalyzeProdLogsRequested?.Invoke(this, e);
        private void OnNotificationSendToChat(object sender, RoutedEventArgs e) => NotificationSendToChatRequested?.Invoke(sender, e);
        private void OnDeployToastClose(object sender, RoutedEventArgs e) => DeployToastCloseRequested?.Invoke(this, e);
        private void OnTaskSelected(object sender, SelectionChangedEventArgs e) => TaskSelectionChanged?.Invoke(this, e);
        private void OnMinLevelChanged(object sender, SelectionChangedEventArgs e) => MinLevelSelectionChanged?.Invoke(this, e);
        private void OnDateRangeChanged(object sender, SelectionChangedEventArgs e) => DateRangeSelectionChanged?.Invoke(this, e);

        private void OnRobotNameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            RobotNameEnterPressed?.Invoke(this, EventArgs.Empty);
        }

        // ---- Pure view handlers. ----

        private void OnCopySpecOutput(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var m in SpecMessages.Messages)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(FormatMessageForCopy(m));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnNoticeClose(object sender, RoutedEventArgs e)
            => NoticeBar.Visibility = Visibility.Collapsed;

        private static string FormatMessageForCopy(ChatMessage message)
        {
            var sb = new StringBuilder();
            sb.Append(message.DisplayText);
            if (!string.IsNullOrWhiteSpace(message.Reasoning))
            {
                sb.AppendLine();
                sb.AppendLine("[Reasoning]");
                sb.Append(message.Reasoning);
            }
            return sb.ToString();
        }
    }
}
