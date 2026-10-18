using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter2.Services;
using RPAWriter2.Services.AgentTools;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.PlatformUI;
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

        /// <summary>Whether the chat tab shows the active conversation or the history list.</summary>
        private enum ChatViewMode
        {
            Conversation,
            History
        }

        private SettingsData _settings;
        private IAiProvider _ai;
        private SpecModel _currentSpec;
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
        private OutputWindowService _outputWindow;
        private readonly GitService _git = new GitService();
        private AgentMode _mode = AgentMode.Plan;
        private AgentPhase _phase = AgentPhase.Idle;
        private AgentToolRegistry? _toolRegistry;
        private AgentRunner? _agentRunner;
        private ITokenUsageProvider? _tokenUsage;
        private int _lastEstimatedInputTokens;
        private IMessageNotifier? _messageNotifier;
        private IRetryNotifier? _retryNotifier;
        private IPromptNotifier? _promptNotifier;
        private DTE2? _dte;
        private IReadOnlyList<IProdLogProvider>? _logProviders;
        private IProdLogProvider? _selectedLogProvider;
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private readonly ObservableCollection<ChatMessage> _specMessages = new ObservableCollection<ChatMessage>();
        private readonly PackageCatalogService _packageCatalog = new PackageCatalogService();
        private readonly ObservableCollection<PackageListItem> _packages = new ObservableCollection<PackageListItem>();
        private CancellationTokenSource? _chatCts;
        private CancellationTokenSource? _kbImportCts;
        private volatile bool _kbImportPaused;
        private readonly DispatcherTimer _chatStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _chatStartedAt;
        private const string SpecLockedMessage = "The spec file is open in another application (e.g. Word/Excel). Close it and try again.";
        private const string CommitBranchName = "dev";
        private const string CommitMessageSystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the provided changes into a single-line commit message in the format \"type(scope): summary\". " +
            "Respond with only the commit message text — no markdown, no quotes, no extra explanation.";
        private const string CommitRobotSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the robot changes below into a single concise line in the imperative mood. " +
            "Respond with only the summary text — no RPA-...: prefix, no markdown, no quotes, no extra explanation.";
        private const string CommitOtherSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the changes below into a single concise line in the imperative mood. " +
            "Respond with only the summary text — no markdown, no quotes, no extra explanation.";

        private const int CommitDiffMaxChars = 8000;
        private const string AnalyzeOutputWindowPrompt =
            "Analyze the Visual Studio Output window content below. " +
            "Identify errors, warnings and their likely root causes, and suggest concrete fixes:\n\n";
        private const string AnalyzeProdLogsPrompt =
            "Analyze the production robot logs below. " +
            "Summarize the robot run, list errors and warnings with their likely root causes, and suggest fixes:\n\n";
        private const int ProdLogMaxChars = 40000;
        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;
        private readonly ObservableCollection<ChatConversation> _conversations = new ObservableCollection<ChatConversation>();
        private ChatConversation _currentConversation = new ChatConversation();
        private readonly DispatcherTimer _historySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        private bool _loadingHistory;
        private ChatViewMode _chatViewMode = ChatViewMode.Conversation;
        private ChatMessage? _pinnedMessage;
        private ICollectionView? _chatListView;
        private ICollectionView? _packagesView;
        private bool _pinnedExpanded;

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            _chatListView = CollectionViewSource.GetDefaultView(_messages);
            _chatListView.Filter = item => !ReferenceEquals(item, _pinnedMessage);
            ChatList.ItemsSource = _chatListView;
            SpecList.ItemsSource = _specMessages;
            HistoryList.ItemsSource = _conversations;
            _packagesView = CollectionViewSource.GetDefaultView(_packages);
            _packagesView.Filter = FilterPackages;
            PackagesList.ItemsSource = _packagesView;
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
            PackagesPathBox.Text = _settings.PackagesPath ?? "";
            RulesPathBox.Text = _settings.RulesPath ?? "";
            ContextWindowBox.Text = (_settings.ContextWindowSize > 0 ? _settings.ContextWindowSize : 409600).ToString();
            CompactThresholdBox.Text = _settings.CompactThreshold.ToString(CultureInfo.InvariantCulture);
            ReserveFractionBox.Text = _settings.ReserveFraction.ToString(CultureInfo.InvariantCulture);
            KeepRecentBox.Text = _settings.KeepRecentMessages.ToString();
            ToolResultMaxCharsBox.Text = _settings.ToolResultMaxChars.ToString();
            MaxIterationsBox.Text = _settings.MaxIterations.ToString();
            AutoCompactCheckBox.IsChecked = _settings.AutoCompact;
            UseToolRoleCheckBox.IsChecked = _settings.UseToolRole;
            ToolCallingModeCombo.SelectedIndex = string.Equals(_settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LogFilePathBox.Text = _settings.LogFilePath ?? "";
            LogProvidersPathBox.Text = _settings.LogProvidersPath ?? "";
            TimeoutSecondsBox.Text = _settings.TimeoutSeconds.ToString();
            RetryTimeoutMinutesBox.Text = _settings.RetryTimeoutMinutes.ToString();
            RetryBaseDelaySecondsBox.Text = _settings.RetryBaseDelaySeconds.ToString();
            RetryMaxDelaySecondsBox.Text = _settings.RetryMaxDelaySeconds.ToString();
            InputPriceBox.Text = _settings.InputPricePerMillion.ToString(CultureInfo.InvariantCulture);
            CacheHitPriceBox.Text = _settings.CacheHitPricePerMillion.ToString(CultureInfo.InvariantCulture);
            OutputPriceBox.Text = _settings.OutputPricePerMillion.ToString(CultureInfo.InvariantCulture);
        }

        private void RecreateAiProvider()
        {
            UnhookTokenUsage();
            UnhookMessageNotifier();
            UnhookRetryNotifier();
            UnhookPromptNotifier();
            _ai = AiProviderFactory.Create(_settings);
            LogService.Info("AI provider recreated: " + (_ai == null ? "null" : _ai.GetType().Name));
            HookTokenUsage();
            HookMessageNotifier();
            HookRetryNotifier();
            HookPromptNotifier();
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
            if (message.Kind == ChatMessageKind.User && _pinnedMessage == null)
                RefreshPinnedMessage();
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

        private void HookPromptNotifier()
        {
            _promptNotifier = _ai as IPromptNotifier;
            if (_promptNotifier != null)
                _promptNotifier.PromptAssembled += OnPromptAssembled;
        }

        private void UnhookPromptNotifier()
        {
            if (_promptNotifier != null)
                _promptNotifier.PromptAssembled -= OnPromptAssembled;
            _promptNotifier = null;
        }

        private void OnAiMessageSent(object? sender, AiMessageEventArgs e)
        {
            // AI message tracing is logged only (not written to the knowledge-base panel),
            // so commit auto-generation and chat traffic no longer pollute the KB "Messages" box.
            LogService.Debug("AI message sent: role=" + (e.Role ?? "") + ", chars=" + (e.Content == null ? 0 : e.Content.Length));
        }

        private void OnPromptAssembled(object? sender, PromptAssembledEventArgs e)
        {
            _lastEstimatedInputTokens = e.EstimatedInputTokens;
            if (!Dispatcher.CheckAccess())
                Dispatcher.BeginInvoke(new Action(UpdateTokenUsageDisplay));
            else
                UpdateTokenUsageDisplay();

            var dump = FormatPromptDump(e);
            LogService.Info(dump);
            try
            {
                if (_outputWindow != null)
                    _outputWindow.WriteLine(dump);
            }
            catch
            {
                // Logging to the output pane must never break an outgoing request.
            }
        }

        private static string FormatPromptDump(PromptAssembledEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== FULL PROMPT ASSEMBLED =====");
            sb.AppendLine("Estimated input tokens: " + e.EstimatedInputTokens);
            sb.AppendLine("System prompt (" + (e.SystemPrompt == null ? 0 : e.SystemPrompt.Length) + " chars):");
            sb.AppendLine("-----");
            sb.AppendLine(e.SystemPrompt ?? "");
            sb.AppendLine("-----");

            var messages = e.Messages ?? new List<ChatMessageData>();
            sb.AppendLine("Messages (" + messages.Count + "):");
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m == null)
                    continue;
                var role = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role;
                sb.AppendLine("[" + i + "] role=" + role + " (" + (m.Content == null ? 0 : m.Content.Length) + " chars):");
                sb.AppendLine(m.Content ?? "");
                sb.AppendLine("-----");
            }

            sb.AppendLine("===== END FULL PROMPT =====");
            return sb.ToString();
        }

        private void OnRetryAttempt(object? sender, RetryAttemptEventArgs e)
        {
            // Retry attempts are logged only (not written to the knowledge-base panel).
            LogService.Warn(
                "Provider retry #" + e.Attempt +
                " status=" + (e.StatusCode.HasValue ? e.StatusCode.Value.ToString() : "no-response") +
                " reason=" + (e.ReasonPhrase ?? "").Replace("\r", " ").Replace("\n", " ") +
                " willRetry=" + e.WillRetry +
                " waitBeforeRetry=" + e.WaitBeforeRetry +
                " elapsed=" + e.Elapsed +
                " remaining=" + e.Remaining +
                " body=" + (e.Body ?? "").Replace("\r", " ").Replace("\n", " "));
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
                ChatTokenUsageText.Text = "Context window: —";
                ChatContextFillBar.Value = 0;
                return;
            }
            var usage = provider.TotalTokenUsage;
            var occupied = _lastEstimatedInputTokens;
            var text = "Context window: " + occupied.ToString("N0") +
                       " / " + provider.ContextWindowSize.ToString("N0") + " tokens" +
                       "    total " + usage.TotalTokens.ToString("N0");
            if (usage.CacheHitTokens > 0)
                text += " · cache hit " + usage.CacheHitTokens.ToString("N0");
            if (usage.EstimatedCostUsd > 0)
                text += " · $" + usage.EstimatedCostUsd.ToString("0.0000", CultureInfo.InvariantCulture);
            TokenUsageText.Text = text;
            ChatTokenUsageText.Text = text;

            var ratio = provider.ContextWindowSize > 0
                ? Math.Min(100.0, 100.0 * occupied / provider.ContextWindowSize)
                : 0.0;
            ContextFillBar.Value = ratio;
            ChatContextFillBar.Value = ratio;
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
                var importSummary = "Knowledge base imported from robots: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).";
                AppendChat(importSummary);
                SetKbStatus(importSummary);
            }
            catch (OperationCanceledException)
            {
                AppendKbMessage("Import stopped by user.");
                SetKbStatus("Import stopped.");
            }
            catch (Exception ex)
            {
                ShowError(ex);
                AppendKbMessage("Import failed.\n\n" + ex.ToString() + "\n\nSelect \"Continue (resume)\" and run again to resume from the last completed robot.");
                SetKbStatus("Import failed. Select \"Continue (resume)\" and run again to resume from the last completed robot.");
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
                LogService.Info("Release build started.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            AnalyzeLogsPanel.Visibility = AnalyzeLogsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnCloseAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
            ProdLogsPanel.Visibility = Visibility.Collapsed;
        }

        private async void OnAnalyzeLogsFromOutputWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = await _buildFeedback.GetOutputWindowTextAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    AppendChat("The Visual Studio Output window is empty.");
                    return;
                }

                AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                await SendChatTextAsync(AnalyzeOutputWindowPrompt + text);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogsFromProdServers(object sender, RoutedEventArgs e)
        {
            try
            {
                ProdLogsPanel.Visibility = Visibility.Visible;
                EnsureLogProviders();
                RefreshProviderCombo();
                if (_selectedLogProvider == null)
                    ProdLogsStatusText.Text = "No log provider found. Place a provider DLL into the \"Log providers directory\" (Settings) and retry.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void EnsureLogProviders()
        {
            if (_logProviders != null) return;

            var discovered = LogProviderLoader.Discover(ResolveLogProvidersDirectory());
            if (discovered.Count > 0)
            {
                // Prefer external providers; fall back to the offline stub only when none are present.
                _logProviders = discovered;
                _selectedLogProvider = discovered[0];
            }
            else
            {
                var fallback = new List<IProdLogProvider> { new StubProdLogProvider() };
                _logProviders = fallback;
                _selectedLogProvider = fallback[0];
            }
        }

        private string ResolveLogProvidersDirectory()
        {
            var configured = (_settings?.LogProvidersPath ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RPAWriter2", "Providers");
        }

        private void RefreshProviderCombo()
        {
            var providers = _logProviders;
            ProviderCombo.ItemsSource = providers;
            ProviderCombo.SelectedItem = _selectedLogProvider;
            ProviderCombo.Visibility = providers != null && providers.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
        {
            var provider = ProviderCombo.SelectedItem as IProdLogProvider;
            if (provider != null)
                _selectedLogProvider = provider;
        }

        private async void OnLoadProdTasks(object sender, RoutedEventArgs e)
        {
            try
            {
                var robotName = (RobotNameBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(robotName))
                {
                    ProdLogsStatusText.Text = "Enter a robot name (e.g. RPA-1212).";
                    return;
                }
                if (_selectedLogProvider == null)
                {
                    ProdLogsStatusText.Text = "No log provider available.";
                    return;
                }

                ProdLogsStatusText.Text = "Loading tasks…";
                LogsGrid.ItemsSource = null;
                var tasks = await _selectedLogProvider.GetTasksAsync(robotName);
                TasksGrid.ItemsSource = tasks;
                ProdLogsStatusText.Text = tasks.Count + " task(s) loaded. Select a task to see its logs.";
            }
            catch (Exception ex)
            {
                ProdLogsStatusText.Text = "Failed to load tasks: " + ex.Message;
                ShowError(ex);
            }
        }

        private async void OnTaskSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var task = TasksGrid.SelectedItem as ProdTask;
                if (task == null || _selectedLogProvider == null)
                {
                    LogsGrid.ItemsSource = null;
                    return;
                }

                ProdLogsStatusText.Text = "Loading logs…";
                var logs = await _selectedLogProvider.GetLogsAsync(task);
                LogsGrid.ItemsSource = logs;
                ProdLogsStatusText.Text = logs.Count + " log entry/entries loaded.";
            }
            catch (Exception ex)
            {
                ProdLogsStatusText.Text = "Failed to load logs: " + ex.Message;
                ShowError(ex);
            }
        }

        private async void OnAnalyzeProdLogs(object sender, RoutedEventArgs e)
        {
            try
            {
                var logs = LogsGrid.ItemsSource as IEnumerable<IProdLog>;
                if (logs == null)
                {
                    ProdLogsStatusText.Text = "Load logs first.";
                    return;
                }

                var list = logs.ToList();
                if (list.Count == 0)
                {
                    ProdLogsStatusText.Text = "No log entries to analyze.";
                    return;
                }

                var entries = list.Select(MapToLogEntry).ToList();
                var analysis = new LogAnalyzer().Analyze(entries);
                var prompt = BuildProdLogsPrompt(analysis, list);
                AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                await SendChatTextAsync(prompt);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private static LogEntry MapToLogEntry(IProdLog log)
        {
            return new LogEntry
            {
                Timestamp = log.Timestamp,
                Level = log.Level.ToString(),
                Source = log.Source,
                Message = log.Message,
                RawLine = string.IsNullOrWhiteSpace(log.RawLine) ? log.Message : log.RawLine
            };
        }

        private string BuildProdLogsPrompt(LogAnalysis analysis, IReadOnlyList<IProdLog> logs)
        {
            var sb = new StringBuilder();
            sb.AppendLine(AnalyzeProdLogsPrompt.TrimEnd());
            sb.AppendLine();
            sb.AppendLine(analysis.Summary);
            sb.AppendLine();

            if (analysis.Errors.Count > 0)
            {
                sb.AppendLine("Errors:");
                foreach (var entry in analysis.Errors.Take(50))
                    sb.AppendLine("  [" + FormatTimestamp(entry.Timestamp) + "] " + (entry.Source ?? "") + ": " + (entry.Message ?? ""));
                sb.AppendLine();
            }

            if (analysis.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var entry in analysis.Warnings.Take(50))
                    sb.AppendLine("  [" + FormatTimestamp(entry.Timestamp) + "] " + (entry.Source ?? "") + ": " + (entry.Message ?? ""));
                sb.AppendLine();
            }

            sb.AppendLine("Full log:");
            foreach (var log in logs.Take(200))
                sb.AppendLine("  [" + FormatTimestamp(log.Timestamp) + "] [" + log.Level + "] " + (log.Source ?? "") + ": " + (log.Message ?? ""));

            var text = sb.ToString();
            if (text.Length > ProdLogMaxChars)
                text = text.Substring(0, ProdLogMaxChars) + "\n... (truncated)";
            return text;
        }

        private static string FormatTimestamp(DateTime? timestamp)
        {
            return timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "?";
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
                LogService.Error(ex, "Failed to restore packages.");
            }
        }

        private async void OnRefreshPackages(object sender, RoutedEventArgs e)
        {
            var folder = RequirePackagesPath();
            if (folder == null) return;
            await RefreshPackagesAsync(folder);
        }

        private async Task RefreshPackagesAsync(string folder)
        {
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var state = dte == null ? new RobotProjectState() : _packageCatalog.GetRobotProjectState(dte);

            var packages = await _packageCatalog.ScanAsync(folder);

            _packages.Clear();
            foreach (var package in packages)
            {
                package.IsInstalled = state.Installed.TryGetValue(package.Id, out var installedVersion);
                if (package.IsInstalled)
                    package.InstalledVersion = installedVersion;
                _packages.Add(new PackageListItem(package));
            }

            var installedCount = _packages.Count(p => p.IsInstalled);
            var projectText = state.Names.Count == 0
                ? "no RPA- robot project found"
                : "robot project(s): " + string.Join(", ", state.Names);
            SetPackagesStatus(_packages.Count + " package(s) in \"" + folder + "\" - " +
                installedCount + " installed - " + projectText + ".");
        }

        private async void OnInstallSelectedPackages(object sender, RoutedEventArgs e)
        {
            var folder = RequirePackagesPath();
            if (folder == null) return;
            var projects = GetRobotProjects();
            if (projects.Count == 0) return;

            var selected = _packages.Where(p => p.IsSelected && !p.IsInstalled).ToList();
            if (selected.Count == 0)
            {
                SetPackagesStatus("Select one or more packages that are not installed yet.");
                return;
            }

            try
            {
                _outputWindow.Activate();
                foreach (var project in projects)
                {
                    foreach (var package in selected)
                    {
                        await _robotTemplate.RunPackageManagerCommandAsync(
                            "Install-Package " + package.Id + " -Version " + package.Version +
                            " -Source \"" + folder + "\" -ProjectName \"" + project + "\"",
                            _outputWindow.CreateProgress());
                    }
                }
                await RefreshPackagesAsync(folder);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to install packages.");
            }
        }

        private async void OnUpdateSelectedPackages(object sender, RoutedEventArgs e)
        {
            var folder = RequirePackagesPath();
            if (folder == null) return;
            var projects = GetRobotProjects();
            if (projects.Count == 0) return;

            var selected = _packages.Where(p => p.IsSelected && p.IsInstalled).ToList();
            if (selected.Count == 0)
            {
                SetPackagesStatus("Select one or more installed packages to update.");
                return;
            }

            try
            {
                _outputWindow.Activate();
                foreach (var project in projects)
                {
                    foreach (var package in selected)
                    {
                        await _robotTemplate.RunPackageManagerCommandAsync(
                            "Update-Package " + package.Id + " -Source \"" + folder + "\" -ProjectName \"" + project + "\"",
                            _outputWindow.CreateProgress());
                    }
                }
                await RefreshPackagesAsync(folder);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to update packages.");
            }
        }

        private async void OnUninstallSelectedPackages(object sender, RoutedEventArgs e)
        {
            var folder = RequirePackagesPath();
            if (folder == null) return;
            var projects = GetRobotProjects();
            if (projects.Count == 0) return;

            var selected = _packages.Where(p => p.IsSelected && p.IsInstalled).ToList();
            if (selected.Count == 0)
            {
                SetPackagesStatus("Select one or more installed packages to uninstall.");
                return;
            }

            try
            {
                _outputWindow.Activate();
                foreach (var project in projects)
                {
                    foreach (var package in selected)
                    {
                        await _robotTemplate.RunPackageManagerCommandAsync(
                            "Uninstall-Package " + package.Id + " -ProjectName \"" + project + "\"",
                            _outputWindow.CreateProgress());
                    }
                }
                await RefreshPackagesAsync(folder);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to uninstall packages.");
            }
        }

        private string? RequirePackagesPath()
        {
            var path = (PackagesPathBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetPackagesStatus("Packages directory is not configured. Set the path in the \"Settings\" tab.");
                MainTabs.SelectedItem = SettingsTab;
                return null;
            }
            if (!Directory.Exists(path))
            {
                SetPackagesStatus("Packages directory does not exist: " + path);
                return null;
            }
            return path;
        }

        private IReadOnlyList<string> GetRobotProjects()
        {
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null)
            {
                SetPackagesStatus("No open solution. Open the RPA- robot solution first.");
                return new List<string>();
            }
            bool isOpen;
            try { isOpen = dte.Solution.IsOpen; }
            catch { isOpen = false; }
            if (!isOpen)
            {
                SetPackagesStatus("No open solution. Open the RPA- robot solution first.");
                return new List<string>();
            }
            var projects = _packageCatalog.GetRobotProjectState(dte).Names;
            if (projects.Count == 0)
                SetPackagesStatus("No \"RPA-\" robot project found in the open solution.");
            return projects;
        }

        private void SetPackagesStatus(string text)
        {
            PackagesStatusText.Text = text ?? "";
        }

        private bool FilterPackages(object item)
        {
            var filter = (PackageFilterBox?.Text ?? "").Trim();
            if (filter.Length == 0)
                return true;
            return item is PackageListItem package &&
                   package.Id.StartsWith(filter, StringComparison.Ordinal);
        }

        private void OnPackageFilterChanged(object sender, TextChangedEventArgs e)
            => _packagesView?.Refresh();

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
                KbStatusText.Text = "Solutions in the knowledge base: " + records.Count;
                foreach (var r in records)
                    AppendKbMessage((r.Name ?? "<unnamed>") + " — tasks: " + (r.Tasks?.Count ?? 0));
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

        #pragma warning disable VSTHRD100 // WPF event handlers are async void

        private async void OnCommit(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitStatusText.Text = "";
                HideCommitResult();
                LogService.Info("Commit control requested.");
                var solutionDir = GetOpenSolutionDirectory();
                if (solutionDir == null)
                {
                    LogService.Warn("Commit control: no open solution.");
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                LogService.Info("Commit control: solutionDir=" + solutionDir);
                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                LogService.Info("Commit control: repoRoot=" + repoRoot + ", branch=" + branch);
                if (!string.Equals(branch, CommitBranchName, StringComparison.Ordinal))
                {
                    LogService.Warn("Commit control blocked: current branch is not '" + CommitBranchName + "'.");
                    ShowCommitWarning("Commit is only available on the '" + CommitBranchName + "' branch. Current branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch));
                    return;
                }

                ShowCommitEditor();
                CommitMessageBox.Focus();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Commit control: failed to open.");
                ShowCommitWarning("Could not open the commit control: " + ex.Message);
            }
        }

        private async void OnCommitSend(object sender, RoutedEventArgs e)
        {
            var message = CommitMessageBox.Text.Trim();
            if (message.Length == 0)
            {
                CommitStatusText.Text = "Commit message is required.";
                CommitMessageBox.Focus();
                return;
            }

            SetCommitBusy(true);
            CommitStatusText.Text = "Committing…";
            LogService.Info("Commit send requested; message=" + message.Length + " chars");
            try
            {
                CommitStatusText.Text = "Saving documents…";
                if (!await TrySaveAllDocumentsAsync())
                {
                    CommitStatusText.Text = "Some documents could not be saved. Save them manually and retry.";
                    return;
                }

                var solutionDir = GetOpenSolutionDirectory();
                if (solutionDir == null)
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                LogService.Info("Commit send: repoRoot=" + repoRoot + ", branch=" + branch);
                if (!string.Equals(branch, CommitBranchName, StringComparison.Ordinal))
                {
                    ShowCommitWarning("Commit is only available on the '" + CommitBranchName + "' branch. Current branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch));
                    return;
                }

                await _git.StageAllAsync(repoRoot);
                await _git.CommitAsync(repoRoot, message);

                CommitMessageBox.Clear();
                CloseCommitControl();
                LogService.Info("Commit send: success (" + CommitBranchName + ").");
                ShowCommitResult("Committed to " + CommitBranchName + ": " + message, true);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Commit send failed.");
                ShowCommitResult(ex.Message, false);
            }
            finally
            {
                SetCommitBusy(false);
            }
        }

        private async void OnCommitAutoGenerate(object sender, RoutedEventArgs e)
        {
            if (_ai == null)
            {
                CommitStatusText.Text = "AI provider is not available.";
                return;
            }

            SetCommitBusy(true);
            CommitStatusText.Text = "Generating commit message…";
            LogService.Info("Commit auto-generate requested.");
            try
            {
                CommitStatusText.Text = "Saving documents…";
                if (!await TrySaveAllDocumentsAsync())
                {
                    CommitStatusText.Text = "Some documents could not be saved. Save them manually and retry.";
                    return;
                }

                var solutionDir = GetOpenSolutionDirectory();
                if (solutionDir == null)
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                LogService.Info("Commit auto-generate: repoRoot=" + repoRoot);

                var message = await GenerateCommitMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(message))
                {
                    CommitStatusText.Text = "No changes detected. If you have unsaved edits in Visual Studio, save them (Ctrl+Shift+S) and retry.";
                    return;
                }

                LogService.Info("Commit auto-generate: AI response=" + message.Length + " chars");
                CommitMessageBox.Text = message;
                CommitStatusText.Text = "";
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Commit auto-generate failed.");
                CommitStatusText.Text = ex.Message;
            }
            finally
            {
                SetCommitBusy(false);
            }
        }

        #pragma warning restore VSTHRD100

        /// <summary>
        /// Saves every open document in Visual Studio so the on-disk files reflect the
        /// current editor contents before a commit message is generated or committed.
        /// Returns false when any document remains unsaved (e.g. an untitled buffer).
        /// </summary>
        private async Task<bool> TrySaveAllDocumentsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
            if (dte?.Documents == null)
                return true;

            try
            {
                dte.ExecuteCommand("File.SaveAll");
            }
            catch (Exception ex)
            {
                LogService.Warn("TrySaveAllDocumentsAsync: File.SaveAll failed: " + ex.Message);
            }

            var unsaved = new List<string>();
            foreach (Document doc in dte.Documents)
            {
                try
                {
                    if (!doc.Saved)
                    {
                        var name = string.IsNullOrWhiteSpace(doc.Name) ? doc.FullName : doc.Name;
                        if (string.IsNullOrWhiteSpace(name))
                            name = "(untitled)";
                        unsaved.Add(name);
                    }
                }
                catch
                {
                    // Ignore documents whose state cannot be read.
                }
            }

            if (unsaved.Count > 0)
            {
                LogService.Warn("TrySaveAllDocumentsAsync: unsaved documents remain: " + string.Join(", ", unsaved));
                return false;
            }

            return true;
        }

        private static List<string> ExtractRobotNames(IEnumerable<string> changedPaths)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in changedPaths)
            {
                var path = (raw ?? "").Trim();
                if (path.Length == 0)
                    continue;
                var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0)
                    path = path.Substring(arrow + 4).Trim();
                if (path.Length == 0)
                    continue;
                var slash = path.IndexOf("/", StringComparison.Ordinal);
                var first = slash < 0 ? path : path.Substring(0, slash);
                if (first.StartsWith("RPA-", StringComparison.OrdinalIgnoreCase) && first.Length > 4)
                    names.Add(first);
            }
            return names.ToList();
        }

        private static bool IsUnderRobot(string path, string robotName)
        {
            var cleaned = (path ?? "").Trim();
            var arrow = cleaned.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                cleaned = cleaned.Substring(arrow + 4).Trim();
            return cleaned.StartsWith(robotName + "/", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(cleaned, robotName, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRobotChangeMaterial(string robotName, string diff, IReadOnlyList<string> files)
        {
            var sb = new StringBuilder();
            sb.Append("Robot: ").AppendLine(robotName);
            if (!string.IsNullOrWhiteSpace(diff))
            {
                sb.AppendLine("Diff:");
                sb.AppendLine(diff.TrimEnd());
            }
            sb.AppendLine("Changed files:");
            foreach (var f in files)
                sb.AppendLine(f);
            return sb.ToString();
        }

        private static string BuildOtherChangeMaterial(IReadOnlyList<string> files)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Changed files (outside robot folders):");
            foreach (var f in files)
                sb.AppendLine(f);
            return sb.ToString();
        }

        private async Task<string> SummarizeChangesAsync(string content, string systemPrompt)
        {
            if (content.Length > CommitDiffMaxChars)
                content = content.Substring(0, CommitDiffMaxChars) + "\n… (diff truncated)";
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = content }
            };
            var response = await _ai.ChatAsync(messages, systemPrompt);
            var text = response?.Answer?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The AI provider returned an empty message.");
            return text;
        }

        private async Task<string?> GenerateCommitMessageAsync(string repoRoot)
        {
            var changedFiles = await _git.GetChangedFilesAsync(repoRoot);
            if (changedFiles.Count == 0)
                return null;
            var robotNames = ExtractRobotNames(changedFiles);
            if (robotNames.Count == 0)
            {
                var diff = await _git.GetDiffForMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(diff))
                    return null;
                return await SummarizeChangesAsync(diff, CommitMessageSystemPrompt);
            }
            var parts = new List<string>();
            foreach (var robot in robotNames)
            {
                var files = changedFiles.Where(f => IsUnderRobot(f, robot)).ToList();
                var diff = await _git.GetDiffForPathAsync(repoRoot, robot);
                var material = BuildRobotChangeMaterial(robot, diff, files);
                var summary = await SummarizeChangesAsync(material, CommitRobotSummarySystemPrompt);
                parts.Add(robot + ": " + summary);
            }
            var otherFiles = changedFiles.Where(f => robotNames.All(r => !IsUnderRobot(f, r))).ToList();
            if (otherFiles.Count > 0)
            {
                var summary = await SummarizeChangesAsync(BuildOtherChangeMaterial(otherFiles), CommitOtherSummarySystemPrompt);
                parts.Add("chore: " + summary);
            }
            return string.Join(Environment.NewLine, parts);
        }

        private void OnCommitClose(object sender, RoutedEventArgs e)
        {
            CloseCommitControl();
        }

        private void ShowCommitWarning(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCommitWarning(message)));
                return;
            }
            CommitControlHost.Visibility = Visibility.Visible;
            CommitWarningBar.Visibility = Visibility.Visible;
            CommitEditorPanel.Visibility = Visibility.Collapsed;
            CommitWarningText.Text = message;
        }

        private void ShowCommitEditor()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ShowCommitEditor));
                return;
            }
            CommitControlHost.Visibility = Visibility.Visible;
            CommitWarningBar.Visibility = Visibility.Collapsed;
            CommitEditorPanel.Visibility = Visibility.Visible;
            CommitMessageBox.Clear();
            CommitStatusText.Text = "";
        }

        private void CloseCommitControl()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CloseCommitControl));
                return;
            }
            CommitControlHost.Visibility = Visibility.Collapsed;
            CommitWarningBar.Visibility = Visibility.Collapsed;
            CommitEditorPanel.Visibility = Visibility.Collapsed;
        }

        private void SetCommitBusy(bool busy)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetCommitBusy(busy)));
                return;
            }
            CommitSendButton.IsEnabled = !busy;
            CommitAutoGenButton.IsEnabled = !busy;
        }

        /// <summary>
        /// Shows the commit result as a colored bar under the toolbar buttons instead of
        /// writing it into the chat: green on success, orange on failure.
        /// </summary>
        private void ShowCommitResult(string message, bool success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCommitResult(message, success)));
                return;
            }
            CommitResultText.Text = message;
            CommitResultBar.Background = (Brush)FindResource(success ? "Commit.SuccessBackground" : "Commit.ErrorBackground");
            CommitResultBar.Visibility = Visibility.Visible;
        }

        private void HideCommitResult()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideCommitResult));
                return;
            }
            CommitResultBar.Visibility = Visibility.Collapsed;
            CommitResultText.Text = "";
        }

        private void OnCloseCommitResult(object sender, RoutedEventArgs e)
        {
            HideCommitResult();
        }

        private void OnCopyCommitResult(object sender, RoutedEventArgs e)
        {
            var text = CommitResultText.Text;
            if (string.IsNullOrEmpty(text))
                return;
            try { Clipboard.SetText(text); }
            catch { /* clipboard busy — ignore */ }
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

                // Finalize the "Thinking…" placeholder in place: it keeps its streamed
                // reasoning (so the thoughts stay visible) and the final answer is appended
                // as a NEW message after any tool calls.
                thinking.IsThinking = false;
                thinking.IsPartial = false;

                // Fall back to the result's reasoning when nothing was streamed.
                if (string.IsNullOrWhiteSpace(thinking.Reasoning) && !string.IsNullOrWhiteSpace(result.Reasoning))
                    thinking.Reasoning = result.Reasoning;

                if (thinking.HasReasoning)
                {
                    // Keep the placeholder as a reasoning-only block above the tool calls.
                    thinking.Text = string.Empty;
                }
                else
                {
                    // No reasoning to show — drop the empty placeholder.
                    _messages.Remove(thinking);
                }

                // Emit the final answer as a new message so it appears after the tool calls.
                if (!string.IsNullOrWhiteSpace(result.FinalText))
                {
                    AppendChatMessage(new ChatMessage
                    {
                        Kind = ChatMessageKind.AgentText,
                        Mode = modeName,
                        Text = result.FinalText
                    });
                }
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
            var toolCallingMode = string.Equals(_settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowedPermissions = BuildAllowedPermissions();
            _agentRunner = new AgentRunner(_ai, registry, mode => AgentPromptBuilder.Build(mode, registry, toolCallingMode, allowedPermissions), toolCallingMode: toolCallingMode, allowedPermissions: allowedPermissions);
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
                RefreshPinnedMessage();
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

        private void OnPermissionsClick(object sender, RoutedEventArgs e)
        {
            AllowReadFilesCheckBox.IsChecked = _settings.AllowReadFiles;
            AllowEditFilesCheckBox.IsChecked = _settings.AllowEditFiles;
            AllowExecuteCommandCheckBox.IsChecked = _settings.AllowExecuteCommand;
            AllowWebFetchCheckBox.IsChecked = _settings.AllowWebFetch;
            AllowMcpCheckBox.IsChecked = _settings.AllowMcp;
            PermissionsOverlay.Visibility = Visibility.Visible;
        }

        private void OnPermissionsClose(object sender, RoutedEventArgs e)
        {
            PermissionsOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnPermissionChanged(object sender, RoutedEventArgs e)
        {
            _settings.AllowReadFiles = AllowReadFilesCheckBox?.IsChecked ?? true;
            _settings.AllowEditFiles = AllowEditFilesCheckBox?.IsChecked ?? true;
            _settings.AllowExecuteCommand = AllowExecuteCommandCheckBox?.IsChecked ?? false;
            _settings.AllowWebFetch = AllowWebFetchCheckBox?.IsChecked ?? false;
            _settings.AllowMcp = AllowMcpCheckBox?.IsChecked ?? false;
            SettingsStore.Save(_settings);
            RebuildAgentTools();
        }

        private AgentPermissions BuildAllowedPermissions()
        {
            var allowed = (AgentPermissions)0;
            if (_settings.AllowReadFiles) allowed |= AgentPermissions.ReadFiles;
            if (_settings.AllowEditFiles) allowed |= AgentPermissions.EditFiles;
            if (_settings.AllowExecuteCommand) allowed |= AgentPermissions.ExecuteCommand;
            if (_settings.AllowWebFetch) allowed |= AgentPermissions.WebFetch;
            if (_settings.AllowMcp) allowed |= AgentPermissions.Mcp;
            return allowed;
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
            }
            finally
            {
                _loadingHistory = false;
            }

            RefreshPinnedMessage();
            ShowConversationView();
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
                RefreshPinnedMessage();
                ChatScroll.ScrollToEnd();
            }
            finally
            {
                _loadingHistory = false;
            }

            if (markDirty)
                ScheduleHistorySave();
        }

        private void RefreshHistoryList()
        {
            try
            {
                _conversations.Clear();
                foreach (var conversation in ChatHistoryStore.ListConversations())
                    _conversations.Add(conversation);
            }
            catch
            {
                // best-effort refresh of the history list
            }
        }

        private void ShowConversationView()
        {
            _chatViewMode = ChatViewMode.Conversation;
            ApplyChatViewMode();
        }

        private void ShowHistoryView()
        {
            _chatViewMode = ChatViewMode.History;
            RefreshHistoryList();
            ApplyChatViewMode();
        }

        private void ApplyChatViewMode()
        {
            var conversation = _chatViewMode == ChatViewMode.Conversation;
            ChatToolbarPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatListContainer.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatSplitter.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatInputPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            PinnedHeaderPanel.Visibility = conversation && _pinnedMessage != null ? Visibility.Visible : Visibility.Collapsed;
            HistoryViewPanel.Visibility = conversation ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnCloseChat(object sender, RoutedEventArgs e)
        {
            SaveHistoryNow();
            ShowHistoryView();
        }

        private void OnShowHistory(object sender, RoutedEventArgs e)
        {
            SaveHistoryNow();
            ShowHistoryView();
        }

        private void OnHistoryCardClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ChatConversation conversation })
            {
                OpenConversation(conversation.Id);
                ShowConversationView();
            }
        }

        private void OnDeleteHistoryCard(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement { DataContext: ChatConversation conversation }))
                return;

            var result = MessageDialog.Show(
                "Delete chat",
                "Delete this chat?\n\n" + conversation.CardTitle,
                MessageDialogCommandSet.YesNo);
            if (result != MessageDialogCommand.Yes)
                return;

            try
            {
                ChatHistoryStore.DeleteConversation(conversation.Id);
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to delete chat: " + ex.Message);
                return;
            }

            if (conversation.Id == _currentConversation?.Id)
                ResetCurrentConversation();

            RefreshHistoryList();
        }

        private void OnClearHistory(object sender, RoutedEventArgs e)
        {
            var result = MessageDialog.Show(
                "Clear history",
                "Delete all chats? This cannot be undone.",
                MessageDialogCommandSet.YesNo);
            if (result != MessageDialogCommand.Yes)
                return;

            try
            {
                ChatHistoryStore.DeleteAllConversations();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to clear chat history: " + ex.Message);
                return;
            }

            ResetCurrentConversation();
            RefreshHistoryList();
        }

        /// <summary>Resets the in-memory conversation to a fresh empty one without persisting it.</summary>
        private void ResetCurrentConversation()
        {
            _historySaveTimer.Stop();

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
            }
            finally
            {
                _loadingHistory = false;
            }

            RefreshPinnedMessage();
        }

        private const double PinnedCollapsedHeight = 48.0;
        private const double PinnedExpandedHeight = 240.0;

        private void OnPinnedExpandToggle(object sender, RoutedEventArgs e)
        {
            _pinnedExpanded = !_pinnedExpanded;
            UpdatePinnedExpandState();
        }

        private void UpdatePinnedExpandState()
        {
            PinnedTextScroll.MaxHeight = _pinnedExpanded ? PinnedExpandedHeight : PinnedCollapsedHeight;
            PinnedTextScroll.VerticalScrollBarVisibility = _pinnedExpanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PinnedExpandGlyph.Points = PointCollection.Parse(_pinnedExpanded ? "6,9 12,15 18,9" : "9,18 15,12 9,6");
        }

        private void RefreshPinnedMessage()
        {
            _pinnedMessage = _messages.FirstOrDefault(m => m.Kind == ChatMessageKind.User)
                          ?? _messages.FirstOrDefault();
            PinnedHeaderPanel.DataContext = _pinnedMessage;
            _pinnedExpanded = false;
            UpdatePinnedExpandState();
            if (_chatListView != null)
                _chatListView.Refresh();
            ApplyChatViewMode();
        }

        private void OnNewChat(object sender, RoutedEventArgs e)
        {
            StartNewChat();
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
            _settings.PackagesPath = PackagesPathBox.Text.Trim();
            _settings.RulesPath = RulesPathBox.Text.Trim();
            _settings.ContextWindowSize = ParseContextWindowSize();
            _settings.CompactThreshold = ParseFraction(CompactThresholdBox?.Text, 0.85, 0.0, 1.0);
            _settings.ReserveFraction = ParseFraction(ReserveFractionBox?.Text, 0.12, 0.0, 0.5);
            _settings.KeepRecentMessages = ParsePositiveInt(KeepRecentBox?.Text, 6);
            _settings.ToolResultMaxChars = ParsePositiveInt(ToolResultMaxCharsBox?.Text, 12000);
            _settings.MaxIterations = ParsePositiveInt(MaxIterationsBox?.Text, 8);
            _settings.AutoCompact = AutoCompactCheckBox?.IsChecked ?? true;
            _settings.UseToolRole = UseToolRoleCheckBox?.IsChecked ?? true;
            _settings.ToolCallingMode = (ToolCallingModeCombo?.SelectedIndex ?? 0) == 1 ? "Native" : "JsonChat";
            _settings.LogFilePath = LogFilePathBox.Text.Trim();
            _settings.LogProvidersPath = LogProvidersPathBox.Text.Trim();
            _logProviders = null;
            _selectedLogProvider = null;
            _settings.TimeoutSeconds = ParsePositiveInt(TimeoutSecondsBox?.Text, 300);
            _settings.RetryTimeoutMinutes = ParsePositiveInt(RetryTimeoutMinutesBox?.Text, 30);
            _settings.RetryBaseDelaySeconds = ParsePositiveInt(RetryBaseDelaySecondsBox?.Text, 1);
            _settings.RetryMaxDelaySeconds = ParsePositiveInt(RetryMaxDelaySecondsBox?.Text, 30);
            _settings.InputPricePerMillion = ParsePrice(InputPriceBox?.Text, 0.27);
            _settings.CacheHitPricePerMillion = ParsePrice(CacheHitPriceBox?.Text, 0.07);
            _settings.OutputPricePerMillion = ParsePrice(OutputPriceBox?.Text, 1.10);
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

        private static double ParsePrice(string text, double fallback)
        {
            if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0)
                return value;
            return fallback;
        }
    }
}
