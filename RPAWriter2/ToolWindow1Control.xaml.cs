using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter.Core.Checkpoints;
using RPAWriter2.Services;
using RPAWriter2.Services.AgentTools;
using RPAWriter2.Services.Checkpoints;
using RPAWriter2.Controls;
using RPAWriter2.Markdown;
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
using System.Xml.Linq;
using RPAWriter.Core.Models;
using Material.Icons;

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
        private readonly GitService _git = new GitService();
        private readonly CheckpointServiceFactory _checkpoints = new CheckpointServiceFactory();
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
        private MicrophoneRecorder _mic;
        private ISpeechToTextService _stt;
        private bool _isRecording;
        private bool _isTranscribing;
        private List<string> _robotNameHistory = new List<string>();
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();
        private readonly ObservableCollection<ChatMessage> _specMessages = new ObservableCollection<ChatMessage>();
        private readonly PackageCatalogService _packageCatalog = new PackageCatalogService();
        private readonly ObservableCollection<PackageListItem> _packages = new ObservableCollection<PackageListItem>();
        private readonly ObservableCollection<ChatAttachment> _attachments = new ObservableCollection<ChatAttachment>();
        private readonly ObservableCollection<MessageSample> _messageSamples = new ObservableCollection<MessageSample>();

        private CancellationTokenSource? _chatCts;
        private TaskCompletionSource<string>? _questionTcs;
        private ChatMessage? _activeQuestion;
        private CancellationTokenSource? _kbImportCts;
        private volatile bool _kbImportPaused;
        private CancellationTokenSource? _securityAuditCts;
        private readonly DispatcherTimer _chatStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _settingsToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _deployToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _notificationAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _chatStartedAt;
        private const string SpecLockedMessage = "The spec file is open in another application (e.g. Word/Excel). Close it and try again.";
        private const string CommitBranchName = "dev";
        private const string SkipAnswerText = "User skipped the question.";
        private const string CommitMessageSystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the provided changes into a single-line commit message in the format \"type(scope): summary\". " +
            "Write the summary in Russian. " +
            "Respond with only the commit message text — no markdown, no quotes, no extra explanation.";
        private const string CommitRobotSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the robot changes below into a single concise line in the imperative mood. " +
            "Write the summary in Russian. " +
            "Respond with only the summary text — no RPA-...: prefix, no markdown, no quotes, no extra explanation.";
        private const string CommitOtherSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the changes below into a single concise line in the imperative mood. " +
            "Write the summary in Russian. " +
            "Respond with only the summary text — no markdown, no quotes, no extra explanation.";

        private const int CommitDiffMaxChars = 8000;
        private const string AnalyzeOutputWindowPrompt =
            "Analyze the Visual Studio Output window content below. " +
            "Identify errors, warnings and their likely root causes, and suggest concrete fixes:\n\n";
        private const string AnalyzeProdLogsPrompt =
            "Analyze the production robot logs below. " +
            "Summarize the robot run, list errors and warnings with their likely root causes, and suggest fixes:\n\n";
        private const int ProdLogMaxChars = 40000;
        private const string RobotNameDefault = "RPA-";
        private const int RobotNameHistoryMax = 20;
        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;
        private readonly ObservableCollection<ChatConversation> _conversations = new ObservableCollection<ChatConversation>();
        private ChatConversation _currentConversation = new ChatConversation();
        private readonly DispatcherTimer _historySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        private bool _loadingHistory;
        private ChatViewMode _chatViewMode = ChatViewMode.Conversation;
        // Starts true so XAML load events (e.g. ModeToggle ValueChanged) don't
        // overwrite persisted state before RestoreUiState() runs.
        private bool _restoringUiState = true;
        private ChatMessage? _pinnedMessage;
        private ICollectionView? _packagesView;
        private ICollectionView? _svgView;
        private bool _pinnedExpanded;
        // Guards against Checked/Unchecked events firing while the permissions
        // overlay is being populated from settings (which would otherwise save
        // half-initialized checkbox states back over the real settings).
        private bool _suppressPermissionEvents;

        private bool _baselineCheckpointCreated;
        private string? _lastCheckpointHash;
        private int _iterationNumber;

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            InitializeMinLevelCombo();
            _settingsToastTimer.Tick += OnSettingsToastTick;
            _deployToastTimer.Tick += OnDeployToastTick;
            _notificationAutoHideTimer.Tick += OnNotificationAutoHideTick;
            MainTabs.SelectionChanged += OnMainTabsSelectionChanged;
            ControlsInnerTabs.SelectionChanged += OnControlsInnerTabsSelectionChanged;
            ChatMessages.Messages = _messages;
            ChatMessages.Filter = message => !ReferenceEquals(message, _pinnedMessage);
            ChatMessages.CopyMessageRequested += OnChatMessageCopyRequested;
            ChatMessages.QuestionAnswered += OnChatMessageQuestionAnswered;
            ChatMessages.QuestionSkipped += OnChatMessageQuestionSkipped;
            ChatMessages.CheckpointCompareRequested += OnCheckpointCompareRequested;
            ChatMessages.CheckpointRestoreRequested += OnCheckpointRestoreRequested;
            ChatMessages.DiffOpenRequested += OnDiffOpenRequested;
            AttachmentsList.ItemsSource = _attachments;
            SpecMessages.Messages = _specMessages;
            SpecMessages.CopyMessageRequested += OnChatMessageCopyRequested;
            HistoryList.ItemsSource = _conversations;
            _packagesView = CollectionViewSource.GetDefaultView(_packages);
            _packagesView.Filter = FilterPackages;
            PackagesList.ItemsSource = _packagesView;
            LoadSettings();
            MaterialIconDataProvider.InitializeGeometryParser(s => Geometry.Parse(s));
            PopulateControlsGallery();
            ShowPendingOptionsWarnings();
            RecreateAiProvider();
            _mic = new MicrophoneRecorder();
            _mic.LevelChanged += OnMicLevelChanged;
            _stt = SpeechToTextFactory.Create(_settings);
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
            TemperatureSlider.Value = _settings.Temperature;
            UpdateTemperatureLabel();
            KbPathBox.Text = _settings.KnowledgeBasePath ?? "";
            RobotsRootPathBox.Text = _settings.RobotsRootPath ?? "";
            RobotTemplateNameBox.Text = _settings.RobotTemplateName ?? "";
            ShowRobotCreatorCheckBox.IsChecked = _settings.ShowRobotCreator;
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
            MemoryBankModeCombo.SelectedIndex = MemoryBankModeToIndex(_settings.MemoryBankMode);
            SecurityAuditModeCombo.SelectedIndex = string.Equals(_settings.SecurityAuditMode, "CredentialsOnly", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            LogFilePathBox.Text = _settings.LogFilePath ?? "";
            ChatLogFilePathBox.Text = _settings.ChatLogFilePath ?? "";
            LogProvidersPathBox.Text = _settings.LogProvidersPath ?? "";
            TimeoutSecondsBox.Text = _settings.TimeoutSeconds.ToString();
            RetryTimeoutMinutesBox.Text = _settings.RetryTimeoutMinutes.ToString();
            RetryBaseDelaySecondsBox.Text = _settings.RetryBaseDelaySeconds.ToString();
            RetryMaxDelaySecondsBox.Text = _settings.RetryMaxDelaySeconds.ToString();
            InputPriceBox.Text = _settings.InputPricePerMillion.ToString(CultureInfo.InvariantCulture);
            CacheHitPriceBox.Text = _settings.CacheHitPricePerMillion.ToString(CultureInfo.InvariantCulture);
            OutputPriceBox.Text = _settings.OutputPricePerMillion.ToString(CultureInfo.InvariantCulture);
            UserNameBox.Text = _settings.UserName ?? "";
            SkillsPathBox.Text = _settings.SkillsPath ?? "";
            AddressByNameCheckBox.IsChecked = _settings.AddressByName;
            AllowSubagentsCheckBox.IsChecked = _settings.AllowSubagents;
            CheckpointsEnabledCheckBox.IsChecked = _settings.CheckpointsEnabled;
            SubagentMaxIterationsBox.Text = _settings.SubagentMaxIterations.ToString();
            SubagentMaxDepthBox.Text = _settings.SubagentMaxDepth.ToString();
            LoadRobotNameHistory();
            ShowControlsCheckBox.IsChecked = _settings.ShowControls;
            ApplyControlsVisibility();
            SpeechToTextProviderCombo.SelectedIndex = SpeechToTextProviderToIndex(_settings.SpeechToTextProvider);
            SpeechToTextModelBox.Text = _settings.SpeechToTextModel ?? "whisper-1";
            SpeechToTextLanguageBox.Text = _settings.SpeechToTextLanguage ?? "ru";
            VoskModelPathBox.Text = _settings.VoskModelPath ?? "";
            SpeechAutoSendCheckBox.IsChecked = _settings.SpeechAutoSend;
            AutoCaptureRuntimeExceptionsCheckBox.IsChecked = _settings.AutoCaptureRuntimeExceptions;
        }

        private void OnTemperatureChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateTemperatureLabel();
        }

        private void UpdateTemperatureLabel()
        {
            if (TemperatureValueText != null)
                TemperatureValueText.Text = Math.Round(TemperatureSlider.Value, 1).ToString("0.0", CultureInfo.InvariantCulture);
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

        /// <summary>
        /// Shows an interactive question in the chat (with answer buttons) and waits for the
        /// user's answer. Invoked by the ask_question tool handler, possibly on a background thread.
        /// </summary>
        private async Task<string> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload { Question = question ?? "", Options = options ?? new List<string>() }
            };
            _activeQuestion = message;
            AppendChatMessage(message);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _questionTcs = tcs;
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    return await tcs.Task;
                }
                finally
                {
                    if (tcs.Task.IsCanceled && _activeQuestion?.Payload is QuestionPayload q && string.IsNullOrEmpty(q.Answer))
                    {
                        q.Answer = ChatMessagesControl.SkipAnswerText;
                        q.IsSkipped = true;
                    }
                    _activeQuestion = null;
                    _questionTcs = null;
                }
            }
        }

        private void OnChatMessageCopyRequested(object? sender, ChatMessage message)
        {
            try { Clipboard.SetText(FormatMessageForCopy(message)); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnCopyPinnedMessage(object sender, RoutedEventArgs e)
        {
            if (_pinnedMessage == null) return;
            try { Clipboard.SetText(FormatMessageForCopy(_pinnedMessage)); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnChatMessageQuestionAnswered(object? sender, ChatMessage message)
        {
            if (_questionTcs == null) return;
            ScheduleHistorySave();
            _questionTcs.TrySetResult((message.Payload as QuestionPayload)?.Answer ?? "");
        }

        private void OnChatMessageQuestionSkipped(object? sender, ChatMessage message)
        {
            if (_questionTcs == null) return;
            ScheduleHistorySave();
            _questionTcs.TrySetResult((message.Payload as QuestionPayload)?.Answer ?? ChatMessagesControl.SkipAnswerText);
        }

        private void AppendExtensionError(Exception ex)
        {
            if (ex == null) return;
            LogService.Error(ex);
            AppendExtensionError(ExceptionFormatter.Format(ex));
        }

        private void AppendChatMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendChatMessage(message)));
                return;
            }
            ChatMessages.Append(message);
            if (message.Kind == ChatMessageKind.User && _pinnedMessage == null)
                RefreshPinnedMessage();
            if (!_loadingHistory)
                ScheduleHistorySave();
        }

        // ------------------------------------------------------------------
        // Checkpoints (shadow git snapshots)
        // ------------------------------------------------------------------

        /// <summary>Creates the immutable "task start" baseline once per chat session.</summary>
        private async Task EnsureBaselineCheckpointAsync()
        {
            if (_baselineCheckpointCreated) return;
            _baselineCheckpointCreated = true;
            _lastCheckpointHash = await SaveCheckpointAsync("task start", allowEmpty: true, CancellationToken.None);
        }

        /// <summary>
        /// Snapshots the just-finished iteration and, when it changed files, appends a Diff
        /// indicator whose button opens the iteration diff in the diff tool window.
        /// </summary>
        private async Task SaveIterationCheckpointAndDiffAsync()
        {
            _iterationNumber++;
            var newHash = await SaveCheckpointAsync("iteration " + _iterationNumber, allowEmpty: false, CancellationToken.None);

            var fromHash = _lastCheckpointHash;
            if (string.IsNullOrWhiteSpace(newHash) || string.IsNullOrWhiteSpace(fromHash))
                return;

            try
            {
                var service = await GetCheckpointServiceForCurrentTaskAsync();
                var diffs = await Task.Run(() => service.GetDiffAsync(fromHash!, newHash!));
                var files = CheckpointDiffBuilder.Build(diffs);
                _lastCheckpointHash = newHash;

                AppendChatMessage(new ChatMessage
                {
                    Kind = ChatMessageKind.Diff,
                    Payload = new DiffPayload { CommitHash = newHash!, Files = files }
                });
            }
            catch (Exception ex)
            {
                LogService.Warn("Iteration diff failed: " + ex.Message);
                _lastCheckpointHash = newHash;
            }
        }

        /// <summary>
        /// Saves a checkpoint for the current conversation + solution and appends an indicator
        /// to the chat when a new snapshot is created. Returns the commit hash, or null when
        /// disabled/unavailable/empty. Never throws.
        /// </summary>
        private async Task<string?> SaveCheckpointAsync(string message, bool allowEmpty, CancellationToken ct)
        {
            if (!_settings.CheckpointsEnabled) return null;

            var ws = GetOpenSolutionDirectory();
            if (string.IsNullOrWhiteSpace(ws)) return null;

            var taskId = _currentConversation?.Id ?? Guid.NewGuid().ToString("N");
            try
            {
                var result = await Task.Run(async () =>
                {
                    var service = _checkpoints.GetOrCreate(taskId, ws);
                    if (!service.IsInitialized)
                        await service.InitializeAsync(ct).ConfigureAwait(false);
                    return await service.SaveAsync(message, allowEmpty, ct).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);

                if (result != null && !result.IsEmpty && !string.IsNullOrWhiteSpace(result.CommitHash))
                {
                    var service = _checkpoints.GetOrCreate(taskId, ws);
                    AppendCheckpointIndicator(taskId, ws, result.CommitHash, message, service.Checkpoints.Count);
                }

                return result?.CommitHash;
            }
            catch (Exception ex)
            {
                LogService.Warn("Checkpoint save failed: " + ex.Message);
                return null;
            }
        }

        private void AppendCheckpointIndicator(string taskId, string workspaceDir, string commitHash, string message, int sequence)
        {
            // SaveCheckpointAsync continues on a thread-pool thread; touch _messages on the UI thread only.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendCheckpointIndicator(taskId, workspaceDir, commitHash, message, sequence)));
                return;
            }

            // A new snapshot means the previous latest checkpoint now has changes after it.
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Kind == ChatMessageKind.Checkpoint && _messages[i].Payload is CheckpointPayload previous)
                {
                    previous.HasChanges = true;
                    break;
                }
            }

            AppendChatMessage(new ChatMessage
            {
                Kind = ChatMessageKind.Checkpoint,
                Payload = new CheckpointPayload
                {
                    TaskId = taskId,
                    WorkspaceDir = workspaceDir,
                    CommitHash = commitHash,
                    Message = message,
                    Sequence = sequence,
                    CreatedAtUtc = DateTime.UtcNow,
                    HasChanges = false,
                }
            });
        }

        private async void OnCheckpointCompareRequested(object? sender, ChatMessage message)
        {
            if (message.Payload is not CheckpointPayload payload) return;
            try
            {
                var service = await GetCheckpointServiceAsync(payload).ConfigureAwait(true);
                var diffs = await Task.Run(() => service.GetDiffAsync(payload.CommitHash, null)).ConfigureAwait(true);
                var files = await Task.Run(() => CheckpointDiffBuilder.Build(diffs)).ConfigureAwait(true);
                DiffToolWindowCommand.Instance?.ShowWindow(files, "Diff since checkpoint " + payload.ShortHash);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Checkpoint compare failed: " + ex.Message, "Compare checkpoint", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDiffOpenRequested(object? sender, ChatMessage message)
        {
            if (message.Payload is not DiffPayload payload) return;
            DiffToolWindowCommand.Instance?.ShowWindow(payload.Files, "Diff " + payload.ShortHash);
        }

        private async void OnCheckpointRestoreRequested(object? sender, ChatMessage message)
        {
            if (message.Payload is not CheckpointPayload payload) return;

            var confirm = MessageBox.Show(
                "Restore files to checkpoint " + payload.ShortHash + "?",
                "Restore checkpoint",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var service = await GetCheckpointServiceAsync(payload).ConfigureAwait(true);
                await Task.Run(() => service.RestoreAsync(payload.CommitHash)).ConfigureAwait(true);
                payload.HasChanges = false;
                LogService.Info("Restored files to checkpoint " + payload.ShortHash + ".");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Checkpoint restore failed.");
                ShowCheckpointRestoreError("Checkpoint restore failed: " + ex.Message);
            }
        }

        private async Task<ICheckpointService> GetCheckpointServiceAsync(CheckpointPayload payload)
        {
            var ws = string.IsNullOrWhiteSpace(payload.WorkspaceDir) ? GetOpenSolutionDirectory() : payload.WorkspaceDir;
            if (string.IsNullOrWhiteSpace(ws))
                throw new InvalidOperationException("No solution is open.");

            var service = _checkpoints.GetOrCreate(payload.TaskId, ws);
            if (!service.IsInitialized)
                await service.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            return service;
        }

        /// <summary>Returns the checkpoint service for the current conversation and open solution.</summary>
        private async Task<ICheckpointService> GetCheckpointServiceForCurrentTaskAsync()
        {
            var ws = GetOpenSolutionDirectory();
            if (string.IsNullOrWhiteSpace(ws))
                throw new InvalidOperationException("No solution is open.");

            var taskId = _currentConversation?.Id ?? Guid.NewGuid().ToString("N");
            var service = _checkpoints.GetOrCreate(taskId, ws);
            if (!service.IsInitialized)
                await service.InitializeAsync(CancellationToken.None);
            return service;
        }

        private void AppendSpecMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendSpecMessage(message)));
                return;
            }
            SpecMessages.Append(message);
            CopySpecAllButton.Visibility = Visibility.Visible;
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

        private void OnCopyMessageSample(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MessageSample sample } && !string.IsNullOrEmpty(sample.Text))
            {
                try { Clipboard.SetText(sample.Text); }
                catch { /* clipboard busy — ignore */ }
            }
        }

        private void OnSendMessageSample(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MessageSample sample } && !string.IsNullOrWhiteSpace(sample.Text))
                AppendSelectionToInput(sample.Text);
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
            LogService.ChatDebug("AI message sent: role=" + (e.Role ?? "") + ", chars=" + (e.Content == null ? 0 : e.Content.Length));
        }

        private void OnPromptAssembled(object? sender, PromptAssembledEventArgs e)
        {
            _lastEstimatedInputTokens = e.EstimatedInputTokens;
            if (!Dispatcher.CheckAccess())
                Dispatcher.BeginInvoke(new Action(UpdateTokenUsageDisplay));
            else
                UpdateTokenUsageDisplay();

            var dump = FormatPromptDump(e);
            LogService.ChatInfo(dump);
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
            LogService.ChatWarn(
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

        private void ShowNotice(string message, NotificationKind kind = NotificationKind.Info)
        {
            NoticeBar.Kind = kind;
            NoticeBar.Message = message;
            NoticeBar.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        private void OnNoticeClose(object sender, RoutedEventArgs e)
        {
            HideNotice();
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
            bool noSolution;
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                noSolution = dte?.Solution == null || !dte.Solution.IsOpen;
            }
            catch
            {
                noSolution = true;
            }
            var showRobotCreator = noSolution && (_settings?.ShowRobotCreator ?? true);
            CreateRobotPanel.Visibility = showRobotCreator ? Visibility.Visible : Visibility.Collapsed;
            RestorePackagesPanel.Visibility = noSolution ? Visibility.Collapsed : Visibility.Visible;
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

        private async void OnDoItClick(object sender, RoutedEventArgs e)
        {
            InputBox.Text = "Do it!!!";
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

        private void OnClearInput(object sender, RoutedEventArgs e)
        {
            InputBox.Clear();
            InputBox.Focus();
        }

        private async void OnMicClick(object sender, RoutedEventArgs e)
        {
            if (_isTranscribing) return;

            if (!_isRecording)
            {
                try
                {
                    _mic.Start();
                }
                catch (Exception ex)
                {
                    ShowVoiceError("Microphone unavailable: " + ex.Message);
                    return;
                }
                _isRecording = true;
                SetMicState(recording: true);
            }
            else
            {
                byte[] audio;
                try
                {
                    audio = _mic.Stop();
                }
                catch (Exception ex)
                {
                    _isRecording = false;
                    SetMicState(recording: false);
                    ShowVoiceError("Failed to stop recording: " + ex.Message);
                    return;
                }

                _isRecording = false;
                SetMicState(recording: false);

                if (audio == null || audio.Length == 0)
                {
                    AppendSystemMessage("No speech detected.");
                    return;
                }

                _isTranscribing = true;
                SetMicState(recording: false, transcribing: true);
                try
                {
                    var language = (_settings.SpeechToTextLanguage ?? "ru").Trim();
                    var text = await _stt.TranscribeAsync(audio, language, CancellationToken.None);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (_settings.SpeechAutoSend && string.IsNullOrWhiteSpace(InputBox.Text))
                        {
                            InputBox.Text = text;
                            await SendMessageAsync();
                        }
                        else
                        {
                            AppendSelectionToInput(text);
                        }
                    }
                    else
                    {
                        AppendSystemMessage("No speech recognized.");
                    }
                }
                catch (Exception ex)
                {
                    ShowVoiceError("Speech recognition failed: " + ex.Message);
                }
                finally
                {
                    _isTranscribing = false;
                    SetMicState(recording: false);
                }
            }
        }

        private void OnMicLevelChanged(object sender, double level)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnMicLevelChanged(sender, level)));
                return;
            }

            if (_isRecording)
                MicButton.Opacity = 0.6 + level * 0.4;
            else
                MicButton.Opacity = 1.0;
        }

        private void SetMicState(bool recording, bool transcribing = false)
        {
            MicButton.ToolTip = transcribing ? "Transcribing…" : (recording ? "Stop recording" : "Start voice input");
            MicButton.Opacity = 1.0;
            MicIcon.Fill = transcribing
                ? Brushes.Orange
                : recording
                    ? Brushes.Red
                    : (Brush)FindResource("Dark.Text");
        }

        private static int SpeechToTextProviderToIndex(string value)
        {
            if (string.Equals(value, "OpenAiWhisper", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "Vosk", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        private static string SpeechToTextProviderIndexToName(int index)
        {
            switch (index)
            {
                case 1: return "OpenAiWhisper";
                case 2: return "Vosk";
                default: return "Auto";
            }
        }

        private void OnAttach(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Attach image or text file",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true)
                return;

            foreach (var fileName in dlg.FileNames)
                TryAddAttachment(fileName);
        }

        private void TryAddAttachment(string fileName)
        {
            try
            {
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                var bytes = File.ReadAllBytes(fileName);

                if (IsImageExtension(extension))
                {
                    if (bytes.Length > 10 * 1024 * 1024)
                    {
                        AppendChat("Skipped " + Path.GetFileName(fileName) + ": image is larger than 10 MB.");
                        return;
                    }

                    _attachments.Add(new ChatAttachment
                    {
                        FileName = Path.GetFileName(fileName),
                        IsImage = true,
                        MediaType = GetImageMediaType(extension),
                        DataBase64 = Convert.ToBase64String(bytes)
                    });
                    return;
                }

                if (IsProbablyBinary(bytes))
                {
                    AppendChat("Skipped " + Path.GetFileName(fileName) + ": binary files cannot be attached as text.");
                    return;
                }

                _attachments.Add(new ChatAttachment
                {
                    FileName = Path.GetFileName(fileName),
                    IsImage = false,
                    TextContent = DecodeText(bytes)
                });
            }
            catch (Exception ex)
            {
                AppendChat("Could not attach " + fileName + ": " + ex.Message);
            }
            finally
            {
                UpdateAttachmentsVisibility();
            }
        }

        private void OnRemoveAttachment(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button { Tag: ChatAttachment attachment }))
                return;
            _attachments.Remove(attachment);
            UpdateAttachmentsVisibility();
        }

        private void UpdateAttachmentsVisibility()
        {
            AttachmentsList.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsImageExtension(string extension)
        {
            switch (extension)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".webp":
                    return true;
                default:
                    return false;
            }
        }

        private static string GetImageMediaType(string extension)
        {
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".bmp":
                    return "image/bmp";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
        }

        private static bool IsProbablyBinary(byte[] bytes)
        {
            var length = Math.Min(bytes.Length, 8000);
            for (var i = 0; i < length; i++)
            {
                if (bytes[i] == 0)
                    return true;
            }
            return false;
        }

        private static string DecodeText(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }


        private async void OnImportSpec(object sender, RoutedEventArgs e)
        {
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Spec import is only available when a solution is open.", NotificationKind.Warning);
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
                ShowNotice(SpecLockedMessage, NotificationKind.Error);
                return;
            }

            ShowImportProgress("Reading file…");
            try
            {
                var docsDir = Path.Combine(solutionDir, "docs");
                Directory.CreateDirectory(docsDir);

                // Unchanged copy of the original spec file.
                SaveImportedSpec(dlg.FileName, solutionDir);

                var mediaDir = Path.Combine(docsDir, "media");
                var spec = new OpenXmlDocumentReader().Read(dlg.FileName, mediaDir);

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
                ShowNotice(SpecLockedMessage, NotificationKind.Error);
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

        /// <summary>Deletes all files and subfolders inside the solution's docs folder after confirmation.</summary>
        private void OnClearSpec(object sender, RoutedEventArgs e)
        {
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Clear docs is only available when a solution is open.", NotificationKind.Warning);
                return;
            }

            var docsDir = Path.Combine(solutionDir, "docs");
            if (!Directory.Exists(docsDir))
            {
                ShowNotice("No docs folder to clear.", NotificationKind.Info);
                return;
            }

            var result = MessageDialog.Show(
                "Clear docs",
                "Delete all files and folders in the docs folder?\n\n" + docsDir + "\n\nThis cannot be undone.",
                MessageDialogCommandSet.YesNo);
            if (result != MessageDialogCommand.Yes)
                return;

            try
            {
                var removed = 0;
                foreach (var file in Directory.GetFiles(docsDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    removed++;
                }

                foreach (var dir in Directory.GetDirectories(docsDir, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length))
                {
                    Directory.Delete(dir);
                }

                _currentSpec = null;
                ShowNotice("Cleared " + removed + " file(s) from docs.", NotificationKind.Success);
            }
            catch (Exception ex)
            {
                AppendExtensionError("Failed to clear docs folder: " + ex.Message);
            }
        }

        private async void OnGenerateAlgorithm(object sender, RoutedEventArgs e)
        {
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Generate algorithm is only available when a solution is open.", NotificationKind.Warning);
                return;
            }

            var algorithmGenerator = _ai as IAlgorithmGenerator;
            if (algorithmGenerator == null)
            {
                ShowNotice("The current AI provider does not support algorithm generation.", NotificationKind.Warning);
                return;
            }

            var docsDir = Path.Combine(solutionDir, "docs");
            Directory.CreateDirectory(docsDir);

            var specMarkdown = ResolveSpecMarkdown(docsDir);
            if (specMarkdown == null)
            {
                ShowNotice("Import a spec first or ensure docs/spec.md exists.", NotificationKind.Warning);
                return;
            }

            ShowImportProgress("Generating algorithm…");
            try
            {
                var drafts = new List<string>();
                for (var i = 0; i < 3; i++)
                {
                    SetImportStatus("Generating algorithm draft " + (i + 1) + "/3…");
                    var streaming = AppendSpecStreaming();
                    streaming.Reasoning = "Generating algorithm draft " + (i + 1) + "/3…";
                    var draft = await algorithmGenerator.GenerateAlgorithmDraftAsync(specMarkdown);
                    streaming.IsThinking = false;
                    streaming.Text = "Draft " + (i + 1) + " generated.";
                    drafts.Add(draft);
                    File.WriteAllText(Path.Combine(docsDir, "alg-" + (i + 1) + ".md"), draft);
                    AppendSpecText("Wrote alg-" + (i + 1) + ".md");
                }

                SetImportStatus("Reconciling drafts…");
                var consolidating = AppendSpecStreaming();
                consolidating.Reasoning = "Reconciling drafts…";
                var consolidated = await algorithmGenerator.ConsolidateAlgorithmAsync(specMarkdown, drafts);
                consolidating.IsThinking = false;
                consolidating.Text = "Algorithm consolidated.";

                File.WriteAllText(Path.Combine(docsDir, "alg.md"), consolidated.AlgorithmMarkdown ?? "");
                File.WriteAllText(Path.Combine(docsDir, "questions.md"), consolidated.QuestionsMarkdown ?? "");
                AppendSpecText("Wrote alg.md and questions.md");
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

        private string? ResolveSpecMarkdown(string docsDir)
        {
            if (_currentSpec != null && !string.IsNullOrWhiteSpace(_currentSpec.MarkdownText))
                return _currentSpec.MarkdownText;

            var specPath = Path.Combine(docsDir, "spec.md");
            return File.Exists(specPath) ? File.ReadAllText(specPath) : null;
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

        /// <summary>
        /// Runs a read-only security audit over every robot solution (RPA-&lt;number&gt;.sln/.slnx)
        /// located in the same directory as the currently open solution and in the sibling robot
        /// folders (uppercase "RPA-" prefix) at the same level. Shows the discovered solutions on
        /// the Spec tab, audits each one with the AI provider, writes the combined report to the
        /// current solution's docs folder and opens it in Visual Studio.
        /// </summary>
        private async void OnRobotSecurityAudit(object sender, RoutedEventArgs e)
        {
            if (_securityAuditCts != null)
            {
                ShowNotice("Security audit is already running.", NotificationKind.Warning);
                return;
            }

            if (_ai == null)
            {
                ShowNotice("The AI provider is not available. Configure it in the Settings tab first.", NotificationKind.Warning);
                return;
            }

            var baseDirectory = GetOpenSolutionDirectory();
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = PickSecurityAuditDirectory();
                if (baseDirectory == null) return;
            }

            var solutions = RobotSecurityAuditService.FindRobotSolutionsInSiblingFolders(baseDirectory);
            if (solutions.Count == 0)
            {
                ShowNotice("No robot solutions (RPA-<number>.sln/.slnx) found in " + baseDirectory + " or its sibling RPA-* folders. Select the directory that contains them.", NotificationKind.Warning);
                var picked = PickSecurityAuditDirectory();
                if (picked == null) return;
                baseDirectory = picked;
                solutions = RobotSecurityAuditService.FindRobotSolutionsInSiblingFolders(baseDirectory);
                if (solutions.Count == 0)
                {
                    ShowNotice("No robot solutions (RPA-<number>.sln/.slnx) found in " + baseDirectory + " or its sibling RPA-* folders.", NotificationKind.Error);
                    return;
                }
            }

            var targetDirectory = GetOpenSolutionDirectory() ?? baseDirectory;
            var docsDirectory = Path.Combine(targetDirectory, "docs");
            var reportPath = Path.Combine(docsDirectory, "security_audit.md");

            _securityAuditCts = new CancellationTokenSource();
            AppendSpecText("Auditing " + solutions.Count + " robot solution(s)...");

            var mode = string.Equals(_settings?.SecurityAuditMode, "CredentialsOnly", StringComparison.OrdinalIgnoreCase)
                ? SecurityAuditMode.CredentialsOnly
                : SecurityAuditMode.Full;

            var service = new RobotSecurityAuditService(_ai);
            service.MessageReported += OnSecurityAuditMessage;
            try
            {
                await service.AuditAsync(solutions, reportPath, cancellationToken: _securityAuditCts.Token, mode: mode);
                ShowNotice("Security audit completed. Report: " + reportPath, NotificationKind.Success);

                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                if (dte != null && File.Exists(reportPath))
                {
                    try { await VsDocumentService.OpenOrActivateAsync(dte, reportPath); }
                    catch (Exception ex) { LogService.Error(ex, "Failed to open the security audit report."); }
                }
            }
            catch (OperationCanceledException)
            {
                AppendSpecText("Security audit stopped by user.");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Security audit failed.");
                ShowError(ex);
                AppendSpecText("Security audit failed.\n\n" + ex);
            }
            finally
            {
                service.MessageReported -= OnSecurityAuditMessage;
                _securityAuditCts.Dispose();
                _securityAuditCts = null;
            }
        }

        private string? PickSecurityAuditDirectory()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the directory that contains robot solutions (RPA-<number>.sln/.slnx).",
                ShowNewFolderButton = true
            })
            {
                var configured = (RobotsRootPathBox?.Text ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                    dialog.SelectedPath = configured;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    return dialog.SelectedPath;
            }
            return null;
        }

        private void OnSecurityAuditMessage(object? sender, string message)
        {
            LogService.Info("Security audit: " + message);
            AppendSpecText(message);
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

        private async void OnDeploy(object sender, RoutedEventArgs e)
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte == null || dte.Solution == null || !dte.Solution.IsOpen)
                {
                    ShowNotice("No open solution. Open a solution and retry.", NotificationKind.Warning);
                    return;
                }

                var robotProjects = _packageCatalog.GetRobotProjectState(dte).Names;
                if (robotProjects.Count == 0)
                {
                    LogService.Warn("Deploy blocked: no RPA- robot project found in the open solution.");
                    ShowDeployToast("No RPA- robot project found in the open solution.", success: false);
                    return;
                }

                if (!await _buildFeedback.HasSolutionConfigurationAsync("Publish"))
                {
                    LogService.Warn("Deploy blocked: 'Publish' solution configuration is not configured.");
                    ShowDeployToast("The 'Publish' solution configuration is not configured. Create it in Build → Configuration Manager and retry.", success: false);
                    return;
                }

                await _buildFeedback.BuildAsync("Publish");
                LogService.Info("Publish build started.");
                ShowDeployToast("Publish build started.", success: true);
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
            return AppDataPaths.Resolve("Providers");
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
            await LoadProdTasksAsync();
        }

        private void OnRobotNameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = LoadProdTasksAsync();
        }

        private async Task LoadProdTasksAsync()
        {
            try
            {
                var robotName = (RobotNameBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(robotName))
                {
                    ProdLogsStatusText.Text = "Enter a robot name (e.g. RPA-1212).";
                    return;
                }

                AddRobotNameToHistory(robotName);

                if (_selectedLogProvider == null)
                {
                    ProdLogsStatusText.Text = "No log provider available.";
                    return;
                }

                ProdLogsStatusText.Text = "Loading tasks…";
                LogsGrid.ItemsSource = null;
                var tasks = await _selectedLogProvider.GetTasksAsync(robotName, BuildProdLogQuery());
                TasksGrid.ItemsSource = tasks;
                ProdLogsStatusText.Text = tasks.Count + " task(s) loaded. Select a task to see its logs.";
            }
            catch (Exception ex)
            {
                ProdLogsStatusText.Text = "Failed to load tasks: " + ex.Message;
                ShowError(ex);
            }
        }

        private void AddRobotNameToHistory(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (string.Equals(name, RobotNameDefault, StringComparison.OrdinalIgnoreCase)) return;

            _robotNameHistory.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
            _robotNameHistory.Insert(0, name);
            if (_robotNameHistory.Count > RobotNameHistoryMax)
                _robotNameHistory.RemoveRange(RobotNameHistoryMax, _robotNameHistory.Count - RobotNameHistoryMax);

            _settings.RobotNameHistory = new List<string>(_robotNameHistory);
            SettingsStore.Save(_settings);
            UpdateRobotNameBox();
        }

        private void LoadRobotNameHistory()
        {
            _robotNameHistory = (_settings.RobotNameHistory ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            UpdateRobotNameBox();
        }

        private void UpdateRobotNameBox()
        {
            var last = _robotNameHistory.Count > 0 ? _robotNameHistory[0] : RobotNameDefault;
            RobotNameBox.ItemsSource = _robotNameHistory.Skip(1).ToList();
            RobotNameBox.Text = last;
        }

        private async void OnTaskSelected(object sender, SelectionChangedEventArgs e)
        {
            await ReloadSelectedTaskLogsAsync();
        }

        private async void OnMinLevelChanged(object sender, SelectionChangedEventArgs e)
        {
            await ReloadSelectedTaskLogsAsync();
        }

        private async void OnDateRangeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProdLogsPanel.Visibility != Visibility.Visible) return;
            var robotName = (RobotNameBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(robotName)) return;
            await LoadProdTasksAsync();
        }

        private async Task ReloadSelectedTaskLogsAsync()
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
                var logs = await _selectedLogProvider.GetLogsAsync(task, BuildProdLogQuery());
                LogsGrid.ItemsSource = logs;
                ProdLogsStatusText.Text = logs.Count + " log entry/entries loaded.";
            }
            catch (Exception ex)
            {
                ProdLogsStatusText.Text = "Failed to load logs: " + ex.Message;
                ShowError(ex);
            }
        }

        private void InitializeMinLevelCombo()
        {
            MinLevelCombo.Items.Add(new ComboBoxItem { Content = "All", Tag = null });
            foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
                MinLevelCombo.Items.Add(new ComboBoxItem { Content = level.ToString(), Tag = level });
            MinLevelCombo.SelectedIndex = 0;
        }

        private ProdLogQuery BuildProdLogQuery()
        {
            var query = new ProdLogQuery();

            if (MinLevelCombo.SelectedItem is ComboBoxItem item && item.Tag is LogLevel level)
                query.MinLevel = level;

            if (FromDatePicker.SelectedDate.HasValue)
                query.FromUtc = DateTime.SpecifyKind(FromDatePicker.SelectedDate.Value.Date, DateTimeKind.Utc);

            if (ToDatePicker.SelectedDate.HasValue)
                query.ToUtc = DateTime.SpecifyKind(ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

            return query;
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
                RawLine = string.IsNullOrWhiteSpace(log.RawLine) ? log.Message : log.RawLine,
                Parameters = log.Parameters?.ToList() ?? new List<LogParameter>()
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
                sb.AppendLine("  [" + FormatTimestamp(log.Timestamp) + "] [" + log.Level + "] " + (log.Source ?? "") + ": " + (log.Message ?? "") + FormatParameters(log.Parameters));

            var text = sb.ToString();
            if (text.Length > ProdLogMaxChars)
                text = text.Substring(0, ProdLogMaxChars) + "\n... (truncated)";
            return text;
        }

        private static string FormatTimestamp(DateTime? timestamp)
        {
            return timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "?";
        }

        private static string FormatParameters(IReadOnlyList<LogParameter> parameters)
        {
            if (parameters == null || parameters.Count == 0) return "";
            var pairs = new List<string>();
            foreach (var parameter in parameters)
                pairs.Add((parameter.Key ?? "") + "=" + (parameter.Value ?? ""));
            return "  parameters: " + string.Join(", ", pairs);
        }

        private void OnConvertMarkdownToDocx(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Markdown file",
                Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                var outputPath = Path.Combine(dir, baseName + ".docx");
                var result = new MarkdownToDocxConverter().Convert(dlg.FileName, outputPath);
                AppendSpecText(
                    "Converted " + Path.GetFileName(dlg.FileName) + " → " + Path.GetFileName(outputPath) +
                    " (paragraphs: " + result.Paragraphs + ", images: " + result.Images + ", tables: " + result.Tables + ").");
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
            foreach (var group in packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                var versions = group
                    .Select(p => p.Version)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var info = group.First();
                info.IsInstalled = state.Installed.TryGetValue(info.Id, out var installedVersion);
                if (info.IsInstalled)
                    info.InstalledVersion = installedVersion;

                var item = new PackageListItem(info, versions)
                {
                    SelectedVersion = info.IsInstalled && versions.Contains(info.InstalledVersion, StringComparer.OrdinalIgnoreCase)
                        ? info.InstalledVersion
                        : versions.FirstOrDefault() ?? ""
                };
                _packages.Add(item);
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
                        if (string.IsNullOrWhiteSpace(package.SelectedVersion))
                            continue;
                        await _robotTemplate.RunPackageManagerCommandAsync(
                            "Install-Package " + package.Id + " -Version " + package.SelectedVersion +
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
                        if (string.IsNullOrWhiteSpace(package.SelectedVersion))
                            continue;
                        await _robotTemplate.RunPackageManagerCommandAsync(
                            "Update-Package " + package.Id + " -Version " + package.SelectedVersion +
                            " -Source \"" + folder + "\" -ProjectName \"" + project + "\"",
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
            DoItButton.IsEnabled = phase != AgentPhase.Streaming;
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

        private static string BuildProjectChangeMaterial(string projectName, string diff, IReadOnlyList<string> files)
        {
            var sb = new StringBuilder();
            sb.Append("Project: ").AppendLine(projectName);
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
            sb.AppendLine("Changed files (outside robot solutions and projects):");
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

            var plan = CommitMessageBuilder.GroupChangedFiles(repoRoot, changedFiles);
            if (plan.Robots.Count == 0 && plan.Projects.Count == 0 && plan.OtherFiles.Count == 0)
                return null;

            // No robot or project at all — keep the existing generic fallback.
            if (plan.Robots.Count == 0 && plan.Projects.Count == 0)
            {
                var diff = await _git.GetDiffForMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(diff))
                    return null;
                return await SummarizeChangesAsync(diff, CommitMessageSystemPrompt);
            }

            var parts = new List<string>();
            foreach (var robot in plan.Robots)
            {
                var diff = await _git.GetDiffForPathAsync(repoRoot, robot.RelativePath);
                var material = BuildRobotChangeMaterial(robot.Name, diff, robot.Files);
                var summary = await SummarizeChangesAsync(material, CommitRobotSummarySystemPrompt);
                parts.Add(CommitMessageBuilder.FormatRobotLine(robot.Name, summary));
            }
            foreach (var project in plan.Projects)
            {
                var diff = await _git.GetDiffForPathAsync(repoRoot, project.RelativePath);
                var material = BuildProjectChangeMaterial(project.Name, diff, project.Files);
                var summary = await SummarizeChangesAsync(material, CommitOtherSummarySystemPrompt);
                parts.Add(CommitMessageBuilder.FormatProjectLine(project.Name, summary));
            }
            if (plan.OtherFiles.Count > 0)
            {
                var summary = await SummarizeChangesAsync(BuildOtherChangeMaterial(plan.OtherFiles), CommitOtherSummarySystemPrompt);
                parts.Add(CommitMessageBuilder.FormatChoreLine(summary));
            }
            return CommitMessageBuilder.JoinLines(parts);
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
            CommitWarningBar.Kind = NotificationKind.Warning;
            CommitWarningBar.Message = message;
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
            CommitResultBar.Kind = success ? NotificationKind.Success : NotificationKind.Error;
            CommitResultBar.Message = message;
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
            CommitResultBar.Message = "";
        }

        private void OnCloseCommitResult(object sender, RoutedEventArgs e)
        {
            HideCommitResult();
        }

        private async void OnNotificationSendToChat(object sender, RoutedEventArgs e)
        {
            if (sender is NotificationBanner banner && !string.IsNullOrWhiteSpace(banner.Message))
                await SubmitErrorToChatAsync(banner.Message);
        }

        private async Task SendMessageAsync()
        {
            var text = InputBox.Text.Trim();
            var attachments = _attachments.ToList();
            if (text.Length == 0 && attachments.Count == 0) return;
            InputBox.Clear();
            _attachments.Clear();
            UpdateAttachmentsVisibility();
            await SendChatTextAsync(text, attachments);
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
            await SendChatTextAsync(text, null);
        }

        private async Task SendChatTextAsync(string text, IReadOnlyList<ChatAttachment> attachments)
        {
            // If the agent is waiting for an answer to an interactive question, treat the
            // typed chat message as the answer instead of dropping it while the agent is busy.
            if (_questionTcs != null
                && _activeQuestion?.Payload is QuestionPayload pendingQuestion
                && !string.IsNullOrWhiteSpace(text))
            {
                pendingQuestion.Answer = text;
                pendingQuestion.IsSkipped = false;
                AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = text });
                _questionTcs.TrySetResult(text);
                return;
            }

            if (_chatCts != null) return; // already awaiting the agent's reply
            if (_agentRunner == null) return;

            var images = new List<ChatImagePart>();
            var attachmentNotes = new List<string>();
            var fileBlocks = new StringBuilder();

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    if (attachment == null)
                        continue;

                    if (attachment.IsImage)
                    {
                        images.Add(attachment.ToImagePart());
                        attachmentNotes.Add("Image: " + attachment.FileName);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(attachment.TextContent))
                        {
                            fileBlocks.AppendLine();
                            fileBlocks.AppendLine("Attached file: " + attachment.FileName);
                            fileBlocks.AppendLine("```");
                            fileBlocks.AppendLine(attachment.TextContent);
                            fileBlocks.AppendLine("```");
                        }
                        attachmentNotes.Add("File: " + attachment.FileName);
                    }
                }
            }

            var promptText = (text ?? "").Trim();
            if (fileBlocks.Length > 0)
            {
                promptText = string.IsNullOrWhiteSpace(promptText)
                    ? fileBlocks.ToString().TrimEnd()
                    : promptText.TrimEnd() + Environment.NewLine + fileBlocks.ToString().TrimEnd();
            }

            var uiText = (text ?? "").Trim();
            if (attachmentNotes.Count > 0)
            {
                uiText = string.IsNullOrWhiteSpace(uiText)
                    ? string.Join("  ", attachmentNotes)
                    : uiText + Environment.NewLine + string.Join("  ", attachmentNotes);
            }

            if (string.IsNullOrWhiteSpace(promptText) && images.Count == 0)
                return;

            var modeName = _mode.ToString();
            var history = BuildTranscript();
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = uiText });
            if (string.IsNullOrEmpty(_currentConversation.Title))
                _currentConversation.Title = BuildChatTitle(uiText);

            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var cts = new CancellationTokenSource();
            _chatCts = cts;
            StartChatStatus();
            LogService.ChatInfo("Agent chat request started (mode=" + modeName + ", input chars=" + promptText.Length + ", images=" + images.Count + ").");

            // Accumulate reasoning in a buffer and flush it to the bound property on a timer.
            // Writing every delta straight into the property would reallocate the string and
            // force a WPF re-layout on every chunk (O(n^2) for long thoughts); the timer caps
            // UI updates while the model streams.
            var reasoningBuffer = new StringBuilder();
            var reasoningTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            reasoningTimer.Tick += (_, __) =>
            {
                thinking.Reasoning = reasoningBuffer.ToString();
                reasoningTimer.Stop();
            };
            try
            {
                var progress = new Progress<ChatStreamDelta>(delta =>
                {
                    if (!string.IsNullOrEmpty(delta.ReasoningDelta))
                    {
                        reasoningBuffer.Append(delta.ReasoningDelta);
                        if (!reasoningTimer.IsEnabled)
                            reasoningTimer.Start();
                    }
                });
                await EnsureBaselineCheckpointAsync();

                var result = await _agentRunner.RunAsync(history, promptText, images, _mode, BuildContextBudget(), cts.Token, progress);

                reasoningTimer.Stop();

                // Finalize the "Thinking…" placeholder in place: it keeps its streamed
                // reasoning (so the thoughts stay visible) and the final answer is appended
                // as a NEW message after any tool calls.
                thinking.Reasoning = reasoningBuffer.ToString();
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

                await SaveIterationCheckpointAndDiffAsync();

                // Emit the final answer as a new message so it appears after the tool calls.
                if (!string.IsNullOrWhiteSpace(result.FinalText))
                {
                    var finalMessage = new ChatMessage
                    {
                        Kind = _mode == AgentMode.Act ? ChatMessageKind.ResultAnswerAct : ChatMessageKind.ResultAnswerPlan,
                        Mode = modeName,
                        Text = result.FinalText
                    };
                    AppendChatMessage(finalMessage);
                }
                StopChatStatus();
                LogService.ChatInfo("Agent chat completed (mode=" + modeName + ", answer chars=" + (result.FinalText == null ? 0 : result.FinalText.Length) + ").");
            }
            catch (OperationCanceledException)
            {
                reasoningTimer.Stop();
                thinking.Reasoning = reasoningBuffer.ToString();
                thinking.IsThinking = false;
                if (_messages.Contains(thinking))
                    AppendChat("Request cancelled.");
                StopChatStatus();
                LogService.ChatInfo("Agent chat request cancelled.");
            }
            catch (Exception ex)
            {
                reasoningTimer.Stop();
                thinking.Reasoning = reasoningBuffer.ToString();
                thinking.IsThinking = false;
                AppendExtensionError(ex);
                ShowChatStatusError(ex.Message);
                LogService.ChatError(ex, "Agent chat request failed.");
            }
            finally
            {
                reasoningTimer.Stop();
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
        /// Appends an externally produced text (e.g. an editor selection) into the chat input box
        /// without sending it. Safe to call from any thread.
        /// </summary>
        public void AppendSelectionToInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendSelectionToInput(text)));
                return;
            }

            ShowConversationView();

            var existing = InputBox.Text;
            InputBox.Text = string.IsNullOrWhiteSpace(existing)
                ? text
                : existing.TrimEnd() + Environment.NewLine + text;
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
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
                    case ChatMessageKind.ResultAnswerPlan:
                    case ChatMessageKind.ResultAnswerAct:
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

        private ContextBudget BuildSubAgentBudget()
        {
            var budget = BuildContextBudget();
            budget.MaxIterations = _settings.SubagentMaxIterations > 0 ? _settings.SubagentMaxIterations : 4;
            budget.AutoCompact = false;
            return budget;
        }

        /// <summary>
        /// Runs a nested agent loop for a sub-agent (spawn_agent). Appends a live
        /// <see cref="ChatMessageKind.Subagent"/> status card and updates it in place
        /// as the sub-agent makes tool calls, then returns the sub-agent's final report.
        /// </summary>
        private async Task<string> RunSubAgentAsync(string systemPrompt, string task, CancellationToken ct)
        {
            var registry = _toolRegistry;
            if (registry == null)
                return "spawn_agent unavailable: the tool registry is not initialized.";

            var toolCallingMode = string.Equals(_settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowedPermissions = BuildAllowedPermissions();
            var budget = BuildSubAgentBudget();

            var item = new SubagentStatusItem { Prompt = task };
            AppendChatMessage(new ChatMessage
            {
                Kind = ChatMessageKind.Subagent,
                Payload = new SubagentPayload { Item = item },
                Mode = _mode.ToString()
            });
            Dispatcher.BeginInvoke(new Action(() => item.Status = "running"));

            var runner = new AgentRunner(
                _ai,
                registry,
                m => AgentPromptBuilder.Build(m, registry, toolCallingMode, allowedPermissions),
                budget,
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowedPermissions);

            runner.ToolExecuted += (s, e) =>
            {
                var toolName = e.ToolName ?? "";
                var display = e.DisplayResult;
                var latest = string.IsNullOrWhiteSpace(display) ? toolName : toolName + ": " + display;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    item.ToolCalls += 1;
                    item.LatestToolCall = latest;
                }));
            };

            try
            {
                var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), task, _mode, budget, ct);
                var finalText = result?.FinalText ?? "";
                Dispatcher.BeginInvoke(new Action(() => { item.Status = "completed"; item.Result = finalText; }));
                return finalText;
            }
            catch (OperationCanceledException)
            {
                Dispatcher.BeginInvoke(new Action(() => { item.Status = "cancelled"; }));
                throw;
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => { item.Status = "failed"; item.Error = ex.Message; }));
                return "spawn_agent sub-agent failed: " + ex.Message;
            }
        }

        private void RebuildAgentTools()
        {
            var memoryBankMode = ParseMemoryBankMode();
            var registry = BuildToolRegistry(memoryBankMode);
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
            _agentRunner = new AgentRunner(_ai, registry, mode => AgentPromptBuilder.Build(mode, registry, toolCallingMode, allowedPermissions, _settings?.UserName ?? "", _settings?.AddressByName ?? false, memoryBankMode), toolCallingMode: toolCallingMode, allowedPermissions: allowedPermissions);
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
            UpdateMemoryBankUi();
        }

        private void OnAgentRetryOccurred(object? sender, AgentRetryArgs e)
        {
            LogService.ChatWarn("Agent retry: " + (e.Kind ?? "") + " attempt " + e.Attempt + "/" + e.AttemptLimit + " - " + (e.Detail ?? ""));
        }

        private void OnAgentContextCompacted(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentContextCompacted(sender, e)));
                return;
            }
            LogService.ChatInfo("Agent context compacted (summarized) to fit the context window.");
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
            InputBox.Text = ParseMemoryBankMode() == MemoryBankMode.Convention
                ? "update memory bank: read memory-bank/*.md files with read_files, then update the relevant files with editor."
                : "update memory bank: read the current memory bank with read_memory, then update the relevant files with update_memory.";
            InputBox.Focus();
        }

        private void UpdateMemoryBankUi()
        {
            if (UpdateMemoryButton == null) return;
            var mode = ParseMemoryBankMode();
            UpdateMemoryButton.Visibility = mode == MemoryBankMode.Disabled ? Visibility.Collapsed : Visibility.Visible;
            UpdateMemoryButton.ToolTip = mode == MemoryBankMode.Convention
                ? "Ask the agent to update the project memory bank (convention)"
                : "Ask the agent to update the project memory bank";
        }

        private void OnPermissionsClick(object sender, RoutedEventArgs e)
        {
            _suppressPermissionEvents = true;
            try
            {
                AllowReadFilesCheckBox.IsChecked = _settings.AllowReadFiles;
                AllowEditFilesCheckBox.IsChecked = _settings.AllowEditFiles;
                AllowExecuteCommandCheckBox.IsChecked = _settings.AllowExecuteCommand;
                AllowWebFetchCheckBox.IsChecked = _settings.AllowWebFetch;
                AllowMcpCheckBox.IsChecked = _settings.AllowMcp;
            }
            finally
            {
                _suppressPermissionEvents = false;
            }

            PermissionsOverlay.Visibility = Visibility.Visible;
        }

        private void OnPermissionsClose(object sender, RoutedEventArgs e)
        {
            PermissionsOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnDocsClick(object sender, RoutedEventArgs e)
        {
            ShowDocsIndex();
            DocsOverlay.Visibility = Visibility.Visible;
        }

        private void OnDocsHome(object sender, RoutedEventArgs e)
        {
            ShowDocsIndex();
        }

        private void OnDocsClose(object sender, RoutedEventArgs e)
        {
            DocsOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowDocsIndex()
        {
            RenderDocsFile(Path.Combine(HelpDocumentation.ResolveDocsDirectory(), "00-index.md"), "Documentation");
        }

        /// <summary>
        /// Renders a shipped documentation markdown file into the built-in Docs viewer.
        /// Relative links are routed back through <see cref="HandleDocsLink"/>.
        /// </summary>
        private void RenderDocsFile(string path, string title)
        {
            DocsTitleText.Text = title;
            if (!File.Exists(path))
            {
                DocsContent.Content = new TextBlock
                {
                    Text = "Documentation file not found: " + path,
                    Foreground = (Brush)FindResource("Dark.Text"),
                    TextWrapping = TextWrapping.Wrap
                };
                return;
            }

            MarkdownRenderer.RelativeLinkNavigator = HandleDocsLink;
            try
            {
                DocsContent.Content = MarkdownRenderer.Render(File.ReadAllText(path));
            }
            finally
            {
                MarkdownRenderer.RelativeLinkNavigator = null;
            }
        }

        /// <summary>
        /// Resolves a relative markdown link against the shipped Docs directory and renders
        /// the target file in the viewer. Returns false when the link cannot be resolved.
        /// </summary>
        private bool HandleDocsLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Only relative, local links are supported in the viewer; absolute URLs are
            // already handled by the renderer (http/https/mailto open in the browser).
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
                return false;

            var dir = HelpDocumentation.ResolveDocsDirectory();
            string target;
            try
            {
                target = Path.GetFullPath(Path.Combine(dir, url));
            }
            catch
            {
                return false;
            }

            var root = Path.GetFullPath(dir);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!File.Exists(target))
                return false;

            RenderDocsFile(target, Path.GetFileName(target));
            return true;
        }

        private void OnPermissionChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressPermissionEvents)
                return;

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
            if (_settings.AllowSubagents) allowed |= AgentPermissions.Subagents;
            return allowed;
        }

        private static int MemoryBankModeToIndex(string value)
        {
            if (string.Equals(value, "Tool", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "Convention", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0; // Disabled
        }

        private static string MemoryBankIndexToMode(int index)
        {
            if (index == 2) return "Convention";
            if (index == 1) return "Tool";
            return "Disabled";
        }

        private MemoryBankMode ParseMemoryBankMode()
        {
            var value = _settings?.MemoryBankMode;
            if (string.Equals(value, "Tool", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Tool;
            if (string.Equals(value, "Convention", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Convention;
            return MemoryBankMode.Disabled;
        }

        private AgentToolRegistry BuildToolRegistry(MemoryBankMode memoryBankMode)
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
                _robotTemplate,
                AskQuestionAsync,
                () => (SkillsPathBox?.Text ?? "").Trim(),
                memoryBankMode,
                RunSubAgentAsync,
                _settings.AllowSubagents,
                _settings.SubagentMaxDepth,
                () => _mode);
            factory.PixMwfStore = PixMwfMappingStore.Shared;
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
            LogService.ChatInfo("Agent tool executed: " + (e.ToolName ?? "") + (e.Blocked ? " (blocked)" : "") + ", result chars=" + (detail == null ? 0 : detail.Length));
            if (string.Equals(e.ToolName, "ask_question", StringComparison.OrdinalIgnoreCase))
                return; // The interactive question message already shows the Q&A.
            if (string.Equals(e.ToolName, "spawn_agent", StringComparison.OrdinalIgnoreCase))
                return; // The sub-agent status card already shows progress and the final report.
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

            RestoreUiState();
        }

        /// <summary>Restores the chat view, active conversation, agent mode and active tab from the last session.</summary>
        private void RestoreUiState()
        {
            _restoringUiState = true;
            try
            {
                var state = UiStateStore.Load();

                _mode = state.AgentMode;
                ModeToggle.SelectedValue = _mode == AgentMode.Act ? "Act"
                    : _mode == AgentMode.Help ? "Help"
                    : "Plan";
                ApplyModeBrush();

                SelectTabByHeader(state.ActiveTab);

                if (state.ChatViewMode == ChatViewMode.History)
                {
                    ShowHistoryView();
                }
                else
                {
                    var toOpen = ResolveConversationToOpen(state.LastConversationId);
                    if (toOpen != null)
                        OpenConversation(toOpen.Id, markDirty: false);
                    else
                        StartNewChat(markDirty: false);
                    ShowConversationView();
                }
            }
            finally
            {
                _restoringUiState = false;
            }
        }

        private ChatConversation? ResolveConversationToOpen(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    var stored = ChatHistoryStore.LoadConversation(id);
                    if (stored != null)
                        return stored;
                }
                catch
                {
                    // fall back to the most recent conversation below
                }
            }

            return _conversations.FirstOrDefault();
        }

        private static string MainTabToHeader(MainTab tab)
        {
            switch (tab)
            {
                case MainTab.Spec: return "Spec";
                case MainTab.KnowledgeBase: return "Knowledge base";
                case MainTab.Settings: return "Settings";
                case MainTab.Packages: return "Packages";
                case MainTab.Controls: return "Controls";
                default: return "Chat";
            }
        }

        private static MainTab HeaderToMainTab(string? header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return MainTab.Chat;

            var value = header.Trim();
            if (string.Equals(value, "Spec", StringComparison.OrdinalIgnoreCase)) return MainTab.Spec;
            if (string.Equals(value, "Knowledge base", StringComparison.OrdinalIgnoreCase)) return MainTab.KnowledgeBase;
            if (string.Equals(value, "Settings", StringComparison.OrdinalIgnoreCase)) return MainTab.Settings;
            if (string.Equals(value, "Packages", StringComparison.OrdinalIgnoreCase)) return MainTab.Packages;
            if (string.Equals(value, "Controls", StringComparison.OrdinalIgnoreCase)) return MainTab.Controls;
            return MainTab.Chat;
        }

        private void SelectTabByHeader(MainTab tab)
        {
            var header = MainTabToHeader(tab);

            foreach (var item in MainTabs.Items)
            {
                if (item is TabItem tabItem &&
                    tabItem.Visibility == Visibility.Visible &&
                    string.Equals(tabItem.Header as string, header, StringComparison.OrdinalIgnoreCase))
                {
                    MainTabs.SelectedItem = tabItem;
                    return;
                }
            }
        }

        private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringUiState)
                return;
            PersistUiState();
        }

        /// <summary>Writes the current chat/tab state so the next session can restore it.</summary>
        private void PersistUiState()
        {
            if (_restoringUiState)
                return;

            UiStateStore.Save(new UiStateData
            {
                ChatViewMode = _chatViewMode,
                LastConversationId = _currentConversation?.Id ?? "",
                AgentMode = _mode,
                ActiveTab = HeaderToMainTab((MainTabs.SelectedItem as TabItem)?.Header as string)
            });
        }

        private void OnWindowUnloaded(object sender, RoutedEventArgs e)
        {
            // UI state is already persisted on every explicit change (tab switch, view switch,
            // mode change, conversation open). Persisting again here would re-save whatever
            // transient state the window is in during teardown and can clobber an explicitly
            // chosen "History" view back to "Conversation".
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

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = null;
            _iterationNumber = 0;

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
                ChatMessages.ScrollToEnd();
            }
            finally
            {
                _loadingHistory = false;
            }

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = null;
            _iterationNumber = 0;

            if (markDirty)
                ScheduleHistorySave();
            PersistUiState();
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
            PersistUiState();
        }

        private void ShowHistoryView()
        {
            _chatViewMode = ChatViewMode.History;
            RefreshHistoryList();
            ApplyChatViewMode();
            PersistUiState();
        }

        private void ApplyChatViewMode()
        {
            var conversation = _chatViewMode == ChatViewMode.Conversation;
            ChatToolbarPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatMessages.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
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

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = null;
            _iterationNumber = 0;

            RefreshPinnedMessage();
            PersistUiState();
        }

        private const double PinnedExpandedHeight = 240.0;

        private void OnPinnedExpandToggle(object sender, RoutedEventArgs e)
        {
            _pinnedExpanded = !_pinnedExpanded;
            UpdatePinnedExpandState();
        }

        private void UpdatePinnedExpandState()
        {
            PinnedSummaryText.Visibility = _pinnedExpanded ? Visibility.Collapsed : Visibility.Visible;
            PinnedTextScroll.Visibility = _pinnedExpanded ? Visibility.Visible : Visibility.Collapsed;
            PinnedTextScroll.MaxHeight = PinnedExpandedHeight;
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
            ChatMessages.RefreshFilter();
            ApplyChatViewMode();
        }

        private void OnNewChat(object sender, RoutedEventArgs e)
        {
            StartNewChat();
        }

        private static ChatHistoryMessage ToHistoryMessage(ChatMessage message)
        {
            if (message.Payload is QuestionPayload question)
            {
                return new ChatHistoryMessage
                {
                    Kind = message.Kind,
                    Text = question.Question ?? "",
                    ToolName = question.Answer ?? "",
                    Options = question.Options?.ToList() ?? new List<string>(),
                    Reasoning = message.Reasoning,
                    Mode = message.Mode
                };
            }

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
            if (m.Kind == ChatMessageKind.Question)
            {
                return new ChatMessage
                {
                    Kind = m.Kind,
                    Payload = new QuestionPayload
                    {
                        Question = m.Text ?? "",
                        Answer = m.ToolName ?? "",
                        IsSkipped = string.Equals(m.ToolName, SkipAnswerText, StringComparison.Ordinal),
                        Options = m.Options ?? new List<string>()
                    }
                };
            }

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
            var selected = ModeToggle?.SelectedValue?.ToString();
            _mode = selected == "Act" ? AgentMode.Act
                  : selected == "Help" ? AgentMode.Help
                  : AgentMode.Plan;
            ApplyModeBrush();
            if (_restoringUiState)
                return;
            AppendModeSwitch("Mode: " + _mode);
            PersistUiState();
        }

        private void ApplyModeBrush()
        {
            if (InputBox == null)
                return;
            var key = _mode == AgentMode.Act ? "Mode.ActBrush"
                    : _mode == AgentMode.Help ? "Mode.HelpBrush"
                    : "Mode.PlanBrush";
            InputBox.BorderBrush = (Brush)FindResource(key);
        }

        private void OnOpenConverterToolWindow(object sender, RoutedEventArgs e)
        {
            ConverterToolWindowCommand.Instance?.ShowWindow();
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

        private void OnExportDocumentation(object sender, RoutedEventArgs e)
        {
            ExportDirectory(HelpDocumentation.ResolveDocsDirectory(), "Docs", "Documentation");
        }

        private void OnExportPrompts(object sender, RoutedEventArgs e)
        {
            ExportDirectory(PromptPaths.ResolvePromptsDirectory(), "Prompts", "Prompts");
        }

        private void ExportDirectory(string sourceDir, string targetSubFolder, string label)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                LogService.Warn(label + " directory not found.");
                return;
            }

            var initial = GetOpenSolutionDirectory() ?? string.Empty;
            var targetRoot = FolderPicker.PickFolder("Select a folder to export " + label + " to", initial, GetOwner());
            if (string.IsNullOrWhiteSpace(targetRoot))
                return;

            try
            {
                var targetDir = Path.Combine(targetRoot, targetSubFolder);
                CopyDirectory(sourceDir, targetDir);
                var message = label + " exported to " + targetDir;
                ShowSettingsToast(message);
                LogService.Info(message);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to export " + label + ".");
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = dir.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dest = Path.Combine(targetDir, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(file, dest, true);
            }
        }

        private IntPtr GetOwner()
        {
            var window = System.Windows.Window.GetWindow(this);
            return window == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }

        private const double SettingsToastMaxHeight = 160.0;

        private void ShowSettingsToast(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowSettingsToast(text)));
                return;
            }
            SettingsToast.Kind = NotificationKind.Success;
            SettingsToast.Message = text ?? "";
            SettingsToast.Visibility = Visibility.Visible;
            _settingsToastTimer.Stop();
            _settingsToastTimer.Start();
            SlideSettingsToast(0, SettingsToastMaxHeight);
        }

        private void HideSettingsToast()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideSettingsToast));
                return;
            }
            _settingsToastTimer.Stop();
            SlideSettingsToast(SettingsToast.ActualHeight, 0,
                () => SettingsToast.Visibility = Visibility.Collapsed);
        }

        private void OnSettingsToastTick(object? sender, EventArgs e)
        {
            HideSettingsToast();
        }

        private void OnSettingsToastClose(object sender, RoutedEventArgs e)
        {
            HideSettingsToast();
        }

        private void SlideSettingsToast(double from, double to, Action? onCompleted = null)
        {
            SettingsToast.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            if (onCompleted != null)
                animation.Completed += (_, __) => onCompleted();
            SettingsToast.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
        }

        private const double DeployToastMaxHeight = 120.0;

        private void ShowDeployToast(string text, bool success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowDeployToast(text, success)));
                return;
            }
            DeployToast.Kind = success ? NotificationKind.Success : NotificationKind.Warning;
            DeployToast.Message = text ?? "";
            DeployToast.Visibility = Visibility.Visible;
            _deployToastTimer.Stop();
            _deployToastTimer.Start();
            SlideDeployToast(0, DeployToastMaxHeight);
        }

        private void HideDeployToast()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideDeployToast));
                return;
            }
            _deployToastTimer.Stop();
            SlideDeployToast(DeployToast.ActualHeight, 0,
                () => DeployToast.Visibility = Visibility.Collapsed);
        }

        private void OnDeployToastTick(object? sender, EventArgs e)
        {
            HideDeployToast();
        }

        private void OnDeployToastClose(object sender, RoutedEventArgs e)
        {
            HideDeployToast();
        }

        private void ShowVoiceError(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowVoiceError(text)));
                return;
            }

            VoiceErrorToast.Kind = NotificationKind.Error;
            VoiceErrorToast.Message = text ?? "";
            VoiceErrorToast.Visibility = Visibility.Visible;
            // No auto-hide: the error banner stays until the user closes it.
        }

        private void OnVoiceErrorClose(object sender, RoutedEventArgs e)
        {
            VoiceErrorToast.Visibility = Visibility.Collapsed;
        }

        private void ShowCheckpointRestoreError(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCheckpointRestoreError(text)));
                return;
            }

            CheckpointRestoreErrorToast.Kind = NotificationKind.Error;
            CheckpointRestoreErrorToast.Message = text ?? "";
            CheckpointRestoreErrorToast.Visibility = Visibility.Visible;
            // No auto-hide: the error banner stays until the user closes it.
        }

        private void OnCheckpointRestoreErrorClose(object sender, RoutedEventArgs e)
        {
            CheckpointRestoreErrorToast.Visibility = Visibility.Collapsed;
        }

        private void SlideDeployToast(double from, double to, Action? onCompleted = null)
        {
            DeployToast.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            if (onCompleted != null)
                animation.Completed += (_, __) => onCompleted();
            DeployToast.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.Temperature = Math.Round(TemperatureSlider.Value, 1);
            _settings.KnowledgeBasePath = KbPathBox.Text.Trim();
            _settings.RobotsRootPath = RobotsRootPathBox.Text.Trim();
            _settings.RobotTemplateName = RobotTemplateNameBox.Text.Trim();
            _settings.ShowRobotCreator = ShowRobotCreatorCheckBox?.IsChecked ?? true;
            _settings.PackagesPath = PackagesPathBox.Text.Trim();
            _settings.RulesPath = RulesPathBox.Text.Trim();
            _settings.SkillsPath = SkillsPathBox.Text.Trim();
            _settings.ContextWindowSize = ParseContextWindowSize();
            _settings.CompactThreshold = ParseFraction(CompactThresholdBox?.Text, 0.85, 0.0, 1.0);
            _settings.ReserveFraction = ParseFraction(ReserveFractionBox?.Text, 0.12, 0.0, 0.5);
            _settings.KeepRecentMessages = ParsePositiveInt(KeepRecentBox?.Text, 6);
            _settings.ToolResultMaxChars = ParsePositiveInt(ToolResultMaxCharsBox?.Text, 12000);
            _settings.MaxIterations = ParsePositiveInt(MaxIterationsBox?.Text, 8);
            _settings.AutoCompact = AutoCompactCheckBox?.IsChecked ?? true;
            _settings.UseToolRole = UseToolRoleCheckBox?.IsChecked ?? true;
            _settings.ToolCallingMode = (ToolCallingModeCombo?.SelectedIndex ?? 0) == 1 ? "Native" : "JsonChat";
            _settings.MemoryBankMode = MemoryBankIndexToMode(MemoryBankModeCombo?.SelectedIndex ?? 0);
            _settings.SecurityAuditMode = (SecurityAuditModeCombo?.SelectedIndex ?? 0) == 1 ? "CredentialsOnly" : "Full";
            _settings.LogFilePath = LogFilePathBox.Text.Trim();
            _settings.ChatLogFilePath = ChatLogFilePathBox.Text.Trim();
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
            _settings.UserName = UserNameBox.Text.Trim();
            _settings.AddressByName = AddressByNameCheckBox?.IsChecked ?? false;
            _settings.AllowSubagents = AllowSubagentsCheckBox?.IsChecked ?? false;
            _settings.CheckpointsEnabled = CheckpointsEnabledCheckBox?.IsChecked ?? true;
            _settings.ShowControls = ShowControlsCheckBox?.IsChecked ?? false;
            _settings.SubagentMaxIterations = ParsePositiveInt(SubagentMaxIterationsBox?.Text, 4);
            _settings.SubagentMaxDepth = ParsePositiveInt(SubagentMaxDepthBox?.Text, 2);
            _settings.SpeechToTextProvider = SpeechToTextProviderIndexToName(SpeechToTextProviderCombo?.SelectedIndex ?? 0);
            _settings.SpeechToTextModel = (SpeechToTextModelBox?.Text ?? "").Trim();
            _settings.SpeechToTextLanguage = (SpeechToTextLanguageBox?.Text ?? "").Trim();
            _settings.VoskModelPath = (VoskModelPathBox?.Text ?? "").Trim();
            _settings.SpeechAutoSend = SpeechAutoSendCheckBox?.IsChecked ?? false;
            _settings.AutoCaptureRuntimeExceptions = AutoCaptureRuntimeExceptionsCheckBox?.IsChecked ?? true;
            SettingsStore.Save(_settings);
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            RecreateAiProvider();
            _stt = SpeechToTextFactory.Create(_settings);
            RebuildAgentTools();
            ApplyControlsVisibility();
            UpdateCreateRobotVisibility();
            ShowSettingsToast("Settings saved. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
        }

        private void ApplyControlsVisibility()
        {
            if (ControlsTab == null)
                return;
            ControlsTab.Visibility = (_settings != null && _settings.ShowControls)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>Fills the hidden "Controls" gallery tab with sample data for every chat message type and svg icon.</summary>
        private void PopulateControlsGallery()
        {
            ControlsChatList.ItemsSource = CreateSampleChatMessages();
            ControlsMessagesList.ItemsSource = _messageSamples;
            RebuildMessageSamples(ParseNotificationAutoHideSeconds());

            _svgView = CollectionViewSource.GetDefaultView(CreateSvgSamples());
            _svgView.Filter = FilterSvgIcon;
            ControlsSvgList.ItemsSource = _svgView;

            var palettes = new ListCollectionView(CreatePaletteSamples());
            palettes.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PaletteSample.Group)));
            ControlsPaletteList.ItemsSource = palettes;
        }

        /// <summary>Rebuilds the notification gallery samples with the given auto-hide duration and resets the countdown.</summary>
        private void RebuildMessageSamples(int autoHideSeconds)
        {
            _notificationAutoHideTimer.Stop();
            _messageSamples.Clear();
            foreach (var sample in CreateMessageSamples(autoHideSeconds))
                _messageSamples.Add(sample);
        }

        /// <summary>Restarts the gallery auto-hide demo; starts the timer only when at least one sample has a positive duration.</summary>
        private void StartNotificationAutoHideDemo()
        {
            _notificationAutoHideTimer.Stop();
            if (_messageSamples.Any(s => s.AutoHideSeconds > 0))
                _notificationAutoHideTimer.Start();
        }

        private int ParseNotificationAutoHideSeconds()
        {
            if (int.TryParse((NotificationAutoHideBox?.Text ?? string.Empty).Trim(), out var value) && value >= 0)
                return value;
            return 5;
        }

        private void ReplayNotificationSamples()
        {
            RebuildMessageSamples(ParseNotificationAutoHideSeconds());
            StartNotificationAutoHideDemo();
        }

        private void OnReplayNotifications(object sender, RoutedEventArgs e)
        {
            ReplayNotificationSamples();
        }

        private void OnNotificationAutoHideTick(object? sender, EventArgs e)
        {
            bool anyPending = false;
            for (int i = _messageSamples.Count - 1; i >= 0; i--)
            {
                var sample = _messageSamples[i];
                if (sample.AutoHideSeconds <= 0)
                    continue;

                sample.RemainingSeconds--;
                if (sample.RemainingSeconds <= 0)
                    _messageSamples.RemoveAt(i);
                else
                    anyPending = true;
            }

            if (!anyPending)
                _notificationAutoHideTimer.Stop();
        }

        private void OnControlsInnerTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only replay when the user is actually looking at the Controls -> Notifications
            // gallery, so the countdown does not start while the tab is hidden at startup.
            if (IsNotificationGalleryVisible())
                ReplayNotificationSamples();
        }

        private bool IsNotificationGalleryVisible()
        {
            return ControlsTab != null
                && ControlsTab.Visibility == Visibility.Visible
                && MainTabs != null
                && ReferenceEquals(MainTabs.SelectedItem, ControlsTab)
                && ControlsInnerTabs != null
                && NotificationsTab != null
                && ReferenceEquals(ControlsInnerTabs.SelectedItem, NotificationsTab);
        }

        private static List<SvgIconSample> CreateSvgSamples()
        {
            var samples = new List<SvgIconSample>();
            foreach (MaterialIconKind kind in Enum.GetValues(typeof(MaterialIconKind)))
                samples.Add(new SvgIconSample { Kind = kind });
            return samples;
        }

        private static List<PaletteSample> CreatePaletteSamples()
        {
            return new List<PaletteSample>
            {
                new PaletteSample { Group = "Mode", Name = "Plan", Hex = "#64B5F6", Brush = Rgb(0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Mode", Name = "Act", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Mode", Name = "Help", Hex = "#F44336", Brush = Rgb(0xF4, 0x43, 0x36) },

                new PaletteSample { Group = "Buttons", Name = "Button background", Hex = "#333337", Brush = Rgb(0x33, 0x33, 0x37) },
                new PaletteSample { Group = "Buttons", Name = "Button border", Hex = "#3F3F46", Brush = Rgb(0x3F, 0x3F, 0x46) },
                new PaletteSample { Group = "Buttons", Name = "Button hover", Hex = "#3E3E42", Brush = Rgb(0x3E, 0x3E, 0x42) },
                new PaletteSample { Group = "Buttons", Name = "Button pressed", Hex = "#264F78", Brush = Rgb(0x26, 0x4F, 0x78) },
                new PaletteSample { Group = "Buttons", Name = "Primary / accent", Hex = "#264F78", Brush = Rgb(0x26, 0x4F, 0x78) },
                new PaletteSample { Group = "Buttons", Name = "Icon button", Hex = "Transparent", Brush = Brushes.Transparent },

                new PaletteSample { Group = "Theme", Name = "Window", Hex = "#2D2D30", Brush = Rgb(0x2D, 0x2D, 0x30) },
                new PaletteSample { Group = "Theme", Name = "Panel", Hex = "#252526", Brush = Rgb(0x25, 0x25, 0x26) },
                new PaletteSample { Group = "Theme", Name = "Control", Hex = "#333337", Brush = Rgb(0x33, 0x33, 0x37) },
                new PaletteSample { Group = "Theme", Name = "Border", Hex = "#3F3F46", Brush = Rgb(0x3F, 0x3F, 0x46) },
                new PaletteSample { Group = "Theme", Name = "Text", Hex = "#F1F1F1", Brush = Rgb(0xF1, 0xF1, 0xF1) },
                new PaletteSample { Group = "Theme", Name = "Error text", Hex = "#F48771", Brush = Rgb(0xF4, 0x87, 0x71) },

                new PaletteSample { Group = "Chat", Name = "User border", Hex = "#CC64B5F6", Brush = Argb(0xCC, 0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Chat", Name = "User background", Hex = "#3364B5F6", Brush = Argb(0x33, 0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Chat", Name = "Last act border", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Chat", Name = "Last act background", Hex = "#334CAF50", Brush = Argb(0x33, 0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Chat", Name = "Last plan border", Hex = "#FF8C00", Brush = Rgb(0xFF, 0x8C, 0x00) },
                new PaletteSample { Group = "Chat", Name = "Last plan background", Hex = "#4DFF8C00", Brush = Argb(0x4D, 0xFF, 0x8C, 0x00) },

                new PaletteSample { Group = "Notifications", Name = "Trace", Hex = "#FFFFFF", Brush = Brushes.White },
                new PaletteSample { Group = "Notifications", Name = "Info", Hex = "#64B5F6", Brush = Rgb(0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Notifications", Name = "Success", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Notifications", Name = "Warning", Hex = "#FF8C00", Brush = Rgb(0xFF, 0x8C, 0x00) },
                new PaletteSample { Group = "Notifications", Name = "Error", Hex = "#F44336", Brush = Rgb(0xF4, 0x43, 0x36) },
            };
        }

        private bool FilterSvgIcon(object item)
        {
            var query = (SvgSearchBox?.Text ?? string.Empty).Trim();
            if (query.Length == 0)
                return true;

            return item is SvgIconSample sample &&
                   sample.Identifier.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSvgSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _svgView?.Refresh();
        }

        private void OnCopySvgIdentifier(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SvgIconSample sample })
            {
                try { Clipboard.SetText(sample.Identifier); }
                catch { /* clipboard busy — ignore */ }
            }
        }

        private void OnSendSvgIdentifier(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SvgIconSample sample })
                AppendSelectionToInput(sample.Identifier);
        }

        private static List<ChatMessage> CreateSampleChatMessages()
        {
            return new List<ChatMessage>
            {
                new ChatMessage { Kind = ChatMessageKind.User, Text = "Import the spec and generate a robot for SAP transaction MM01." },
                new ChatMessage
                {
                    Kind = ChatMessageKind.AgentText,
                    Text = "Plan:\n\n- Read the specification\n- Design the workflow\n- Generate the XAML and C# helpers\n\n```csharp\nvar robot = new Robot();\n```",
                    Reasoning = "The user wants a robot generated from an imported spec. I will decompose it into workflow steps first."
                },
                new ChatMessage { Kind = ChatMessageKind.ToolCall, Payload = new ToolCallPayload { ToolName = "read_file", Result = "FILE: spec.docx (ok, 2340 chars)" } },
                new ChatMessage { Kind = ChatMessageKind.ToolBlocked, Payload = new ToolCallPayload { ToolName = "generate_robot" } },
                new ChatMessage { Kind = ChatMessageKind.Error, Text = "Failed to reach the AI provider: request timed out after 300 s." },
                new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "Knowledge base directory is not configured." },
                new ChatMessage { Kind = ChatMessageKind.Compaction, Text = "Conversation compacted: 12 earlier messages summarized." },
                new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Text = "Switched from Plan to Act mode." },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Question,
                    Payload = new QuestionPayload
                    {
                        Question = "Which robot template should I use?",
                        Options = new List<string> { "SAP", "Web", "Excel" }
                    }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Subagent,
                    Payload = new SubagentPayload
                    {
                        Item = new SubagentStatusItem
                        {
                            Prompt = "Parse the UiPath XAML and reconstruct the specification.",
                            Status = "completed",
                            ToolCalls = 3,
                            LatestToolCall = "read_file",
                            Result = "Reconstructed 5 tasks."
                        }
                    }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerPlan,
                    Mode = "Plan",
                    Text = "## Plan\n\n- Read the specification\n- Design the workflow\n- Generate the XAML and C# helpers\n\n```csharp\nvar robot = new Robot();\n```"
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerAct,
                    Mode = "Act",
                    Text = "## Done\n\nCreated the robot project and generated the XAML files."
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.DiffEdit,
                    Text = "--- a/Robot.xaml\n+++ b/Robot.xaml\n@@ -1,3 +1,3 @@\n- <Sequence>\n+ <Sequence DisplayName=\"Main\">"
                }
            };
        }

        private void ShowPendingOptionsWarnings()
        {
            var warnings = VsOptionsService.LastWarnings;
            if (warnings == null || warnings.Count == 0)
                return;

            ShowOptionsWarning(string.Join(Environment.NewLine, warnings));
        }

        private void ShowOptionsWarning(string message)
        {
            if (OptionsWarningToast == null)
                return;

            OptionsWarningToast.Kind = NotificationKind.Warning;
            OptionsWarningToast.Message = message ?? string.Empty;
            OptionsWarningToast.Visibility = Visibility.Visible;
        }

        private void HideOptionsWarning()
        {
            if (OptionsWarningToast != null)
                OptionsWarningToast.Visibility = Visibility.Collapsed;
        }

        private void OnOptionsWarningClose(object sender, RoutedEventArgs e)
        {
            HideOptionsWarning();
        }


        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            OpenLogFile(LogService.CurrentLogFilePath, "Log file path is not available.", "log");
        }

        private void OnOpenChatLog(object sender, RoutedEventArgs e)
        {
            OpenLogFile(LogService.CurrentChatLogFilePath, "Chat log file path is not available.", "chat log");
        }

        private void OpenLogFile(string logFile, string unavailableMessage, string logKind)
        {
            if (string.IsNullOrWhiteSpace(logFile))
            {
                AppendExtensionError(unavailableMessage);
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
                AppendExtensionError("Failed to open " + logKind + ": " + ex.Message);
                LogService.Error(ex, "Failed to open " + logKind + " file.");
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
        private static List<MessageSample> CreateMessageSamples(int autoHideSeconds)
        {
            var blue = Rgb(0x64, 0xB5, 0xF6);
            var green = Rgb(0x4C, 0xAF, 0x50);
            var orange = Rgb(0xFF, 0x8C, 0x00);
            var red = Rgb(0xF4, 0x43, 0x36);
            var samples = new List<MessageSample>();
            AddMessageSamples(samples, "trace", Brushes.White, Brushes.White, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "info", blue, blue, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "success", green, green, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "warning", orange, orange, new Thickness(1), autoHideSeconds, showSend: true);
            AddMessageSamples(samples, "error", red, red, new Thickness(1), autoHideSeconds, showSend: true);
            return samples;
        }

        private static void AddMessageSamples(List<MessageSample> samples, string colorName, Brush accent, Brush foreground, Thickness borderThickness, int autoHideSeconds, bool showSend)
        {
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-copy-close", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = true, ShowSend = showSend, ShowClose = true, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-copy", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = true, ShowSend = showSend, ShowClose = false, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-close", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = false, ShowSend = showSend, ShowClose = true, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName, Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = false, ShowSend = showSend, ShowClose = false, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
        }
        private static SolidColorBrush Rgb(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush Argb(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
    }
    /// <summary>Demo item for the Controls -> Notifications gallery.</summary>
    public sealed class MessageSample
    {
        public string Text { get; set; }
        public Brush Accent { get; set; }
        public Brush Foreground { get; set; }
        public Thickness BorderThickness { get; set; } = new Thickness(1);
        public bool ShowClose { get; set; }
        public bool ShowCopy { get; set; }
        public bool ShowSend { get; set; }

        /// <summary>Seconds after which the demo notification disappears; 0 means it never disappears.</summary>
        public int AutoHideSeconds { get; set; }

        /// <summary>Internal countdown for the auto-hide demo (not bound to UI).</summary>
        public int RemainingSeconds { get; set; }
    }

    /// <summary>Demo item for the Controls -> svg gallery.</summary>
    public sealed class SvgIconSample
    {
        public MaterialIconKind Kind { get; set; }

        public string Identifier => Kind.ToString();
    }

    /// <summary>Demo item for the Controls -> palette gallery.</summary>
    public sealed class PaletteSample
    {
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
        public Brush Brush { get; set; } = Brushes.Transparent;
    }
}
