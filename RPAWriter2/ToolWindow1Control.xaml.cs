using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System;
using System.Globalization;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter2.Services;
using RPAWriter2.Services.AgentTools;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Input;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RPAWriter.Core.Models;

namespace RPAWriter2
{
    /// <summary>
    /// Single extension window: agent chat and settings.
    /// </summary>
    public partial class ToolWindow1Control : UserControl
    {
        /// <summary>Explicit UI phase of the current agent turn (drives the footer buttons/indicators).</summary>
        private enum AgentPhase
        {
            Idle,
            Streaming,
            Error
        }

        private SettingsData _settings;
        private IAiProvider _ai;
        private SpecModel _currentSpec;
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
        private OutputWindowService _outputWindow;
        private AgentMode _mode = AgentMode.Plan;
        private AgentPhase _phase = AgentPhase.Idle;
        private AgentToolRegistry? _toolRegistry;
        private AgentRunner? _agentRunner;
        private ITokenUsageProvider? _tokenUsage;
        private IMessageNotifier? _messageNotifier;
        private IRetryNotifier? _retryNotifier;
        private DTE2? _dte;
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private readonly ObservableCollection<ChatMessage> _specMessages = new ObservableCollection<ChatMessage>();
        private CancellationTokenSource? _chatCts;
        private CancellationTokenSource? _kbImportCts;
        private volatile bool _kbImportPaused;
        private readonly DispatcherTimer _chatStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _chatStartedAt;
        private const string SpecLockedMessage = "The spec file is open in another application (e.g. Word/Excel). Close it and try again.";
        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;
        private readonly ObservableCollection<ChatConversation> _conversations = new ObservableCollection<ChatConversation>();
        private ChatConversation _currentConversation = new ChatConversation();
        private readonly DispatcherTimer _historySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        private bool _loadingHistory;
        private bool _updatingHistoryCombo;

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            ChatList.ItemsSource = _messages;
            SpecList.ItemsSource = _specMessages;
            LoadSettings();
            RecreateAiProvider();
            _buildFeedback = new BuildFeedbackService();
            _robotTemplate = new RobotTemplateService();
            _outputWindow = new OutputWindowService();
            RebuildAgentTools();
            HookSolutionEvents();
            UpdateCreateRobotVisibility();
            InitializeChatHistory();
        }

        private void LoadSettings()
        {
            _settings = SettingsStore.Load();
            BaseUrlBox.Text = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://api.openai.com/v1" : _settings.BaseUrl;
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            ModelBox.Text = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4o-mini" : _settings.Model;
            KbPathBox.Text = _settings.KnowledgeBasePath ?? "";
            RobotsRootPathBox.Text = _settings.RobotsRootPath ?? "";
            RobotTemplateNameBox.Text = _settings.RobotTemplateName ?? "";
            RulesPathBox.Text = _settings.RulesPath ?? "";
            ContextWindowBox.Text = (_settings.ContextWindowSize > 0 ? _settings.ContextWindowSize : 409600).ToString();
            CompactThresholdBox.Text = _settings.CompactThreshold.ToString(CultureInfo.InvariantCulture);
            ReserveFractionBox.Text = _settings.ReserveFraction.ToString(CultureInfo.InvariantCulture);
            KeepRecentBox.Text = _settings.KeepRecentMessages.ToString();
            ToolResultMaxCharsBox.Text = _settings.ToolResultMaxChars.ToString();
            MaxIterationsBox.Text = _settings.MaxIterations.ToString();
            AutoCompactCheckBox.IsChecked = _settings.AutoCompact;
            UseToolRoleCheckBox.IsChecked = _settings.UseToolRole;
            LogFilePathBox.Text = _settings.LogFilePath ?? "";
            TimeoutSecondsBox.Text = _settings.TimeoutSeconds.ToString();
            RetryTimeoutMinutesBox.Text = _settings.RetryTimeoutMinutes.ToString();
            RetryBaseDelaySecondsBox.Text = _settings.RetryBaseDelaySeconds.ToString();
            RetryMaxDelaySecondsBox.Text = _settings.RetryMaxDelaySeconds.ToString();
        }

        private void RecreateAiProvider()
        {
            UnhookTokenUsage();
            UnhookMessageNotifier();
            UnhookRetryNotifier();
            _ai = AiProviderFactory.Create(_settings);
            LogService.Info("AI provider recreated: " + (_ai == null ? "null" : _ai.GetType().Name));
            HookTokenUsage();
            HookMessageNotifier();
            HookRetryNotifier();
            UpdateTokenUsageDisplay();
        }

        private string KbPath()
        {
            return (KbPathBox?.Text ?? "").Trim();
        }

        private string? RequireKbPath()
        {
            var p = KbPath();
            if (string.IsNullOrWhiteSpace(p))
            {
                AppendChat("Specify the knowledge base directory in the \"Settings\" tab first.");
                SetOutput("Knowledge base directory is not configured. Set the path in the \"Settings\" tab.");
                MainTabs.SelectedItem = SettingsTab;
                return null;
            }
            return p;
        }

        private string? RequireKbPathForKnowledgeBase()
        {
            var p = KbPath();
            if (string.IsNullOrWhiteSpace(p))
            {
                SetKbStatus("Knowledge base directory is not configured. Set the path in the \"Settings\" tab.");
                return null;
            }
            return p;
        }

        private void AppendChat(string text)
        {
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });
        }

        private void AppendExtensionError(string text)
        {
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Error, Text = text });
        }

        private void AppendToolCall(string toolName, string result, bool blocked)
        {
            AppendChatMessage(new ChatMessage
            {
                Kind = blocked ? ChatMessageKind.ToolBlocked : ChatMessageKind.ToolCall,
                Payload = new ToolCallPayload { ToolName = toolName ?? "", Result = result ?? "", Blocked = blocked }
            });
        }

        private void AppendCompaction(string text)
        {
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Compaction, Text = text });
        }

        private void AppendModeSwitch(string text)
        {
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Text = text });
        }

        private void AppendExtensionError(Exception ex)
        {
            if (ex == null) return;
            LogService.Error(ex);
            AppendExtensionError(ex.Message);
            AppendExtensionError(ex.ToString());
        }

        private void AppendChatMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendChatMessage(message)));
                return;
            }
            _messages.Add(message);
            ChatScroll.ScrollToEnd();
            if (!_loadingHistory)
                ScheduleHistorySave();
        }

        private void AppendSpecMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendSpecMessage(message)));
                return;
            }
            _specMessages.Add(message);
            SpecScroll.ScrollToEnd();
        }

        private void AppendSpecText(string text)
        {
            AppendSpecMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });
        }

        private ChatMessage AppendSpecStreaming()
        {
            var message = new ChatMessage { Kind = ChatMessageKind.SystemNotice, IsThinking = true, Text = "Thinking\u2026" };
            AppendSpecMessage(message);
            return message;
        }

        private void OnCopyMessage(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ChatMessage message })
            {
                try { Clipboard.SetText(FormatMessageForCopy(message)); }
                catch { /* clipboard busy — ignore */ }
            }
        }

        /// <summary>
        /// Forwards the mouse wheel from the code block's horizontal-only ScrollViewer to its
        /// parent, so hovering over a code block still scrolls the chat/spec list vertically.
        /// </summary>
        private void OnCodeScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
                return;

            e.Handled = true;

            var parent = (sender as FrameworkElement)?.Parent as UIElement;
            if (parent == null)
                return;

            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = parent
            });
        }

        private void OnCopyConversation(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var m in _messages)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(FormatMessageForCopy(m));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnCopySpecOutput(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var m in _specMessages)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(FormatMessageForCopy(m));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard busy — ignore */ }
        }

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

        private void SetOutput(string text)
        {
            AppendSpecText(text);
        }

        private void SetKbStatus(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbStatus(status)));
                return;
            }
            KbStatusText.Text = status;
        }

        private void SetKbProgress(double value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbProgress(value)));
                return;
            }
            KbProgressBar.Value = value;
            KbProgressBar.Visibility = Visibility.Visible;
        }

        private void HookTokenUsage()
        {
            _tokenUsage = _ai as ITokenUsageProvider;
            if (_tokenUsage != null)
                _tokenUsage.TokenUsageUpdated += OnTokenUsageUpdated;
        }

        private void UnhookTokenUsage()
        {
            if (_tokenUsage != null)
                _tokenUsage.TokenUsageUpdated -= OnTokenUsageUpdated;
            _tokenUsage = null;
        }

        private void HookMessageNotifier()
        {
            _messageNotifier = _ai as IMessageNotifier;
            if (_messageNotifier != null)
                _messageNotifier.MessageSent += OnAiMessageSent;
        }

        private void UnhookMessageNotifier()
        {
            if (_messageNotifier != null)
                _messageNotifier.MessageSent -= OnAiMessageSent;
            _messageNotifier = null;
        }

        private void HookRetryNotifier()
        {
            _retryNotifier = _ai as IRetryNotifier;
            if (_retryNotifier != null)
                _retryNotifier.RetryAttempt += OnRetryAttempt;
        }

        private void UnhookRetryNotifier()
        {
            if (_retryNotifier != null)
                _retryNotifier.RetryAttempt -= OnRetryAttempt;
            _retryNotifier = null;
        }

        private void OnAiMessageSent(object? sender, AiMessageEventArgs e)
        {
            LogService.Debug("AI message sent: role=" + (e.Role ?? "") + ", chars=" + (e.Content == null ? 0 : e.Content.Length));
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAiMessageSent(sender, e)));
                return;
            }
            AppendKbMessage("→ [" + (e.Role ?? "") + "] " + (e.Content ?? ""));
        }

        private void OnRetryAttempt(object? sender, RetryAttemptEventArgs e)
        {
            LogService.Warn(
                "Provider retry #" + e.Attempt +
                " status=" + (e.StatusCode.HasValue ? e.StatusCode.Value.ToString() : "no-response") +
                " reason=" + (e.ReasonPhrase ?? "").Replace("\r", " ").Replace("\n", " ") +
                " willRetry=" + e.WillRetry +
                " waitBeforeRetry=" + e.WaitBeforeRetry +
                " elapsed=" + e.Elapsed +
                " remaining=" + e.Remaining +
                " body=" + (e.Body ?? "").Replace("\r", " ").Replace("\n", " "));
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnRetryAttempt(sender, e)));
                return;
            }

            var status = e.StatusCode.HasValue
                ? "HTTP " + e.StatusCode.Value + (string.IsNullOrEmpty(e.ReasonPhrase) ? "" : " (" + e.ReasonPhrase + ")")
                : "no response";

            string message;
            if (e.WillRetry)
            {
                message = "Retry #" + e.Attempt + " → " + status;
                if (!string.IsNullOrWhiteSpace(e.Body))
                    message += ": " + e.Body;
                var total = e.Elapsed + e.Remaining;
                message += " (elapsed " + FormatKbDuration(e.Elapsed) + " / " + FormatKbDuration(total) +
                           ", next attempt in " + FormatKbDuration(e.WaitBeforeRetry) + ")";
            }
            else
            {
                message = "Retry budget exhausted after " + e.Attempt + " attempt(s): " + status;
                if (!string.IsNullOrWhiteSpace(e.Body))
                    message += ": " + e.Body;
            }

            AppendKbMessage(message);
            SetKbStatus(message);
        }

        private static string FormatKbDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds + "s";
            if (t.TotalSeconds >= 1) return (int)t.TotalSeconds + "s";
            return (int)t.TotalMilliseconds + "ms";
        }

        private void AppendKbMessage(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendKbMessage(text)));
                return;
            }
            KbMessagesBox.AppendText(text + Environment.NewLine);
            if (KbAutoScrollCheckBox.IsChecked == true)
                KbMessagesBox.ScrollToEnd();
        }

        private void OnRobotImportMessage(object? sender, string message)
        {
            AppendKbMessage(message);
        }

        private void OnTokenUsageUpdated(object? sender, TokenUsage usage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnTokenUsageUpdated(sender, usage)));
                return;
            }
            UpdateTokenUsageDisplay();
        }

        private void UpdateTokenUsageDisplay()
        {
            var provider = _tokenUsage;
            if (provider == null)
            {
                TokenUsageText.Text = "Context window: —";
                ContextFillBar.Value = 0;
                return;
            }
            var usage = provider.TotalTokenUsage;
            TokenUsageText.Text = "Context window: " + usage.TotalTokens.ToString("N0") +
                                  " / " + provider.ContextWindowSize.ToString("N0") + " tokens";

            var ratio = provider.ContextWindowSize > 0
                ? Math.Min(100.0, 100.0 * usage.TotalTokens / provider.ContextWindowSize)
                : 0.0;
            ContextFillBar.Value = ratio;
        }

        private void ShowError(Exception ex)
        {
            AppendExtensionError(ex);
            SetOutput(ex.ToString());
        }

        private void ShowNotice(string message)
        {
            NoticeText.Text = message;
            NoticeBar.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        private void HookSolutionEvents()
        {
            try
            {
                _dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (_dte == null) return;
                var solEvents = _dte.Events.SolutionEvents;
                solEvents.Opened += () => UpdateCreateRobotVisibility();
                solEvents.AfterClosing += () => UpdateCreateRobotVisibility();
                solEvents.ProjectAdded += p => UpdateCreateRobotVisibility();
                solEvents.ProjectRemoved += p => UpdateCreateRobotVisibility();
            }
            catch
            {
                // Solution events unavailable — visibility will be refreshed on load / lazily.
            }
        }

        private void UpdateCreateRobotVisibility()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateCreateRobotVisibility));
                return;
            }
            bool visible;
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                visible = dte?.Solution == null || !dte.Solution.IsOpen;
            }
            catch
            {
                visible = true;
            }
            CreateRobotPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            RestorePackagesPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowImportProgress(string status)
        {
            ImportStatusText.Text = status;
            ImportProgressPanel.Visibility = Visibility.Visible;
        }

        private void SetImportStatus(string status)
        {
            ImportStatusText.Text = status;
        }

        private void HideImportProgress()
        {
            ImportProgressPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Returns the open solution's directory, or null when no solution is open.</summary>
        private string? GetOpenSolutionDirectory()
        {
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;
                var slnPath = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(slnPath) ? null : Path.GetDirectoryName(slnPath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Copies the spec file, unchanged, into the docs folder at the solution root.</summary>
        private static void SaveImportedSpec(string sourcePath, string solutionDir)
        {
            var docsDir = Path.Combine(solutionDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.Copy(sourcePath, Path.Combine(docsDir, Path.GetFileName(sourcePath)), overwrite: true);
        }

#pragma warning disable VSTHRD100 // WPF event handlers are async void
        private async void OnSend(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Enter — send; Ctrl+Enter — newline (handled by the TextBox itself)
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async void OnImportSpec(object sender, RoutedEventArgs e)
        {
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Spec import is only available when a solution is open.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Select spec",
                Filter = "Specs (*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md)|*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            if (FileLockDetector.IsFileLocked(dlg.FileName))
            {
                ShowNotice(SpecLockedMessage);
                return;
            }

            ShowImportProgress("Reading file…");
            try
            {
                var docsDir = Path.Combine(solutionDir, "docs");
                Directory.CreateDirectory(docsDir);

                // Unchanged copy of the original spec file.
                SaveImportedSpec(dlg.FileName, solutionDir);

                var spec = new OpenXmlDocumentReader().Read(dlg.FileName);

                // 1. Structured spec.md (headings, lists, tables).
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "spec.md"), spec.MarkdownText, "spec.md");

                // 2. Decompose the spec into tasks.
                SetImportStatus("Parsing tasks (AI)…");
                var parsing = AppendSpecStreaming();
                parsing.Reasoning = "Decomposing the specification into tasks\u2026";
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText);
                spec.Tasks = tasks.ToList();
                parsing.IsThinking = false;
                parsing.Text = "Parsed " + tasks.Count + " tasks.";

                // 3. Structured tasks.md.
                var tasksMarkdown = MarkdownContent.TasksToMarkdown(spec);
                WriteMarkdownWithConfirm(Path.Combine(docsDir, "tasks.md"), tasksMarkdown, "tasks.md");

                _currentSpec = spec;
                HideNotice();

                var sb = new StringBuilder();
                sb.AppendLine("Spec: " + spec.Name);
                sb.AppendLine();
                sb.AppendLine(spec.RawText);
                sb.AppendLine();
                sb.AppendLine("Tasks: " + spec.Tasks.Count);
                foreach (var t in spec.Tasks)
                    sb.AppendLine("- " + (t.Action ?? "") + " → " + (t.Target ?? "") + " [" + (t.ActivityType ?? "") + "]");
                SetOutput(sb.ToString());

                AppendSpecText("Imported spec: " + spec.Name + " (tasks: " + spec.Tasks.Count + ")");
            }
            catch (Exception ex) when (FileLockDetector.IsFileLocked(ex))
            {
                ShowNotice(SpecLockedMessage);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                HideImportProgress();
            }
        }

        /// <summary>
        /// Saves an md file. If it already exists — offers full overwrite,
        /// partial fix via diff, or leaving it unchanged.
        /// </summary>
        private void WriteMarkdownWithConfirm(string path, string content, string displayName)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, content);
                AppendSpecText("Created " + displayName + ".");
                return;
            }

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
                    File.WriteAllText(path, content);
                    AppendSpecText(displayName + " overwritten.");
                    break;

                case MessageBoxResult.No:
                    var existing = File.ReadAllText(path);
                    var diff = TextDiff.Diff(existing, content);
                    var summary = diff.HasChanges ? diff.Summary : "No discrepancies found.";
                    SetOutput(summary);
                    if (diff.HasChanges)
                    {
                        var apply = MessageBox.Show(
                            summary + "\n\nApply the fixes (overwrite)?",
                            "Partial fix — " + displayName,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (apply == MessageBoxResult.Yes)
                        {
                            File.WriteAllText(path, content);
                            AppendSpecText("Fixes applied to " + displayName + ".");
                        }
                        else
                        {
                            AppendSpecText(displayName + " left unchanged.");
                        }
                    }
                    else
                    {
                        AppendSpecText(displayName + " has not changed.");
                    }
                    break;

                default: // Cancel
                    AppendSpecText(displayName + " left unchanged.");
                    break;
            }
        }

        private async void OnFindSimilar(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Import a spec first."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var matches = new List<SimilarTaskMatch>();
                foreach (var task in _currentSpec.Tasks)
                    matches.AddRange(await kb.FindSimilarTasksAsync(task, 3));

                var sb = new StringBuilder();
                foreach (var m in matches)
                    sb.AppendLine("[" + m.Score.ToString("P0") + "] " + m.Solution.Name + " — " + string.Join("; ", m.Reasons));
                SetOutput(sb.Length == 0 ? "No similar tasks found." : sb.ToString());
                AppendSpecText("Similar tasks found: " + matches.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnGenerateRobot(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Import a spec first."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var pipeline = new RpaPipelineService(new OpenXmlDocumentReader(), _ai, kb, new RobotGenerator());
                var result = await pipeline.GenerateRobotFromSpecAsync(_currentSpec.SourcePath);

                var dir = Path.GetDirectoryName(_currentSpec.SourcePath);
                var baseName = Path.GetFileNameWithoutExtension(_currentSpec.SourcePath);
                var xamlPath = Path.Combine(dir, baseName + ".xaml");
                var csPath = Path.Combine(dir, baseName + ".Actions.cs");
                File.WriteAllText(xamlPath, result.Xaml);
                File.WriteAllText(csPath, result.CSharpCode);

                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendSpecText("Robot generated and saved next to the spec: " + xamlPath + ", " + csPath);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnFormKnowledgeBase(object sender, RoutedEventArgs e)
        {
            await RunKnowledgeBaseImport(GetKnowledgeBaseFormationMode());
        }

        private KnowledgeBaseFormationMode GetKnowledgeBaseFormationMode()
        {
            return KbModeCombo.SelectedIndex == 1
                ? KnowledgeBaseFormationMode.Continue
                : KnowledgeBaseFormationMode.Override;
        }

        private async Task RunKnowledgeBaseImport(KnowledgeBaseFormationMode mode)
        {
            var kbPath = RequireKbPathForKnowledgeBase();
            if (kbPath == null) return;

            var robotsRoot = (RobotsRootPathBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(robotsRoot))
            {
                SetKbStatus("Specify the robot solutions directory in the \"Settings\" tab first.");
                return;
            }
            if (!Directory.Exists(robotsRoot))
            {
                SetKbStatus("Robot solutions directory does not exist: " + robotsRoot);
                return;
            }

            _kbImportPaused = false;
            _kbImportCts = new CancellationTokenSource();
            SetKbImportRunning(true);
            SetKbStatus(mode == KnowledgeBaseFormationMode.Continue ? "Resuming import…" : "Importing robots…");
            KbProgressBar.Value = 0;
            KbProgressBar.Visibility = Visibility.Visible;
            var service = new RobotKnowledgeBaseImportService(_ai);
            service.MessageReported += OnRobotImportMessage;
            try
            {
                var progress = new Progress<string>(SetKbStatus);
                var progressValue = new Progress<double>(SetKbProgress);
                var result = await service.ImportFromRobotsAsync(
                    robotsRoot, kbPath, progress, progressValue, _kbImportCts.Token, () => _kbImportPaused, mode);

                var sb = new StringBuilder();
                sb.AppendLine("Robots processed: " + result.RobotsProcessed);
                sb.AppendLine("Spec files written: " + result.SpecFilesWritten);
                sb.AppendLine("Task files written: " + result.TaskFilesWritten);
                sb.AppendLine("Tasks added to index: " + result.TasksAdded);
                foreach (var warning in result.Warnings)
                    sb.AppendLine("Warning: " + warning);
                AppendKbMessage(sb.ToString().TrimEnd());
                AppendChat("Knowledge base imported from robots: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).");
                SetKbStatus("Import finished: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).");
            }
            catch (OperationCanceledException)
            {
                AppendKbMessage("Import stopped by user.");
                SetKbStatus("Import stopped.");
            }
            catch (Exception ex)
            {
                ShowError(ex);
                SetKbStatus("Import failed.\n\n" + ex.ToString() + "\n\nSelect \"Continue (resume)\" and run again to resume from the last completed robot.");
            }
            finally
            {
                service.MessageReported -= OnRobotImportMessage;
                KbProgressBar.Visibility = Visibility.Collapsed;
                SetKbImportRunning(false);
                _kbImportCts.Dispose();
                _kbImportCts = null;
            }
        }

        private void SetKbImportRunning(bool running)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetKbImportRunning(running)));
                return;
            }
            KbImportButton.IsEnabled = !running;
            KbModeCombo.IsEnabled = !running;
            KbPauseButton.IsEnabled = running;
            KbStopButton.IsEnabled = running;
            if (!running)
            {
                KbPauseButton.Content = "Pause";
                _kbImportPaused = false;
            }
        }

        private void OnPauseKnowledgeBaseImport(object sender, RoutedEventArgs e)
        {
            if (_kbImportCts == null) return;
            _kbImportPaused = !_kbImportPaused;
            if (_kbImportPaused)
            {
                KbPauseButton.Content = "Resume";
                SetKbStatus("Paused — click Resume to continue.");
            }
            else
            {
                KbPauseButton.Content = "Pause";
                SetKbStatus("Importing robots…");
            }
        }

        private void OnStopKnowledgeBaseImport(object sender, RoutedEventArgs e)
        {
            _kbImportCts?.Cancel();
            KbStopButton.IsEnabled = false;
            SetKbStatus("Stopping…");
        }

        private async void OnBuildRelease(object sender, RoutedEventArgs e)
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                {
                    ShowNotice("No open solution. Open a solution and retry.");
                    return;
                }
                await _buildFeedback.BuildReleaseAsync();
                AppendSpecText("Release build started.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Log files",
                Filter = "Logs (*.log;*.txt;*.json)|*.log;*.txt;*.json|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = new List<string>();
                foreach (var f in dlg.FileNames) lines.AddRange(File.ReadAllLines(f));
                var analysis = new LogAnalyzer().Analyze(lines);
                SetOutput(analysis.Summary);
                foreach (var err in analysis.Errors.Take(20)) AppendSpecText("ERR: " + err.Message);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnConvertUiPath(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "UiPath robot",
                Filter = "UiPath workflow (*.xaml)|*.xaml|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var xaml = File.ReadAllText(dlg.FileName);
                var result = new UiPathToWfConverter(new RobotGenerator()).Convert(xaml);
                var dir = Path.GetDirectoryName(dlg.FileName);
                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.xaml"), result.Xaml);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.cs"), result.CSharpCode);
                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendSpecText("Conversion completed. Warnings: " + result.Warnings.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }
        private async void OnRestorePackages(object sender, RoutedEventArgs e)
        {
            try
            {
                _outputWindow.Activate();
                await _robotTemplate.RunPackageManagerCommandAsync(
                    "Update-Package -Reinstall",
                    _outputWindow.CreateProgress());
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnCreateRobot(object sender, RoutedEventArgs e)
        {
            var number = (RobotNumberBox?.Text ?? "").Trim();
            if (number.Length == 0 || !number.All(char.IsDigit))
            {
                AppendChat("Specify the robot number (digits only).");
                return;
            }

            var solutionName = "RPA-" + number;
            try
            {
                _outputWindow.Activate();
                var result = await _robotTemplate.CreateRobotAsync(
                    solutionName, _settings.RobotsRootPath, _settings.RobotTemplateName,
                    progress: _outputWindow.CreateProgress());
                if (result.Success)
                    _outputWindow.WriteLine("Robot created: " + result.SolutionPath);
                else
                    _outputWindow.WriteLine("Warning: " + result.Message);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void OnRobotNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(c => !char.IsDigit(c));
        }

        private async void OnRefreshKnowledgeBase(object sender, RoutedEventArgs e)
        {
            var kbPath = RequireKbPathForKnowledgeBase();
            if (kbPath == null) return;
            try
            {
                var kb = new FileKnowledgeBase(kbPath);
                var records = await kb.GetAllAsync();
                var items = records.Select(r => (r.Name ?? "<unnamed>") + " — tasks: " + (r.Tasks?.Count ?? 0)).ToList();
                KbListBox.ItemsSource = items;
                KbStatusText.Text = "Solutions in the knowledge base: " + records.Count;
            }
            catch (Exception ex) { ShowError(ex); }
        }

#pragma warning restore VSTHRD100

        private void StartChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StartChatStatus));
                return;
            }
            SetPhase(AgentPhase.Streaming);
            AgentStatusText.Visibility = Visibility.Visible;
            _chatStartedAt = DateTime.UtcNow;
            _chatStatusTimer.Tick -= OnChatStatusTick;
            _chatStatusTimer.Tick += OnChatStatusTick;
            _chatStatusTimer.Start();
            OnChatStatusTick(null, EventArgs.Empty);
        }

        private void OnChatStatusTick(object? sender, EventArgs e)
        {
            var elapsed = (int)(DateTime.UtcNow - _chatStartedAt).TotalSeconds;
            var status = elapsed < 2 ? "Sending request…" : "Thinking…";
            AgentStatusText.Text = status + " (" + elapsed + " s)";
        }

        private void StopChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StopChatStatus));
                return;
            }
            _chatStatusTimer.Stop();
            _chatStatusTimer.Tick -= OnChatStatusTick;
            AgentStatusText.Text = "";
            AgentStatusText.Visibility = Visibility.Collapsed;
            SetPhase(AgentPhase.Idle);
        }

        /// <summary>Switches the footer to the given phase and updates the dependent controls.</summary>
        private void SetPhase(AgentPhase phase)
        {
            if (_phase == phase)
                return;
            _phase = phase;
            SendButton.IsEnabled = phase != AgentPhase.Streaming;
            CancelButton.Visibility = phase == AgentPhase.Streaming ? Visibility.Visible : Visibility.Collapsed;
            AgentProgress.Visibility = phase == AgentPhase.Streaming ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowChatStatusError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowChatStatusError(message)));
                return;
            }
            _chatStatusTimer.Stop();
            _chatStatusTimer.Tick -= OnChatStatusTick;
            AgentStatusText.Text = "Error: " + message;
            AgentStatusText.Visibility = Visibility.Visible;
            SetPhase(AgentPhase.Error);
        }

        private void OnCancelSend(object sender, RoutedEventArgs e)
        {
            _chatCts?.Cancel();
        }

        private void OnCommit(object sender, RoutedEventArgs e)
        {
            // TODO: not implemented yet.
        }

        private async Task SendMessageAsync()
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0) return;
            InputBox.Clear();
            await SendChatTextAsync(text);
        }

        private async void OnSendErrors(object sender, RoutedEventArgs e)
        {
            try
            {
                var errors = await _buildFeedback.GetCompilationErrorsAsync();
                var text = await BuildCompilationErrorsContextAsync(errors);
                if (string.IsNullOrWhiteSpace(text))
                {
                    AppendChat(await FormatNoCompilationErrorsAsync());
                    return;
                }

                await SendChatTextAsync(text);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        /// <summary>
        /// Builds the complete compiler-error context the agent receives: the error text plus,
        /// for each error, a bounded window around the error line read from the open editor tab
        /// (never from disk). The whole context is assembled deterministically by
        /// <see cref="CompilationErrorContextBuilder"/> and returned as one string, so nothing
        /// reaches the chat until the full context has been built.
        /// </summary>
        private async Task<string> BuildCompilationErrorsContextAsync(IReadOnlyList<BuildError> errors)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);

            return await CompilationErrorContextBuilder.BuildAsync(
                errors,
                async (file, line, ctx) =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (dte == null) throw new InvalidOperationException("Visual Studio is not available.");
                    var doc = await VsDocumentService.OpenOrActivateAsync(dte, file).ConfigureAwait(true);
                    if (doc == null) throw new InvalidOperationException("could not open in the editor");
                    return VsDocumentService.ReadDocumentWindow(doc, line, ctx);
                },
                MaxErrorsWithContext,
                ErrorContextLines).ConfigureAwait(true);
        }

        private async Task<string> FormatNoCompilationErrorsAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("The Visual Studio Error List is empty.");

            string output;
            try
            {
                output = await _buildFeedback.GetOutputWindowTextAsync();
            }
            catch (Exception ex)
            {
                output = "Output window unavailable: " + ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                sb.AppendLine();
                sb.AppendLine("Build output:");
                sb.AppendLine(output);
            }

            return sb.ToString().TrimEnd();
        }

        private async Task SendChatTextAsync(string text)
        {
            if (_chatCts != null) return; // already awaiting the agent's reply
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_agentRunner == null) return;

            var modeName = _mode.ToString();
            var history = BuildTranscript();
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = text });
            if (string.IsNullOrEmpty(_currentConversation.Title))
                _currentConversation.Title = BuildChatTitle(text);

            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var cts = new CancellationTokenSource();
            _chatCts = cts;
            StartChatStatus();
            LogService.Info("Agent chat request started (mode=" + modeName + ", input chars=" + text.Length + ").");
            try
            {
                var progress = new Progress<ChatStreamDelta>(delta =>
                {
                    if (!string.IsNullOrEmpty(delta.ReasoningDelta))
                        thinking.Reasoning += delta.ReasoningDelta;
                });
                var result = await _agentRunner.RunAsync(history, text, _mode, BuildContextBudget(), cts.Token, progress);
                thinking.IsThinking = false;
                thinking.IsPartial = false;
                thinking.Text = result.FinalText;
                StopChatStatus();
                LogService.Info("Agent chat completed (mode=" + modeName + ", answer chars=" + (result.FinalText == null ? 0 : result.FinalText.Length) + ").");
            }
            catch (OperationCanceledException)
            {
                thinking.IsThinking = false;
                if (_messages.Contains(thinking))
                    AppendChat("Request cancelled.");
                StopChatStatus();
                LogService.Info("Agent chat request cancelled.");
            }
            catch (Exception ex)
            {
                thinking.IsThinking = false;
                AppendExtensionError(ex);
                ShowChatStatusError(ex.Message);
                LogService.Error(ex, "Agent chat request failed.");
            }
            finally
            {
                _chatCts = null;
                cts.Dispose();
            }
        }

        /// <summary>
        /// Appends a system notice to the chat without invoking the agent.
        /// Safe to call from any thread.
        /// </summary>
        public void AppendSystemMessage(string text)
        {
            AppendChat(text);
        }

        /// <summary>
        /// Sends an externally produced message (e.g. an Error List item selected by the user)
        /// to the agent chat. When the agent is still processing a previous request, the message
        /// is placed into the input box instead of being silently dropped.
        /// </summary>
        public async Task SubmitErrorToChatAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_chatCts != null)
            {
                InputBox.Text = text;
                AppendChat("The agent is busy — the message was placed into the input box.");
                return;
            }

            await SendChatTextAsync(text);
        }

        /// <summary>Builds the model transcript from visible chat messages, mapping UI roles to model roles.</summary>
        private List<ChatMessageData> BuildTranscript()
        {
            var transcript = new List<ChatMessageData>();
            foreach (var message in _messages)
            {
                if (string.IsNullOrWhiteSpace(message.Text))
                    continue;

                switch (message.Kind)
                {
                    case ChatMessageKind.User:
                        transcript.Add(new ChatMessageData { Role = "user", Content = message.Text });
                        break;
                    case ChatMessageKind.AgentText:
                        transcript.Add(new ChatMessageData { Role = "assistant", Content = message.Text });
                        break;
                    // Other kinds (tool calls, notices, errors) are UI-only and not part of the model conversation.
                }
            }
            return transcript;
        }

        /// <summary>Builds the context budget from persisted settings, falling back to defaults.</summary>
        private ContextBudget BuildContextBudget()
        {
            return new ContextBudget
            {
                ContextWindowSize = _settings.ContextWindowSize > 0 ? _settings.ContextWindowSize : 409600,
                CompactThresholdFraction = _settings.CompactThreshold,
                ReserveFraction = _settings.ReserveFraction,
                KeepRecentMessages = _settings.KeepRecentMessages,
                ToolResultMaxChars = _settings.ToolResultMaxChars,
                UseToolRole = _settings.UseToolRole,
                MaxIterations = _settings.MaxIterations,
                AutoCompact = _settings.AutoCompact
            };
        }

        private void RebuildAgentTools()
        {
            var registry = BuildToolRegistry();
            _toolRegistry = registry;
            if (_agentRunner != null)
            {
                _agentRunner.ToolExecuted -= OnAgentToolExecuted;
                _agentRunner.ContextCompacted -= OnAgentContextCompacted;
                _agentRunner.RetryOccurred -= OnAgentRetryOccurred;
            }
            _agentRunner = new AgentRunner(_ai, registry, mode => AgentPromptBuilder.Build(mode, registry));
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
        }

        private void OnAgentRetryOccurred(object? sender, AgentRetryArgs e)
        {
            LogService.Warn("Agent retry: " + (e.Kind ?? "") + " attempt " + e.Attempt + "/" + e.AttemptLimit + " - " + (e.Detail ?? ""));
        }

        private void OnAgentContextCompacted(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentContextCompacted(sender, e)));
                return;
            }
            LogService.Info("Agent context compacted (summarized) to fit the context window.");
            AppendCompaction("Context compacted (summarized) to fit the context window.");
        }

        private async void OnCompactNow(object sender, RoutedEventArgs e)
        {
            if (_agentRunner == null || _chatCts != null)
                return;

            var transcript = BuildTranscript();
            if (transcript.Count == 0)
            {
                AppendChat("Nothing to compact yet.");
                return;
            }

            try
            {
                var condensed = await _agentRunner.CompactAsync(transcript, BuildContextBudget(), CancellationToken.None);

                _messages.Clear();
                foreach (var m in condensed)
                {
                    var kind = m.Role == "assistant" ? ChatMessageKind.AgentText
                             : m.Role == "user" ? ChatMessageKind.User
                             : ChatMessageKind.SystemNotice;
                    _messages.Add(new ChatMessage { Kind = kind, Text = m.Content });
                }
                AppendCompaction("Context compacted (summarized).");
                ScheduleHistorySave();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Compact failed: " + ex.Message);
            }
        }

        private void OnUpdateMemory(object sender, RoutedEventArgs e)
        {
            InputBox.Text = "update memory bank: read the current memory bank with read_memory, then update the relevant files with update_memory.";
            InputBox.Focus();
        }

        private AgentToolRegistry BuildToolRegistry()
        {
            var factory = new AgentToolFactory(
                _ai,
                () => _currentSpec,
                KbPath,
                () => (RobotsRootPathBox?.Text ?? "").Trim(),
                () => (RobotTemplateNameBox?.Text ?? "").Trim(),
                GetOpenSolutionDirectory,
                () => _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2),
                _buildFeedback,
                _robotTemplate);
            return factory.CreateRegistry();
        }

        private void OnAgentToolExecuted(object? sender, AgentToolCall e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentToolExecuted(sender, e)));
                return;
            }
            var detail = string.IsNullOrWhiteSpace(e.DisplayResult) ? e.Result : e.DisplayResult;
            LogService.Info("Agent tool executed: " + (e.ToolName ?? "") + (e.Blocked ? " (blocked)" : "") + ", result chars=" + (detail == null ? 0 : detail.Length));
            AppendToolCall(e.ToolName, detail, e.Blocked);
        }

        // ------------------------------------------------------------------
        // Chat history (SQLite-backed persistence)
        // ------------------------------------------------------------------

        private void InitializeChatHistory()
        {
            _historySaveTimer.Tick += OnHistorySaveTick;
            this.Unloaded += OnWindowUnloaded;
            HistoryCombo.ItemsSource = _conversations;

            try
            {
                ChatHistoryStore.Initialize();
                RefreshHistoryList();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Chat history unavailable: " + ex.Message);
            }

            var latest = _conversations.FirstOrDefault();
            if (latest != null)
                OpenConversation(latest.Id, markDirty: false);
            else
                StartNewChat(markDirty: false);
        }

        private void OnWindowUnloaded(object sender, RoutedEventArgs e)
        {
            SaveHistoryNow();
        }

        private void ScheduleHistorySave()
        {
            _historySaveTimer.Stop();
            _historySaveTimer.Start();
        }

        private void OnHistorySaveTick(object? sender, EventArgs e)
        {
            _historySaveTimer.Stop();
            SaveHistoryNow();
        }

        private void SaveHistoryNow()
        {
            if (_currentConversation == null || string.IsNullOrWhiteSpace(_currentConversation.Id))
                return;

            _currentConversation.Messages = _messages.Select(ToHistoryMessage).ToList();
            if (string.IsNullOrEmpty(_currentConversation.Title))
            {
                var firstUser = _currentConversation.Messages.FirstOrDefault(m => m.Kind == ChatMessageKind.User);
                if (firstUser != null)
                    _currentConversation.Title = BuildChatTitle(firstUser.Text);
            }
            _currentConversation.UpdatedAtUtc = DateTime.UtcNow;

            try
            {
                ChatHistoryStore.SaveConversation(_currentConversation);
                RefreshHistoryList();
            }
            catch
            {
                // History persistence is best-effort; never block or spam the chat on failure.
            }
        }

        private void StartNewChat(bool markDirty = true)
        {
            _chatCts?.Cancel();
            SaveHistoryNow();

            _loadingHistory = true;
            try
            {
                _messages.Clear();
                _currentConversation = new ChatConversation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                AppendChat("Agent ready. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
            }
            finally
            {
                _loadingHistory = false;
            }

            UpdateHistorySelection();
            if (markDirty)
                ScheduleHistorySave();
        }
        private void OpenConversation(string id, bool markDirty = true)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            if (id == _currentConversation?.Id)
                return;

            _chatCts?.Cancel();
            SaveHistoryNow();

            ChatConversation? stored;
            try
            {
                stored = ChatHistoryStore.LoadConversation(id);
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to open chat: " + ex.Message);
                return;
            }
            if (stored == null)
                return;

            _loadingHistory = true;
            try
            {
                _messages.Clear();
                foreach (var m in stored.Messages)
                {
                    _messages.Add(FromHistoryMessage(m));
                }
                _currentConversation = stored;
                ChatScroll.ScrollToEnd();
            }
            finally
            {
                _loadingHistory = false;
            }

            UpdateHistorySelection();
            if (markDirty)
                ScheduleHistorySave();
        }

        private void DeleteCurrentConversation()
        {
            var id = _currentConversation?.Id;
            if (string.IsNullOrWhiteSpace(id))
                return;

            _chatCts?.Cancel();

            try
            {
                ChatHistoryStore.DeleteConversation(id);
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to delete chat: " + ex.Message);
                return;
            }

            _loadingHistory = true;
            try
            {
                _messages.Clear();
                _currentConversation = new ChatConversation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                AppendChat("Agent ready. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
            }
            finally
            {
                _loadingHistory = false;
            }

            RefreshHistoryList();
            UpdateHistorySelection();
        }

        private void RefreshHistoryList()
        {
            try
            {
                _conversations.Clear();
                foreach (var conversation in ChatHistoryStore.ListConversations())
                    _conversations.Add(conversation);
                UpdateHistorySelection();
            }
            catch
            {
                // best-effort refresh of the history dropdown
            }
        }

        private void UpdateHistorySelection()
        {
            if (HistoryCombo == null)
                return;

            var currentId = _currentConversation?.Id;
            _updatingHistoryCombo = true;
            try
            {
                HistoryCombo.SelectedItem = _conversations.FirstOrDefault(c => c.Id == currentId);
            }
            finally
            {
                _updatingHistoryCombo = false;
            }
        }

        private void OnNewChat(object sender, RoutedEventArgs e)
        {
            StartNewChat();
        }

        private void OnDeleteChat(object sender, RoutedEventArgs e)
        {
            DeleteCurrentConversation();
        }

        private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingHistoryCombo)
                return;
            if (HistoryCombo.SelectedItem is ChatConversation conversation && conversation.Id != _currentConversation?.Id)
                OpenConversation(conversation.Id);
        }

        private static ChatHistoryMessage ToHistoryMessage(ChatMessage message)
        {
            var tool = message.Payload as ToolCallPayload;
            return new ChatHistoryMessage
            {
                Kind = message.Kind,
                Text = tool != null ? tool.Result : message.Text,
                ToolName = tool?.ToolName ?? "",
                Reasoning = message.Reasoning,
                Mode = message.Mode
            };
        }

        private static ChatMessage FromHistoryMessage(ChatHistoryMessage m)
        {
            if (m.Kind == ChatMessageKind.ToolCall || m.Kind == ChatMessageKind.ToolBlocked)
            {
                return new ChatMessage
                {
                    Kind = m.Kind,
                    Payload = new ToolCallPayload { ToolName = m.ToolName ?? "", Result = m.Text ?? "", Blocked = m.Kind == ChatMessageKind.ToolBlocked }
                };
            }

            return new ChatMessage { Kind = m.Kind, Text = m.Text, Reasoning = m.Reasoning, Mode = m.Mode };
        }

        private static string BuildChatTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "New chat";
            var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            const int max = 60;
            return singleLine.Length <= max ? singleLine : singleLine.Substring(0, max) + "…";
        }

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            _mode = ActModeButton?.IsChecked == true ? AgentMode.Act : AgentMode.Plan;
            AppendModeSwitch("Mode: " + _mode);
        }

        private void OnRefreshPromptsFromDisk(object sender, RoutedEventArgs e)
        {
            if (_ai is IPromptReloadable reloadable)
            {
                reloadable.ReloadPromptsFromDisk();
                AppendChat("Prompts reloaded from disk: " + PromptPaths.ResolvePromptsDirectory());
            }
            else
            {
                AppendChat("Prompts reload is unavailable for the current provider (stub/offline).");
            }
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.KnowledgeBasePath = KbPathBox.Text.Trim();
            _settings.RobotsRootPath = RobotsRootPathBox.Text.Trim();
            _settings.RobotTemplateName = RobotTemplateNameBox.Text.Trim();
            _settings.RulesPath = RulesPathBox.Text.Trim();
            _settings.ContextWindowSize = ParseContextWindowSize();
            _settings.CompactThreshold = ParseFraction(CompactThresholdBox?.Text, 0.85, 0.0, 1.0);
            _settings.ReserveFraction = ParseFraction(ReserveFractionBox?.Text, 0.12, 0.0, 0.5);
            _settings.KeepRecentMessages = ParsePositiveInt(KeepRecentBox?.Text, 6);
            _settings.ToolResultMaxChars = ParsePositiveInt(ToolResultMaxCharsBox?.Text, 12000);
            _settings.MaxIterations = ParsePositiveInt(MaxIterationsBox?.Text, 8);
            _settings.AutoCompact = AutoCompactCheckBox?.IsChecked ?? true;
            _settings.UseToolRole = UseToolRoleCheckBox?.IsChecked ?? true;
            _settings.LogFilePath = LogFilePathBox.Text.Trim();
            _settings.TimeoutSeconds = ParsePositiveInt(TimeoutSecondsBox?.Text, 300);
            _settings.RetryTimeoutMinutes = ParsePositiveInt(RetryTimeoutMinutesBox?.Text, 30);
            _settings.RetryBaseDelaySeconds = ParsePositiveInt(RetryBaseDelaySecondsBox?.Text, 1);
            _settings.RetryMaxDelaySeconds = ParsePositiveInt(RetryMaxDelaySecondsBox?.Text, 30);
            SettingsStore.Save(_settings);
            LogService.Configure(_settings.LogFilePath);
            RecreateAiProvider();
            RebuildAgentTools();
            AppendChat("Settings saved. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
        }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            var logFile = LogService.CurrentLogFilePath;
            if (string.IsNullOrWhiteSpace(logFile))
            {
                AppendExtensionError("Log file path is not available.");
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(logFile))
                    File.WriteAllText(logFile, "");

                var editor = FindNotepadPlusPlus();
                if (!string.IsNullOrWhiteSpace(editor))
                    System.Diagnostics.Process.Start(new ProcessStartInfo(editor, "\"" + logFile + "\"") { UseShellExecute = true });
                else
                    System.Diagnostics.Process.Start(new ProcessStartInfo("notepad.exe", "\"" + logFile + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to open log: " + ex.Message);
                LogService.Error(ex, "Failed to open log file.");
            }
        }

        private static string FindNotepadPlusPlus()
        {
            var candidates = new List<string>
            {
                ReadAppPath("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\notepad++.exe"),
                ReadAppPath("HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\notepad++.exe")
            };

            var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrWhiteSpace(prog)) candidates.Add(Path.Combine(prog, "Notepad++", "notepad++.exe"));
            if (!string.IsNullOrWhiteSpace(progX86)) candidates.Add(Path.Combine(progX86, "Notepad++", "notepad++.exe"));
            if (!string.IsNullOrWhiteSpace(local)) candidates.Add(Path.Combine(local, "Notepad++", "notepad++.exe"));
            if (!string.IsNullOrWhiteSpace(roaming)) candidates.Add(Path.Combine(roaming, "Notepad++", "notepad++.exe"));

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private static string ReadAppPath(string registryKeyPath)
        {
            try
            {
                return Registry.GetValue(registryKeyPath, "", null) as string;
            }
            catch
            {
                return null;
            }
        }

        private int ParseContextWindowSize()
        {
            if (int.TryParse((ContextWindowBox?.Text ?? "").Trim(), out var value) && value > 0)
                return value;
            return 409600;
        }

        private static double ParseFraction(string text, double fallback, double min, double max)
        {
            if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
                return value;
            return fallback;
        }

        private static int ParsePositiveInt(string text, int fallback)
        {
            if (int.TryParse((text ?? "").Trim(), out var value) && value > 0)
                return value;
            return fallback;
        }
    }
}
