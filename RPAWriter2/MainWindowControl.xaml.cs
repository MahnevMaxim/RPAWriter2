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
using Agent.Core.AgentTools;
using Agent.Core.Context;
using Agent.Core.Contracts;
using Agent.Core.Services;
using Agent.Core.Mcp;
using Agent.Core.Checkpoints;
using RPAWriter2.Services;
using Agent.Models;
using Agent.Mvvm;
using RPAWriter2.ViewModels;
using RPAWriter2.Mvvm;
using RPAWriter2.Services.AgentTools;
using RPAWriter2.Services.Checkpoints;
using RPAWriter2.Controls;
using Agent.Toolkit.Wpf.Controls;
using Agent.Toolkit.Wpf.Markdown;
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
using Agent.Core.Models;
using Material.Icons;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2
{
    /// <summary>
    /// Single extension window: agent chat and settings.
    /// </summary>
    public partial class MainWindowControl : UserControl
    {
        private SettingsData _settings;
        private RpaSettingsModel _rpaSettingsModel;
        private IRpaAiProvider _ai;

        /// <summary>Live snapshot of the RPA-domain settings, rebuilt on each access.</summary>
        private RpaSettingsData _rpaSettings => _rpaSettingsModel.ToSettingsData();
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
        private RoslynWorkspaceService _roslyn;
        private OutputWindowService _outputWindow;
        private readonly GitService _git = new GitService();
        private readonly CheckpointServiceFactory _checkpoints = new CheckpointServiceFactory();
        private readonly ChatModel _chatModel;
        private readonly ChatViewModel _chatViewModel = new ChatViewModel();
        private readonly SettingsModel _settingsModel;
        private readonly SettingsViewModel _settingsViewModel = new SettingsViewModel();
        private readonly SpecTabModel _specTabModel;
        private readonly SpecTabViewModel _specTabViewModel = new SpecTabViewModel();
        private readonly KnowledgeBaseModel _knowledgeBaseModel;
        private readonly KnowledgeBaseViewModel _knowledgeBaseViewModel = new KnowledgeBaseViewModel();
        private readonly VsWorkspaceProvider _workspaceProvider = new VsWorkspaceProvider();

        /// <summary>
        /// Last non-empty workspace id the provider reported. Kept so a transient <c>null</c> while a
        /// solution is reloading does not switch the storages to the global scope and tear the active
        /// conversation apart.
        /// </summary>
        private string _lastWorkspaceId = "";

        // Mode and phase are read from _chatModel directly: the footer controls bind to them, so
        // there is nothing left for the host to mirror (documentation/13-model-layer.md, phase E).
        private AgentToolRegistry? _toolRegistry;
        private AgentToolFactory? _toolFactory;
        private AgentRunner? _agentRunner;
        private readonly List<McpClient> _mcpClients = new List<McpClient>();
        private IReadOnlyList<IAgentTool> _mcpAdapters = new List<IAgentTool>();
        private AgentResumeSnapshot? _resumableSnapshot;
        // Canonical provider transcript of the last completed turn (Cline "apiConversationHistory"
        // equivalent). It is the source for the next RunAsync, not the visible Messages collection.
        private List<ChatMessageData> _providerTranscript = new List<ChatMessageData>();
        private AgentMode _resumableMode;
        private ContextBudget? _resumableBudget;
        private readonly ContextUsageBinder _contextUsageBinder;
        private int _compactionDepth;
        private IMessageNotifier? _messageNotifier;
        private IRetryNotifier? _retryNotifier;
        private IPromptNotifier? _promptNotifier;
        private DTE2? _dte;
        private MicrophoneRecorder _mic;
        private ISpeechToTextService _stt;
        private bool _isRecording;
        private bool _isTranscribing;
        // Chat state is owned by the model; the host keeps these aliases so the existing control
        // code keeps working while the bindings move over (phase E) and the tests cover the model.
        private ObservableCollection<ChatMessage> _messages => _chatModel.Messages;
        private ObservableCollection<PendingMessage> _pendingMessages => _chatModel.Pending;
        private readonly PackageCatalogService _packageCatalog = new PackageCatalogService();
        private readonly ObservableCollection<PackageListItem> _packages = new ObservableCollection<PackageListItem>();
        private ObservableCollection<ChatAttachment> _attachments => _chatModel.Attachments;

        private CancellationTokenSource? _chatCts;

        /// <summary>A steered message captured atomically so the consumed slot can be cleared on the UI thread.</summary>
        private sealed class SteerSlot
        {
            public readonly PendingMessage Message;
            public readonly string Text;
            public SteerSlot(PendingMessage message, string text) { Message = message; Text = text; }
        }

        // Single-slot steer state for the running turn. At most one steered message is in flight;
        // the message stays queued until the agent loop consumes it (TakeSteer), so a steer that
        // arrives right before the turn ends is not lost — the drain loop runs it normally.
        private volatile SteerSlot _steerSlot;
        private TaskCompletionSource<string>? _questionTcs;
        private ChatMessage? _activeQuestion;
        private TaskCompletionSource<ToolApprovalDecision>? _approvalTcs;
        private ChatMessage? _activeApproval;
        private bool _approveAllInRun;
        private bool _denyAllInRun;
        private readonly DispatcherTimer _settingsToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private NotificationBanner _settingsToast = null!;
        private TextBlock _workspaceIndicatorText = null!;
        private Button _resetWorkspaceButton = null!;
        private readonly DispatcherTimer _deployToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _workspacePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        // Domain tab bodies created in code (stage 5): the XAML shell only hosts the
        // AgentTabControl; the toolkit owns Chat/Settings, the host registers the rest.
        private readonly SpecTabControl SpecPanel = new SpecTabControl();
        private readonly KnowledgeBaseControl KnowledgeBasePanel = new KnowledgeBaseControl();
        private readonly PackagesControl PackagesPanel = new PackagesControl();
        private readonly ControlsGalleryControl ControlsPanel = new ControlsGalleryControl();
        private TabItem ControlsTab = null!;

        // The toolkit AgentTabControl owns Chat/Settings; these aliases keep the existing
        // WireChatPanel()/settings wiring code unchanged.
        private ChatControl ChatPanel => MainTabs.ChatPanel;
        private SettingsControl SettingsPanel => MainTabs.SettingsPanel;

        private static string CommitMessageSystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-message.txt", RpaPromptDefaults.CommitMessageSystemPrompt);

        private static string CommitRobotSummarySystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-robot-summary.txt", RpaPromptDefaults.CommitRobotSummarySystemPrompt);

        private static string CommitOtherSummarySystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-other-summary.txt", RpaPromptDefaults.CommitOtherSummarySystemPrompt);

        private const int CommitDiffMaxChars = 8000;
        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;
        private ObservableCollection<ChatConversation> _conversations => _chatModel.History;

        /// <summary>
        /// Pass-through alias of <see cref="ChatModel.ActiveChatConversation"/>: the model stays the
        /// only source of "which chat is active", so do not add local fields that remember the current
        /// conversation.
        /// </summary>
        private ChatConversation _currentConversation
        {
            get => _chatModel.ActiveChatConversation;
            set => _chatModel.ActiveChatConversation = value;
        }

        private readonly DispatcherTimer _historySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };

        private ChatViewMode _chatViewMode
        {
            get => _chatModel.ViewMode;
            set => _chatModel.ViewMode = value;
        }
        // Starts true so XAML load events (e.g. ModeToggle ValueChanged) don't
        // overwrite persisted state before RestoreUiState() runs.
        private bool _restoringUiState = true;
        private ChatMessage? _pinnedMessage => _chatModel.PinnedMessage;
        private bool _pinnedExpanded;
        // Guards against Checked/Unchecked events firing while the permissions
        // overlay is being populated from settings (which would otherwise save
        // half-initialized checkbox states back over the real settings).
        private bool _suppressPermissionEvents;

        private bool _baselineCheckpointCreated;
        private string? _lastCheckpointHash;
        private int _iterationNumber;

        public MainWindowControl()
        {
            ApplyWorkspaceScope();

            _chatModel = new ChatModel(
                new SqliteChatHistoryStore(),
                new JsonUiStateStore(),
                chatSink: new ChatPromptSinkAdapter((prompt, _) => SendChatTextAsync(prompt)),
                workspaceProvider: _workspaceProvider,
                logger: new NLogAgentLogger());
            _contextUsageBinder = new ContextUsageBinder(_chatModel, SynchronizationContext.Current);
            // The geometry parser must be ready before InitializeComponent: the
            // ClearInputButton icon (MaterialIconKind.Broom) is resolved via a binding
            // during XAML load and would otherwise fall back to an empty geometry.
            MaterialIconDataProvider.InitializeGeometryParser(s => Geometry.Parse(s));
            this.InitializeComponent();

            // Register the host-owned domain tabs (stage 5). Chat/Settings are built in by
            // AgentTabControl from CoreTabs; the Controls tab starts hidden like before.
            MainTabs.AddHostTab(new TabDefinition("Spec", "Spec", "Spec", "FileDocumentOutline", "FileDocument"), SpecPanel);
            MainTabs.AddHostTab(new TabDefinition("KnowledgeBase", "Knowledge base", "Knowledge base", "DatabaseOutline", "Database"), KnowledgeBasePanel);
            MainTabs.AddHostTab(new TabDefinition("Packages", "Packages", "Packages", "PackageOutline", "Package"), PackagesPanel);
            ControlsTab = MainTabs.AddHostTab(new TabDefinition("Controls", "Controls", "Controls", "PaletteOutline", "Palette"), ControlsPanel);
            ControlsTab.Visibility = Visibility.Collapsed;

            _settingsToastTimer.Tick += OnSettingsToastTick;
            _deployToastTimer.Tick += OnDeployToastTick;
            MainTabs.SelectionChanged += OnMainTabsSelectionChanged;
            SpecPanel.SpecMessages.CopyMessageRequested += OnChatMessageCopyRequested;
            PackagesPanel.BindPackages(_packages);
            PackagesPanel.RefreshRequested += OnRefreshPackages;
            PackagesPanel.InstallRequested += OnInstallSelectedPackages;
            PackagesPanel.UpdateRequested += OnUpdateSelectedPackages;
            PackagesPanel.UninstallRequested += OnUninstallSelectedPackages;
            PackagesPanel.RestoreRequested += OnRestorePackages;
            WireSpecPanel();
            WireControlsGallery();
            _settingsModel = new SettingsModel(
                new JsonSettingsStore(),
                new JsonWorkspaceLogSettingsStore(),
                _workspaceProvider,
                new DpapiSecretsStore(),
                () => _ai,
                new VsFolderPicker(GetOwner),
                new VsSolutionPathProvider(),
                new VsFileWriter(),
                new VsFileOpener(),
                () => LogService.CurrentLogFilePath,
                () => LogService.CurrentChatLogFilePath);
            _settingsModel.SettingsSaved += OnSettingsModelSaved;
            _settingsModel.Notify += OnSettingsNotify;
            _rpaSettingsModel = new RpaSettingsModel(new RpaSettingsStore());
            _rpaSettingsModel.Run();
            _settingsViewModel.RpaSettings = _rpaSettingsModel;
            MvvmFactory.Attach(_settingsModel, _settingsViewModel, SettingsPanel);
            _settings = _settingsModel.ToSettingsData();
            _chatModel.CacheLossDropRatioPercent = _settings.CacheLossDropRatioPercent;
            SettingsPanel.ApiKeyBox.Password = _settings.ApiKey ?? "";
            SettingsPanel.ApiKeyBox.PasswordChanged += OnApiKeyChanged;
            WireSettingsHostChrome();

            _specTabModel = new SpecTabModel(
                () => _ai,
                new VsSolutionPathProvider(),
                new VsFilePicker(),
                new VsFolderPicker(GetOwner),
                new VsFileWriter(),
                new MessageBoxFileOverwritePrompter(),
                Confirm,
                new VsDocumentOpener(),
                new OpenXmlDocumentReader(),
                new RobotGenerator(),
                kbPath => new FileKnowledgeBase(kbPath),
                () => _rpaSettings,
                SynchronizationContext.Current);
            _specTabModel.Notify += OnSpecTabNotify;
            MvvmFactory.Attach(_specTabModel, _specTabViewModel, SpecPanel);

            _knowledgeBaseModel = new KnowledgeBaseModel(
                () => _ai,
                () => _rpaSettings,
                kbPath => new FileKnowledgeBase(kbPath),
                new VsFileWriter(),
                SynchronizationContext.Current);
            _knowledgeBaseModel.Notify += OnKnowledgeBaseNotify;
            _knowledgeBaseModel.PropertyChanged += OnKnowledgeBaseModelPropertyChanged;
            MvvmFactory.Attach(_knowledgeBaseModel, _knowledgeBaseViewModel, KnowledgeBasePanel);

            SpecPanel.SpecMessages.Messages = _specTabModel.SpecMessages;

            ControlsPanel.PopulateGallery();
            ShowPendingOptionsWarnings();
            RecreateAiProvider();
            _mic = new MicrophoneRecorder();
            _stt = SpeechToTextFactory.Create(_settings);
            WireChatPanel();
            _buildFeedback = new BuildFeedbackService();
            _robotTemplate = new RobotTemplateService();
            _roslyn = new RoslynWorkspaceService();
            _outputWindow = new OutputWindowService();
            RebuildAgentToolsCore();
            RefreshMcpToolsAsync();
            HookSolutionEvents();
            _workspaceProvider.WorkspaceChanged += OnWorkspaceChanged;
            _workspaceProvider.Initialize();
            _workspacePollTimer.Tick += OnWorkspacePollTick;
            _workspacePollTimer.Start();
            UpdateCreateRobotVisibility();
            UpdateWorkspaceIndicator();
            _chatModel.ChildModel = CreateLogAnalyzer();
            _chatModel.Run();
            InitializeChatHistory();
        }

        private void WireSpecPanel()
        {
            SpecPanel.DeployRequested += OnDeploy;
            SpecPanel.AnalyzeLogsRequested += OnAnalyzeLogs;
            SpecPanel.CloseAnalyzeLogsRequested += OnCloseAnalyzeLogs;
            SpecPanel.AnalyzeLogsFromOutputWindowRequested += OnAnalyzeLogsFromOutputWindow;
            SpecPanel.AnalyzeLogsFromProdServersRequested += OnAnalyzeLogsFromProdServers;
            SpecPanel.LoadProdTasksRequested += OnLoadProdTasks;
            SpecPanel.AnalyzeProdLogsRequested += OnAnalyzeProdLogs;
            SpecPanel.NotificationSendToChatRequested += OnNotificationSendToChat;
            SpecPanel.DeployToastCloseRequested += OnDeployToastClose;
            SpecPanel.RobotNameEnterPressed += OnRobotNameEnterPressed;
            SpecPanel.TaskSelectionChanged += OnTaskSelected;
            SpecPanel.MinLevelSelectionChanged += OnMinLevelChanged;
            SpecPanel.DateRangeSelectionChanged += OnDateRangeChanged;
        }

        private void WireControlsGallery()
        {
            ControlsPanel.SendToInputRequested += (_, text) => AppendSelectionToInput(text);
        }

        private void OnRobotNameEnterPressed(object? sender, EventArgs e)
            => _ = LoadProdTasksFromModelAsync();

        /// <summary>Re-renders the layout parts that depend on chat model state.</summary>
        private void RecreateAiProvider()
        {
            _contextUsageBinder.Detach();
            UnhookMessageNotifier();
            UnhookRetryNotifier();
            UnhookPromptNotifier();
            _ai = RpaAiProviderFactory.Create(_settings, _rpaSettings);
            LogService.Info("AI provider recreated: " + (_ai == null ? "null" : _ai.GetType().Name));
            _contextUsageBinder.Attach(_ai as ITokenUsageProvider, BuildContextBudget());
            HookMessageNotifier();
            HookRetryNotifier();
            HookPromptNotifier();
        }

        private string KbPath()
        {
            return (_rpaSettings?.KnowledgeBasePath ?? "").Trim();
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

        /// <summary>
        /// Runs an async action on the WPF dispatcher thread and awaits its result. The agent loop
        /// invokes interactive flows (questions, tool approvals) from a background thread, and the
        /// tool window must touch its controls only on the dispatcher. Using the dispatcher directly
        /// (rather than the VS JoinableTaskFactory) keeps the switch reliable inside a tool window.
        /// </summary>
        private async Task<T> InvokeOnDispatcherAsync<T>(Func<Task<T>> action, CancellationToken ct)
        {
            if (Dispatcher.CheckAccess())
                return await action();

            return await Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct).Task.Unwrap();
        }

        /// <summary>
        /// Shows an interactive question in the chat (with answer buttons) and waits for the
        /// user's answer. Invoked by the ask_question tool handler, possibly on a background thread.
        /// </summary>
        private async Task<string> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        {
            return await InvokeOnDispatcherAsync(async () =>
            {
                var alreadyActive = _questionTcs != null;
                var message = new ChatMessage
                {
                    Kind = ChatMessageKind.Question,
                    Payload = new QuestionPayload { Question = question ?? "", Options = options ?? new List<string>() }
                };
                _activeQuestion = message;
                AppendChatMessage(message);
                _chatModel.PauseStatus();

                LogService.ChatInfo("Question shown: \"" + (question ?? "").Replace("\r", " ").Replace("\n", " ") + "\" (options=" + (options?.Count ?? 0) + "); alreadyActive=" + alreadyActive + ".");

                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _questionTcs = tcs;
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    try
                    {
                        var answer = await tcs.Task;
                        _chatModel.ResumeStatus();
                        LogService.ChatInfo("Question completed: answer=\"" + (answer ?? "").Replace("\r", " ").Replace("\n", " ") + "\", canceled=" + tcs.Task.IsCanceled + ".");
                        return answer;
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
            }, ct);
        }

        /// <summary>
        /// Approval gate for the agent runner: decides whether a tool call may execute. When the
        /// user already chose "Approve all"/"Reject all" for this run it answers without UI;
        /// otherwise it shows the approval card and waits for the user's decision.
        /// Invoked by the runner on a background thread.
        /// </summary>
        private async Task<ToolApprovalDecision> ApproveToolAsync(ToolApprovalRequest request, CancellationToken ct)
        {
            if (_approveAllInRun)
                return ToolApprovalDecision.Approve;
            if (_denyAllInRun)
                return ToolApprovalDecision.Reject;

            try
            {
                LogService.ChatDebug("Tool approval gate entered for \"" + (request.ToolName ?? "") + "\".");

                return await InvokeOnDispatcherAsync(async () =>
                {
                    var summary = string.IsNullOrWhiteSpace(request.ArgumentsSummary) ? "" : request.ArgumentsSummary.Trim();
                    if (summary == "{}")
                        summary = "";

                    var message = new ChatMessage
                    {
                        Kind = ChatMessageKind.Approval,
                        Payload = new ApprovalPayload
                        {
                            ToolName = request.ToolName ?? "",
                            Description = summary
                        }
                    };
                    _activeApproval = message;
                    AppendChatMessage(message);
                    _chatModel.PauseStatus();

                    LogService.ChatInfo("Tool approval requested: \"" + (request.ToolName ?? "") + "\".");

                    var tcs = new TaskCompletionSource<ToolApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _approvalTcs = tcs;
                    using (ct.Register(() => tcs.TrySetCanceled()))
                    {
                        try
                        {
                            var decision = await tcs.Task;
                            _chatModel.ResumeStatus();
                            LogService.ChatInfo("Tool approval decision received: " + decision + " for \"" + (request.ToolName ?? "") + "\".");
                            return decision;
                        }
                        finally
                        {
                            _activeApproval = null;
                            _approvalTcs = null;
                        }
                    }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.ChatError("Tool approval failed for \"" + (request.ToolName ?? "") + "\": " + ex.Message);
                throw;
            }
        }

        private void OnChatMessageApprovalDecided(object? sender, ChatMessage message)
        {
            if (_approvalTcs == null)
            {
                LogService.ChatWarn("ApprovalDecided but _approvalTcs is null (decision may be lost); activeApproval=" + (_activeApproval != null) + ".");
                return;
            }
            if (message.Payload is not ApprovalPayload payload)
                return;

            var decision = payload.Decision == "rejected" ? ToolApprovalDecision.Reject : ToolApprovalDecision.Approve;
            if (payload.ApplyToAll)
            {
                if (decision == ToolApprovalDecision.Approve) _approveAllInRun = true;
                else _denyAllInRun = true;
            }

            LogService.ChatInfo("Tool approval decision: " + payload.Decision + (payload.ApplyToAll ? " (apply to all)" : "") + ".");
            ScheduleHistorySave();
            _approvalTcs.TrySetResult(decision);
        }

        private void OnChatMessageCopyRequested(object? sender, ChatMessage message)
        {
            try { Clipboard.SetText(FormatMessageForCopy(message)); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnChatMessageQuestionAnswered(object? sender, ChatMessage message)
        {
            var answer = (message.Payload as QuestionPayload)?.Answer ?? "";
            if (_questionTcs == null)
            {
                LogService.ChatWarn("QuestionAnswered but _questionTcs is null (answer may be lost): \"" + answer.Replace("\r", " ").Replace("\n", " ") + "\"; activeQuestion=" + (_activeQuestion != null) + ".");
                return;
            }
            LogService.ChatInfo("Question answered by user: \"" + answer.Replace("\r", " ").Replace("\n", " ") + "\".");
            ScheduleHistorySave();
            _questionTcs.TrySetResult(answer);
        }

        private void OnChatMessageQuestionSkipped(object? sender, ChatMessage message)
        {
            var answer = (message.Payload as QuestionPayload)?.Answer ?? ChatMessagesControl.SkipAnswerText;
            if (_questionTcs == null)
            {
                LogService.ChatWarn("QuestionSkipped but _questionTcs is null (answer may be lost); activeQuestion=" + (_activeQuestion != null) + ".");
                return;
            }
            LogService.ChatInfo("Question skipped by user.");
            ScheduleHistorySave();
            _questionTcs.TrySetResult(answer);
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
            // The chat control adds into the model-owned Messages collection; the model reacts by
            // updating the pinned message and the dirty flag.
            ChatPanel.AppendChatMessage(message);
            if (_chatModel.IsDirty)
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
        /// Snapshots the just-finished iteration and, when it changed files, attaches the
        /// iteration diff to the new checkpoint so its diff button can open it in the diff tool window.
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

                AttachDiffToCheckpoint(newHash!, files);
            }
            catch (Exception ex)
            {
                LogService.Warn("Iteration diff failed: " + ex.Message);
                _lastCheckpointHash = newHash;
            }
        }

        private void AttachDiffToCheckpoint(string commitHash, IReadOnlyList<DiffFileModel> files)
        {
            void Apply()
            {
                for (int i = _messages.Count - 1; i >= 0; i--)
                {
                    if (_messages[i].Kind == ChatMessageKind.Checkpoint
                        && _messages[i].Payload is CheckpointPayload checkpoint
                        && string.Equals(checkpoint.CommitHash, commitHash, StringComparison.OrdinalIgnoreCase))
                    {
                        checkpoint.Files = files ?? new List<DiffFileModel>();
                        checkpoint.HasDiff = files != null && files.Count > 0;
                        break;
                    }
                }
            }

            if (Dispatcher.CheckAccess())
                Apply();
            else
                Dispatcher.Invoke(Apply);
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

        private async void OnDiffOpenRequested(object? sender, ChatMessage message)
        {
            if (message.Payload is not CheckpointPayload payload) return;
            if (string.IsNullOrWhiteSpace(payload.CommitHash))
            {
                ShowNotice("This checkpoint has no commit to diff.", NotificationKind.Error);
                return;
            }

            try
            {
                var previous = FindPreviousCheckpoint(message);
                if (previous == null || string.IsNullOrWhiteSpace(previous.CommitHash))
                {
                    ShowNotice("No previous checkpoint to diff against.", NotificationKind.Error);
                    return;
                }

                var service = await GetCheckpointServiceAsync(payload).ConfigureAwait(true);
                var diffs = await Task.Run(() => service.GetDiffAsync(previous.CommitHash, payload.CommitHash)).ConfigureAwait(true);
                var files = CheckpointDiffBuilder.Build(diffs);
                DiffToolWindowCommand.Instance?.ShowWindow(files, "Diff " + payload.ShortHash);
            }
            catch (Exception ex)
            {
                LogService.Warn("Checkpoint diff failed: " + ex.Message);
                ShowNotice("Checkpoint diff failed: " + ex.Message, NotificationKind.Error);
            }
        }

        private CheckpointPayload? FindPreviousCheckpoint(ChatMessage message)
        {
            var index = _messages.IndexOf(message);
            for (var i = index - 1; i >= 0; i--)
            {
                if (_messages[i].Kind == ChatMessageKind.Checkpoint && _messages[i].Payload is CheckpointPayload previous)
                    return previous;
            }
            return null;
        }

        private async void OnCheckpointRestoreRequested(object? sender, ChatMessage message)
        {
            if (message.Payload is not CheckpointPayload payload) return;

            var result = MessageDialog.Show(
                "Restore checkpoint",
                "Restore files to checkpoint " + payload.ShortHash + "?",
                MessageDialogCommandSet.YesNo);
            if (result != MessageDialogCommand.Yes) return;

            // Stop any running agent turn and drop queued messages before rewinding the
            // workspace, so a live edit pass does not race with the restore.
            _pendingMessages.Clear();
            _chatCts?.Cancel();

            try
            {
                var service = await GetCheckpointServiceAsync(payload).ConfigureAwait(true);
                await Task.Run(() => service.RestoreAsync(payload.CommitHash)).ConfigureAwait(true);
                payload.HasChanges = false;

                // Checkpoints below the restored one are now stale: the workspace was
                // rewound to this snapshot, so their indicators no longer make sense.
                var index = _messages.IndexOf(message);
                if (index >= 0)
                {
                    for (int i = _messages.Count - 1; i > index; i--)
                    {
                        if (_messages[i].Kind == ChatMessageKind.Checkpoint)
                            _messages.RemoveAt(i);
                    }
                }

                // The next iteration diff must be computed from the restored checkpoint,
                // not from the now-orphaned snapshot that used to be HEAD.
                _lastCheckpointHash = payload.CommitHash;

                // Notify the agent that the workspace was rolled back, so the next turn
                // does not keep reasoning against a stale file snapshot.
                var rollbackNote = "User rolled back the workspace to checkpoint " + payload.ShortHash
                    + (string.IsNullOrWhiteSpace(payload.Message) ? "" : " (\"" + payload.Message + "\")")
                    + ". Files were restored to the state of that snapshot.";
                AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = rollbackNote });

                ScheduleHistorySave();
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
            var taskId = _currentConversation?.Id ?? payload.TaskId;
            var ws = string.IsNullOrWhiteSpace(payload.WorkspaceDir) ? GetOpenSolutionDirectory() : payload.WorkspaceDir;
            if (string.IsNullOrWhiteSpace(taskId))
                throw new InvalidOperationException("No conversation is open.");
            if (string.IsNullOrWhiteSpace(ws))
                throw new InvalidOperationException("No solution is open.");

            var service = _checkpoints.GetOrCreate(taskId, ws);
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

        private const int PromptDumpContentMaxChars = 2000;

        private static string TruncatePromptDump(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= PromptDumpContentMaxChars)
                return text ?? string.Empty;
            return text.Substring(0, PromptDumpContentMaxChars) + "\n…(truncated)";
        }

        private static string FormatPromptDump(PromptAssembledEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== FULL PROMPT ASSEMBLED =====");
            sb.AppendLine("Estimated input tokens: " + e.EstimatedInputTokens);
            sb.AppendLine("System prompt (" + (e.SystemPrompt == null ? 0 : e.SystemPrompt.Length) + " chars):");
            sb.AppendLine("-----");
            sb.AppendLine(TruncatePromptDump(e.SystemPrompt));
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
                sb.AppendLine(TruncatePromptDump(m.Content));
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

        private void ShowError(Exception ex)
        {
            AppendExtensionError(ex);
        }

        private void ShowNotice(string message, NotificationKind kind = NotificationKind.Info)
        {
            SpecPanel.NoticeBar.Kind = kind;
            SpecPanel.NoticeBar.Message = message;
            SpecPanel.NoticeBar.Visibility = Visibility.Visible;
        }

        private bool Confirm(string title, string message)
            => MessageDialog.Show(title, message, MessageDialogCommandSet.YesNo) == MessageDialogCommand.Yes;

        private void OnSpecTabNotify(object? sender, Notify notify)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSpecTabNotify(sender, notify)));
                return;
            }
            ShowNotice(notify.Message, (NotificationKind)notify.NotifyLevel);
        }

        private void OnKnowledgeBaseNotify(object? sender, Notify notify)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnKnowledgeBaseNotify(sender, notify)));
                return;
            }
            ShowNotice(notify.Message, (NotificationKind)notify.NotifyLevel);
        }

        private void OnKnowledgeBaseModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(KnowledgeBaseModel.MessagesText))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_knowledgeBaseModel.AutoScroll)
                    KnowledgeBasePanel.KbMessagesBox.ScrollToEnd();
            }));
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
            bool noWorkspace;
            try
            {
                noWorkspace = string.IsNullOrWhiteSpace(GetOpenSolutionDirectory());
            }
            catch
            {
                noWorkspace = true;
            }
            var showRobotCreator = noWorkspace && (_rpaSettings?.ShowRobotCreator ?? true);
            ChatPanel.CreateRobotHost.Visibility = showRobotCreator ? Visibility.Visible : Visibility.Collapsed;
            PackagesPanel.RestorePackagesPanel.Visibility = noWorkspace ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Returns the open solution's directory, or null when no solution is open.</summary>
        private string? GetOpenSolutionDirectory()
        {
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                if (dte?.Solution != null && dte.Solution.IsOpen)
                {
                    var slnPath = dte.Solution.FullName;
                    if (!string.IsNullOrWhiteSpace(slnPath))
                        return Path.GetDirectoryName(slnPath);
                }

                // "Open Folder" mode has no solution: fall back to the opened folder path.
                return VsOpenFolderProbe.GetOpenFolderPath();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Sets the global workspace scope from the provider and creates the workspace folder.</summary>
        private void ApplyWorkspaceScope()
        {
            var workspace = _workspaceProvider.GetCurrentWorkspace();
            var workspaceId = workspace?.Id ?? "";

            // A transient null (e.g. while the solution is reloading) must not drop the storages to
            // the global scope: keep the last known workspace so an in-progress conversation is not
            // flushed to the wrong scope and closed.
            if (string.IsNullOrEmpty(workspaceId) && !string.IsNullOrEmpty(_lastWorkspaceId))
                workspaceId = _lastWorkspaceId;
            else if (!string.IsNullOrEmpty(workspaceId))
                _lastWorkspaceId = workspaceId;

            AppDataPaths.SetWorkspaceScope(workspaceId);
            if (workspace == null)
                return;

            AppDataPaths.EnsureWorkspaceDirectory();

            // First open of this workspace: seed ui_state.json from the global one so the last
            // active tab/conversation carries over once, without clobbering an existing file.
            try
            {
                var globalUiState = AppDataPaths.Resolve("ui_state.json");
                var workspaceUiState = AppDataPaths.ResolveWorkspaceCurrent("ui_state.json");
                if (File.Exists(globalUiState) && !File.Exists(workspaceUiState))
                    File.Copy(globalUiState, workspaceUiState);
            }
            catch
            {
                // migration is best-effort
            }
        }

        private void OnWorkspaceChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnWorkspaceChanged(sender, e)));
                return;
            }

            ApplyWorkspaceScope();
            _chatModel.SetWorkspaceScope(AppDataPaths.WorkspaceId);
            // SetWorkspaceScope switches the model to History; sync the panels (and persist the view)
            // so the UI cannot stay in the conversation layout while the model is in History.
            ShowHistoryView();
            ResetProviderTranscript();
            _settingsModel.ReloadWorkspaceScope();
            _settings = _settingsModel.ToSettingsData();
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            UpdateWorkspaceIndicator();
            UpdateCreateRobotVisibility();
        }

        /// <summary>
        /// Solution events do not fire for "Open Folder" mode, so poll the provider and re-scope
        /// only when the workspace actually changed. Mirrors ApplyWorkspaceScope's transient-null
        /// guard so a solution reload does not thrash the scopes.
        /// </summary>
        private void OnWorkspacePollTick(object? sender, EventArgs e)
        {
            var currentId = _workspaceProvider.GetCurrentWorkspace()?.Id ?? "";

            var effectiveId = string.IsNullOrEmpty(currentId) && !string.IsNullOrEmpty(_lastWorkspaceId)
                ? _lastWorkspaceId
                : currentId;

            if (string.Equals(effectiveId, AppDataPaths.WorkspaceId, StringComparison.Ordinal))
                return;

            OnWorkspaceChanged(this, EventArgs.Empty);
        }

        /// <summary>
        /// Wires the host-specific settings chrome (toast banner, workspace indicator + reset) into the
        /// shared <see cref="SettingsControl"/> slots and injects the RPA and VS sections.
        /// </summary>
        private void WireSettingsHostChrome()
        {
            _settingsToast = new NotificationBanner { Visibility = Visibility.Collapsed };
            _settingsToast.CloseRequested += OnSettingsToastClose;
            _settingsToast.SendToChatRequested += OnNotificationSendToChat;
            SettingsPanel.HostBannerSlot.Content = _settingsToast;

            var workspacePanel = new StackPanel { Orientation = Orientation.Horizontal };
            _workspaceIndicatorText = new TextBlock
            {
                Text = "No workspace open",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 5, 8, 0),
                Foreground = (Brush)FindResource("Dark.Text"),
            };
            _resetWorkspaceButton = new Button
            {
                Content = "Reset workspace",
                Margin = new Thickness(0, 5, 8, 0),
                ToolTip = "Clear workspace-scoped settings, chat history and UI state",
            };
            _resetWorkspaceButton.Click += OnResetWorkspace;
            workspacePanel.Children.Add(_workspaceIndicatorText);
            workspacePanel.Children.Add(_resetWorkspaceButton);
            SettingsPanel.HostToolbarSlot.Content = workspacePanel;

            var rpaSection = new RpaSettingsSection();
            rpaSection.OpenConverterRequested += (_, __) => ConverterToolWindowCommand.Instance?.ShowWindow();
            SettingsPanel.InsertHostSection(1, new SettingsControl.SettingsSection
            {
                Title = "RPA",
                OutlineIcon = MaterialIconKind.RobotOutline,
                FilledIcon = MaterialIconKind.Robot,
                Content = rpaSection,
            });
            SettingsPanel.InsertHostSection(2, new SettingsControl.SettingsSection
            {
                Title = "VS",
                OutlineIcon = MaterialIconKind.MicrosoftVisualStudio,
                FilledIcon = MaterialIconKind.MicrosoftVisualStudio,
                Content = new VsSettingsSection(),
            });
        }

        private void UpdateWorkspaceIndicator()
        {
            if (_workspaceIndicatorText == null)
                return;

            var workspace = _workspaceProvider.GetCurrentWorkspace();
            if (workspace == null)
            {
                _workspaceIndicatorText.Text = "No workspace open";
                _workspaceIndicatorText.ToolTip = null;
                return;
            }

            _workspaceIndicatorText.Text = "Workspace: " + workspace.Name;
            _workspaceIndicatorText.ToolTip = workspace.Path;
        }

        private void OnResetWorkspace(object sender, RoutedEventArgs e)
        {
            var workspace = _workspaceProvider.GetCurrentWorkspace();
            if (workspace == null)
            {
                ShowNotice("No workspace is open.", NotificationKind.Info);
                return;
            }

            if (!Confirm("Reset workspace", "Clear workspace-scoped settings, chat history and UI state for \"" + workspace.Name + "\"?"))
                return;

            var workspaceDir = AppDataPaths.ResolveWorkspace(workspace.Id);
            try
            {
                if (Directory.Exists(workspaceDir))
                    Directory.Delete(workspaceDir, recursive: true);
            }
            catch (Exception ex)
            {
                ShowNotice("Failed to reset workspace: " + ex.Message, NotificationKind.Error);
                return;
            }

            // Re-scope: recreates a fresh workspace directory and reloads empty history/settings.
            ApplyWorkspaceScope();
            _chatModel.SetWorkspaceScope(AppDataPaths.WorkspaceId);
            // SetWorkspaceScope switches the model to History; sync the panels (and persist the view)
            // so the UI cannot stay in the conversation layout while the model is in History.
            ShowHistoryView();
            ResetProviderTranscript();
            _settingsModel.ReloadWorkspaceScope();
            _settings = _settingsModel.ToSettingsData();
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            UpdateWorkspaceIndicator();
            ShowNotice("Workspace state reset.", NotificationKind.Info);
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

                var deployProjectName = (_rpaSettings?.DeployProjectName ?? "").Trim();

                if (string.IsNullOrEmpty(deployProjectName))
                {
                    var robotProjects = _packageCatalog.GetRobotProjectState(dte).Names;
                    if (robotProjects.Count == 0)
                    {
                        LogService.Warn("Deploy blocked: no RPA- robot project found in the open solution.");
                        ShowDeployToast("No RPA- robot project found in the open solution.", success: false);
                        return;
                    }
                }

                if (!await _buildFeedback.HasSolutionConfigurationAsync("Publish"))
                {
                    LogService.Warn("Deploy blocked: 'Publish' solution configuration is not configured.");
                    ShowDeployToast("The 'Publish' solution configuration is not configured. Create it in Build в†’ Configuration Manager and retry.", success: false);
                    return;
                }

                if (string.IsNullOrEmpty(deployProjectName))
                {
                    await _buildFeedback.BuildAsync("Publish");
                    LogService.Info("Publish build started.");
                    ShowDeployToast("Publish build started.", success: true);
                }
                else
                {
                    try
                    {
                        await _buildFeedback.BuildProjectAsync("Publish", deployProjectName);
                        LogService.Info($"Publish build started for project '{deployProjectName}'.");
                        ShowDeployToast($"Publish build started for project '{deployProjectName}'.", success: true);
                    }
                    catch (InvalidOperationException ex)
                    {
                        LogService.Warn(ex.Message);
                        ShowDeployToast(ex.Message, success: false);
                    }
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        /// <summary>
        /// "Analyze logs" panel logo: layout only — visibility is view state
        /// (documentation/13-model-layer.md, rule 10).
        /// </summary>
        private void OnAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            SpecPanel.AnalyzeLogsPanel.Visibility = SpecPanel.AnalyzeLogsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnCloseAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            SpecPanel.AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
            SpecPanel.ProdLogsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Child model of the panel; owned by <see cref="ChatModel"/>, may be absent in headless runs.</summary>
        private LogAnalyzer? LogAnalyzer => _chatModel.ChildModel as LogAnalyzer;

        /// <summary>Builds the log analysis model with its host adapters (discovery, Output window, chat, settings).</summary>
        private LogAnalyzer CreateLogAnalyzer()
        {
            return new LogAnalyzer(
                new LogProviderSourceAdapter(() => _rpaSettings?.LogProvidersPath ?? ""),
                new OutputWindowTextSourceAdapter(_ => _buildFeedback.GetOutputWindowTextAsync()),
                new ChatPromptSinkAdapter((prompt, _) =>
                {
                    SendChatPromptDetached(prompt);
                    return Task.CompletedTask;
                }),
                new UserMessageSinkAdapter(AppendChat, AppendExtensionError),
                new SettingsLogAnalysisAdapter(() => _rpaSettings));
        }

        /// <summary>
        /// Copies what the user typed/selected into the model right before a request.
        /// WPF raises <c>SelectionChanged</c> and <c>SelectedDateChanged</c> <i>before</i> a two-way
        /// binding pushes the new value into the source, so a handler that acts immediately cannot
        /// rely on the binding alone. Everything the model owns stays writable from here; the
        /// controls remain a pure presentation of <see cref="LogAnalyzer"/>.
        /// </summary>
        private void PushPanelInputsToModel(LogAnalyzer analyzer)
        {
            analyzer.RobotName = (SpecPanel.RobotNameBox?.Text ?? "").Trim();

            if (SpecPanel.MinLevelCombo?.SelectedItem is LogLevelOption option)
                analyzer.SelectedMinLevel = option;

            analyzer.FromDate = SpecPanel.FromDatePicker?.SelectedDate;
            analyzer.ToDate = SpecPanel.ToDatePicker?.SelectedDate;
            analyzer.SelectedTask = SpecPanel.TasksGrid?.SelectedItem as ProdTask;
        }

        /// <summary>
        /// Delivers a log-analysis prompt into the chat without waiting for the agent to finish
        /// answering. The panel must close as soon as the prompt is committed to the chat, not after
        /// the (potentially long) agent turn completes — that is what <see cref="SendChatTextAsync"/>
        /// would wait for. The user message is appended synchronously inside
        /// <see cref="RunSingleTurnAsync"/> before its first await, so it is visible immediately.
        /// </summary>
        private void SendChatPromptDetached(string prompt)
            => _ = SendChatPromptDetachedAsync(prompt);

        private async Task SendChatPromptDetachedAsync(string prompt)
        {
            try
            {
                await SendChatTextAsync(prompt);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnAnalyzeLogsFromOutputWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var analyzer = LogAnalyzer;
                if (analyzer == null)
                    return;

                if (await analyzer.AnalyzeFromOutputWindowAsync())
                {
                    SpecPanel.AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                    SpecPanel.ProdLogsPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogsFromProdServers(object sender, RoutedEventArgs e)
        {
            try
            {
                SpecPanel.ProdLogsPanel.Visibility = Visibility.Visible;

                // Discovery, the provider combo, the status text and the robot-name suggestions are
                // model state; the bindings of ProdLogsPanel render them (rule 10: visibility is ours).
                LogAnalyzer?.Prepare();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnLoadProdTasks(object sender, RoutedEventArgs e)
        {
            await LoadProdTasksFromModelAsync();
        }

        /// <summary>
        /// Pushes the panel inputs into the model and lets it run the "load tasks" scenario
        /// (validation, robot-name history, provider call and status texts all live there).
        /// </summary>
        private async Task LoadProdTasksFromModelAsync()
        {
            var analyzer = LogAnalyzer;
            if (analyzer == null)
                return;

            try
            {
                PushPanelInputsToModel(analyzer);
                await analyzer.LoadTasksAsync();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnTaskSelected(object sender, SelectionChangedEventArgs e)
        {
            await RefreshLogsFromModelAsync();
        }

        private async void OnMinLevelChanged(object sender, SelectionChangedEventArgs e)
        {
            await RefreshLogsFromModelAsync();
        }

        private async void OnDateRangeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Visibility is layout state, so this guard stays in the view (rule 10); the business
            // guard ("a robot name is typed and a provider is selected") lives in the model.
            if (SpecPanel.ProdLogsPanel.Visibility != Visibility.Visible) return;

            var analyzer = LogAnalyzer;
            if (analyzer == null) return;

            PushPanelInputsToModel(analyzer);
            if (!analyzer.CanLoadTasks) return;

            try
            {
                await analyzer.LoadTasksAsync();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        /// <summary>Pushes the selection/filter into the model and reloads the logs of the selected task.</summary>
        private async Task RefreshLogsFromModelAsync()
        {
            var analyzer = LogAnalyzer;
            if (analyzer == null)
                return;

            try
            {
                PushPanelInputsToModel(analyzer);
                await analyzer.RefreshLogsAsync();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnAnalyzeProdLogs(object sender, RoutedEventArgs e)
        {
            try
            {
                var analyzer = LogAnalyzer;
                if (analyzer == null)
                    return;

                if (await analyzer.AnalyzeFromProdLogsAsync())
                {
                    SpecPanel.AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                    SpecPanel.ProdLogsPanel.Visibility = Visibility.Collapsed;
                }
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
            var path = (_rpaSettings?.PackagesPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetPackagesStatus("Packages directory is not configured. Set the path in the \"Settings\" tab.");
                MainTabs.TrySelectTab("Settings");
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
            PackagesPanel.PackagesStatusText.Text = text ?? "";
        }

        private void StartChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StartChatStatus));
                return;
            }
            _chatModel.StartStatus();
        }

        private void StopChatStatus()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StopChatStatus));
                return;
            }
            _chatModel.StopStatus();
        }

        /// <summary>
        /// Drops any resumable breakpoint. The "Resume task" button hides by itself: its visibility is
        /// bound to <see cref="ChatModel.CanResume"/>, which turns false as soon as the flag is cleared.
        /// </summary>
        private void ResetResume()
        {
            _chatModel.HasResumableRun = false;
            _resumableSnapshot = null;
            _resumableBudget = null;
        }

        /// <summary>Drops the canonical provider transcript (new chat, close, clear, workspace switch).</summary>
        private void ResetProviderTranscript()
        {
            _providerTranscript = new List<ChatMessageData>();
        }

        /// <summary>
        /// Copies the canonical provider transcript onto the active conversation so the next history save
        /// persists it. A reopened chat then restores the exact native tool_calls/tool_call_id structure
        /// and keeps the provider's prompt cache hot. The live agent loop is not affected: it always uses
        /// the in-memory <see cref="_providerTranscript"/>.
        /// </summary>
        private void PersistProviderTranscript()
        {
            var conversation = _chatModel?.ActiveChatConversation;
            if (conversation == null)
                return;
            conversation.ProviderTranscriptJson = ProviderTranscriptCodec.Serialize(_providerTranscript);
        }

        private void ShowChatStatusError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowChatStatusError(message)));
                return;
            }
            _chatModel.ShowStatusError(message);
        }


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

        private async void OnNotificationSendToChat(object sender, RoutedEventArgs e)
        {
            if (sender is NotificationBanner banner && !string.IsNullOrWhiteSpace(banner.Message))
                await SubmitErrorToChatAsync(banner.Message);
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

        private async Task SendChatTextAsync(string text, IReadOnlyList<ChatAttachment>? attachments)
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

            if (_agentRunner == null) return;

            // A closed chat has no transcript to write into: activate a conversation before the message
            // (or the queue entry) is created. EnsureChatReadyToReceive also brings the conversation
            // view back, so a typed message never lands while the history list is on screen (or while
            // the panels are out of sync with the model's view mode).
            EnsureChatReadyToReceive();

            // Agent busy — enqueue instead of dropping the message.
            if (_chatCts != null)
            {
                EnqueuePending(text, attachments);
                return;
            }

            // Run the current message, then drain the queue.
            await RunSingleTurnAsync(text, attachments);

            while (_pendingMessages.Count > 0)
            {
                var next = DequeueNext();
                if (next == null) break;
                await RunSingleTurnAsync(next.Text, next.Attachments);
            }
        }

        private void EnqueuePending(string text, IReadOnlyList<ChatAttachment>? attachments)
        {
            _chatModel.Enqueue(text, attachments);
        }

        private PendingMessage? DequeueNext()
        {
            return _chatModel.DequeueNext();
        }

        private void OnSteerRequested(PendingMessage item)
        {
            if (item == null || _steerSlot != null)
                return;

            // v1: text-only steering — messages with attachments stay queued so their files are
            // not silently dropped (the steer button is already disabled for them in the control).
            var text = (item.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text) || item.HasAttachments)
                return;

            // Only mark it as steered: the message is removed from the queue and shown in the chat
            // by TakeSteer when the agent loop actually consumes it, so an unconsumed steer (the
            // turn ended right after the click) falls back to a normal queued message.
            _steerSlot = new SteerSlot(item, text);
        }

        /// <summary>
        /// Polled by <see cref="AgentRunner"/> at each iteration boundary. Atomically takes the
        /// pending steer (at most one) and, on the UI thread, drops it from the queue and shows it
        /// in the chat. Runs on the agent's background thread.
        /// </summary>
        private string TakeSteer()
        {
            var slot = Interlocked.Exchange(ref _steerSlot, null);
            if (slot == null)
                return string.Empty;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _pendingMessages.Remove(slot.Message);
                AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = slot.Text });
            }));

            return slot.Text;
        }

        /// <summary>Executes a single agent turn (one user message) to completion.</summary>
        private async Task RunSingleTurnAsync(string text, IReadOnlyList<ChatAttachment>? attachments)
        {
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

            ResetResume();

            var modeName = AgentModeNames.ToValue(_chatModel.Mode);
            var history = _providerTranscript;
            // The turn runs inside an active conversation: activate it before the message is appended
            // (EnsureActiveConversation drops what was left of a closed chat) and set the title on it.
            var conversation = _chatModel.EnsureActiveConversation();
            if (string.IsNullOrEmpty(conversation.Title))
                conversation.Title = ChatMessageMapper.BuildTitle(uiText);

            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = uiText });

            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var budget = BuildContextBudget();
            var environment = BuildAgentToolEnvironment();
            await RunAgentTurnAsync(_chatModel.Mode, budget, "input chars=" + promptText.Length + ", images=" + images.Count, thinking,
                (ct, progress, resumeProgress) => _agentRunner.RunAsync(history, promptText, images, _chatModel.Mode, budget, ct, environment, progress, resumeProgress, TakeSteer));
        }

        /// <summary>
        /// Drives one agent turn end-to-end: streaming status, reasoning buffering, checkpoints,
        /// tool progress, final answer and the Resume button after a manual Cancel.
        /// </summary>
        private async Task RunAgentTurnAsync(
            AgentMode mode,
            ContextBudget budget,
            string startDetail,
            ChatMessage thinking,
            Func<CancellationToken, IProgress<ChatStreamDelta>, IProgress<AgentResumeSnapshot>, Task<AgentRunResult>> run)
        {
            var modeName = mode.ToString();
            var cts = new CancellationTokenSource();
            _chatCts = cts;
            _approveAllInRun = false;
            _denyAllInRun = false;
            StartChatStatus();
            LogService.ChatInfo("Agent chat request started (mode=" + modeName + (string.IsNullOrEmpty(startDetail) ? "" : ", " + startDetail) + ").");

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

            AgentResumeSnapshot? resumableSnapshot = null;

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
                var resumeProgress = new Progress<AgentResumeSnapshot>(snap => resumableSnapshot = snap);
                await EnsureBaselineCheckpointAsync();

                var result = await run(cts.Token, progress, resumeProgress);
                _providerTranscript = (result.Transcript ?? new List<ChatMessageData>()).ToList();
                PersistProviderTranscript();

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
                        Kind = mode == AgentMode.Act ? ChatMessageKind.ResultAnswerAct : ChatMessageKind.ResultAnswerPlan,
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

                if (_chatModel.HasResumableRun && resumableSnapshot != null)
                {
                    _resumableSnapshot = resumableSnapshot;
                    _resumableMode = mode;
                    _resumableBudget = budget;
                }
                else
                {
                    ResetResume();
                }

                // StopChatStatus moves the chat to Idle, which is what makes the Resume button
                // visible through ChatModel.CanResume - the host touches no control here.
                StopChatStatus();
                LogService.ChatInfo("Agent chat request cancelled.");
            }
            catch (ProviderErrorException pex)
            {
                reasoningTimer.Stop();
                thinking.Reasoning = reasoningBuffer.ToString();
                thinking.IsThinking = false;
                ShowChatStatusError(pex.FriendlyMessage);
                LogService.ChatError(pex, "Agent chat request failed (provider).");
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
                // Any steer not consumed by this turn's loop reverts to a normal queued message:
                // it is still in the queue and the drain loop will run it next.
                _steerSlot = null;
                if (!Dispatcher.CheckAccess())
                    Dispatcher.Invoke(new Action(() => { _chatCts = null; }));
                else
                {
                    _chatCts = null;
                }
                cts.Dispose();
            }
        }

        /// <summary>Continues a cancelled turn from its saved snapshot, reusing the same user message in the chat.</summary>
        private async Task RunResumeTurnAsync(AgentResumeSnapshot snapshot, AgentMode mode, ContextBudget budget)
        {
            var modeName = mode.ToString();
            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var environment = snapshot.Environment ?? BuildAgentToolEnvironment();
            await RunAgentTurnAsync(mode, budget, "resume", thinking,
                (ct, progress, resumeProgress) => _agentRunner.ResumeAsync(snapshot, mode, budget, ct, environment, progress, resumeProgress, TakeSteer));
        }

        /// <summary>Removes the "Request cancelled." notice added by a cancelled turn before resuming.</summary>
        private void RemoveCancelledNotice()
        {
            var notice = _messages.LastOrDefault(m => m.Kind == ChatMessageKind.SystemNotice && m.Text == "Request cancelled.");
            if (notice != null)
                _messages.Remove(notice);
        }

        /// <summary>Removes the stale partial "Thinking…" placeholder left behind by a cancelled turn.</summary>
        private void RemoveStaleThinkingPlaceholder()
        {
            var stale = _messages.LastOrDefault(m => m.Kind == ChatMessageKind.AgentText && m.IsPartial);
            if (stale != null)
                _messages.Remove(stale);
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
            ChatPanel.AppendInputText(text);
        }

        /// <summary>
        /// Sends an externally produced message (e.g. an Error List item selected by the user)
        /// to the agent chat. When the agent is still processing a previous request, the message
        /// is queued and processed after the current turn completes.
        /// <para>
        /// A closed chat (the close button of the conversation header) is reopened first, so every
        /// external entry point — "Send errors", "Send to RPA Writer", "Send exception to RPA Writer",
        /// notification banners — lands in a real conversation instead of disappearing.
        /// </para>
        /// </summary>
        public async Task SubmitErrorToChatAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Model scenario: activates a conversation when the chat is closed and brings its view back.
            EnsureChatReadyToReceive();

            if (_chatCts != null)
            {
                EnqueuePending(text, null);
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
                switch (message.Kind)
                {
                    case ChatMessageKind.User:
                        if (!string.IsNullOrWhiteSpace(message.Text))
                            transcript.Add(new ChatMessageData { Role = "user", Content = message.Text });
                        break;

                    case ChatMessageKind.AgentText:
                    case ChatMessageKind.ResultAnswerPlan:
                    case ChatMessageKind.ResultAnswerAct:
                        if (!string.IsNullOrWhiteSpace(message.Text))
                            transcript.Add(new ChatMessageData { Role = "assistant", Content = message.Text });
                        break;

                    case ChatMessageKind.ToolCall:
                    case ChatMessageKind.ToolBlocked:
                    {
                        // Fallback reconstruction from persisted history (used only when no canonical
                        // provider transcript was persisted): the exact tool-call arguments are not
                        // stored, so both the request and the result are emitted in a flat, provider-safe
                        // form. The live agent loop is unaffected — it uses the canonical transcript.
                        var payload = message.Payload as ToolCallPayload;
                        var toolName = payload?.ToolName ?? "";
                        transcript.Add(new ChatMessageData { Role = "assistant", Content = "Tool call: " + toolName });
                        if (message.Kind == ChatMessageKind.ToolBlocked)
                        {
                            var blockReason = string.IsNullOrWhiteSpace(payload?.Result) ? "blocked in Plan mode." : payload.Result;
                            transcript.Add(new ChatMessageData { Role = "user", Content = blockReason });
                        }
                        else
                        {
                            // Emit the result as a plain "user" message: the exact native tool_calls
                            // structure (arguments/ids) is not persisted, and a bare "tool" role without
                            // a matching tool_calls block is rejected by native providers (400).
                            transcript.Add(new ChatMessageData { Role = "user", Content = "Tool \"" + toolName + "\" returned:\n" + (payload?.Result ?? "") });
                        }
                        break;
                    }

                    // Other kinds (notices, errors, questions, sub-agents, checkpoints) are UI-only
                    // and not part of the model conversation.
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
                MaxStagnantRepeats = _settings.MaxStagnantRepeats,
                StagnantLoopNudge = _settings.StagnantLoopNudge,
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
                Mode = AgentModeNames.ToValue(_chatModel.Mode)
            });
            Dispatcher.BeginInvoke(new Action(() => item.Status = "running"));

            var runner = new AgentRunner(
                _ai,
                registry,
                m => AgentPromptBuilder.Build(m, registry, toolCallingMode, allowedPermissions, GetReadRootsForPrompt()),
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
                var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), task, _chatModel.Mode, budget, ct);
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

        /// <summary>
        /// Captures the tool environment available right now: the registry built from the current
        /// settings and MCP adapters, the calling protocol, the enabled permission categories, the
        /// memory-bank mode, the read roots and the user addressing. Built at the start of every
        /// dialogue turn so the system prompt always matches the tools that can actually run.
        /// </summary>
        private AgentToolEnvironment BuildAgentToolEnvironment()
        {
            var memoryBankMode = ParseMemoryBankMode();
            var registry = BuildToolRegistry(memoryBankMode);
            _toolRegistry = registry;

            var toolCallingMode = string.Equals(_settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;

            return new AgentToolEnvironment
            {
                Registry = registry,
                ToolCallingMode = toolCallingMode,
                AllowedPermissions = BuildAllowedPermissions(),
                MemoryBankMode = memoryBankMode,
                AllowedReadPaths = GetReadRootsForPrompt(),
                UserName = _settings?.UserName ?? "",
                AddressByName = _settings?.AddressByName ?? false
            };
        }

        private void RebuildAgentToolsCore()
        {
            var memoryBankMode = ParseMemoryBankMode();
            var registry = BuildToolRegistry(memoryBankMode);
            _toolRegistry = registry;
            if (_agentRunner != null)
            {
                _agentRunner.ToolExecuted -= OnAgentToolExecuted;
                _agentRunner.ContextCompacted -= OnAgentContextCompacted;
                _agentRunner.RetryOccurred -= OnAgentRetryOccurred;
                _agentRunner.ToolRepetitionDetected -= OnAgentToolRepetition;
                _agentRunner.CompactionStarted -= OnAgentCompactionStarted;
                _agentRunner.CompactionFinished -= OnAgentCompactionFinished;
            }
            var toolCallingMode = string.Equals(_settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowedPermissions = BuildAllowedPermissions();
            _agentRunner = new AgentRunner(_ai, registry, mode => AgentPromptBuilder.Build(mode, registry, toolCallingMode, allowedPermissions, _settings?.UserName ?? "", _settings?.AddressByName ?? false, memoryBankMode, GetReadRootsForPrompt()), toolCallingMode: toolCallingMode, allowedPermissions: allowedPermissions, approvalGate: _settings?.AskBeforeEachTool == true ? ApproveToolAsync : null);
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
            _agentRunner.ToolRepetitionDetected += OnAgentToolRepetition;
            _agentRunner.CompactionStarted += OnAgentCompactionStarted;
            _agentRunner.CompactionFinished += OnAgentCompactionFinished;
        }

        /// <summary>
        /// Connects to the configured MCP servers in the background and registers their tools
        /// once ready. Fire-and-forget: a broken server is logged and skipped, never thrown.
        /// </summary>
        private async void RefreshMcpToolsAsync()
        {
            try
            {
                var json = (_settings?.McpServersJson ?? "").Trim();
                McpConnectResult? result = null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    result = await Task.Run(() => McpServerManager.ConnectAsync(json, CancellationToken.None));
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ApplyMcpResult(result);
            }
            catch (Exception ex)
            {
                LogService.Error("MCP refresh failed: " + ex.Message);
            }
        }

        private void ApplyMcpResult(McpConnectResult? result)
        {
            DisposeMcpClients();
            _mcpAdapters = result?.Tools ?? new List<IAgentTool>();
            if (result != null)
            {
                _mcpClients.AddRange(result.Clients);
                foreach (var error in result.Errors)
                    LogService.Warn("MCP: " + error);
            }
            RebuildAgentToolsCore();
        }

        private void DisposeMcpClients()
        {
            foreach (var client in _mcpClients)
            {
                try { client.Dispose(); } catch { /* best effort */ }
            }
            _mcpClients.Clear();
        }

        private void OnAgentRetryOccurred(object? sender, AgentRetryArgs e)
        {
            LogService.ChatWarn("Agent retry: " + (e.Kind ?? "") + " attempt " + e.Attempt + "/" + e.AttemptLimit + " - " + (e.Detail ?? ""));
        }

        private void OnAgentToolRepetition(object? sender, AgentToolRepetitionArgs e)
        {
            LogService.ChatWarn("Stagnant loop detected: tool \"" + (e.ToolName ?? "") + "\" repeated " + e.Threshold + " times in a row.");
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

        private void OnAgentCompactionStarted(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentCompactionStarted(sender, e)));
                return;
            }
            _compactionDepth++;
            ChatPanel.SetCompactIndicator(true);
        }

        private void OnAgentCompactionFinished(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentCompactionFinished(sender, e)));
                return;
            }
            if (_compactionDepth > 0)
                _compactionDepth--;
            ChatPanel.SetCompactIndicator(_compactionDepth > 0);
        }

        /// <summary>Shows or hides the blinking red dot on the "Compact now" button.</summary>
        private void ShowDocsIndexCore()
        {
            RenderDocsFile(Path.Combine(HelpDocumentation.ResolveDocsDirectory(), "00-index.md"), "Documentation");
        }

        /// <summary>
        /// Renders a shipped documentation markdown file into the built-in Docs viewer.
        /// Relative links are routed back through <see cref="HandleDocsLink"/>.
        /// </summary>
        private void RenderDocsFile(string path, string title)
        {
            ChatPanel.DocsTitleTextControl.Text = title;
            if (!File.Exists(path))
            {
                ChatPanel.DocsContentControl.Content = new TextBlock
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
                ChatPanel.DocsContentControl.Content = MarkdownRenderer.Render(File.ReadAllText(path));
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

        private IReadOnlyCollection<string> GetDisabledAgentTools()
        {
            return _settings?.DisabledAgentTools ?? new List<string>();
        }

        private MemoryBankMode ParseMemoryBankMode()
        {
            var value = _settings?.MemoryBankMode;
            if (string.Equals(value, "Tool", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Tool;
            if (string.Equals(value, "Convention", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Convention;
            return MemoryBankMode.Disabled;
        }

        private IReadOnlyList<string> GetReadRootsForPrompt()
        {
            return _toolFactory?.GetReadRoots() ?? Array.Empty<string>();
        }

        private AgentToolRegistry BuildToolRegistry(MemoryBankMode memoryBankMode)
        {
            var factory = new AgentToolFactory(
                _ai,
                () => _specTabModel.CurrentSpec,
                KbPath,
                () => (_rpaSettings?.RobotsRootPath ?? "").Trim(),
                () => (_rpaSettings?.RobotTemplateName ?? "").Trim(),
                GetOpenSolutionDirectory,
                () => _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2),
                _buildFeedback,
                _robotTemplate,
                _roslyn,
                AskQuestionAsync,
                () => (_settings?.SkillsPath ?? "").Trim(),
                () => (IReadOnlyList<string>)(_settings?.AdditionalReadPaths ?? new List<string>()),
                () => _settings?.DenyServiceDirectories ?? true,
                () => _settings?.DenySensitiveFiles ?? true,
                _settings.RoslynSemanticsEnabled,
                _settings.FilterBuildTestOutput,
                memoryBankMode,
                RunSubAgentAsync,
                _settings.AllowSubagents,
                _settings.SubagentMaxDepth,
                () => _chatModel.Mode,
                GetDisabledAgentTools);
            _toolFactory = factory;
            factory.PixMwfStore = PixMwfMappingStore.Shared;
            var registry = factory.CreateRegistry();
            foreach (var adapter in _mcpAdapters)
                registry.Register(adapter);
            return registry;
        }

        private void OnAgentToolExecuted(object? sender, AgentToolCall e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAgentToolExecuted(sender, e)));
                return;
            }
            var detail = string.IsNullOrWhiteSpace(e.DisplayResult) ? e.Result : e.DisplayResult;
            LogService.ChatInfo("Agent tool executed: " + (e.ToolName ?? "") + (e.Blocked ? " (blocked)" : "") + (e.Rejected ? " (rejected)" : "") + ", result chars=" + (detail == null ? 0 : detail.Length));
            if (e.Rejected)
                return; // The approval card already shows the reject decision.
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

            if (!string.IsNullOrEmpty(_chatModel.LastError))
                AppendExtensionError(_chatModel.LastError);

            RestoreUiState();
        }

        /// <summary>Restores the chat view, active conversation, agent mode and active tab from the last session.</summary>
        private void RestoreUiState()
        {
            _restoringUiState = true;
            try
            {
                var state = _chatModel.LoadUiState();

                // The toggle and the input border follow the model through bindings; RestoreMode
                // assigns the mode without recording a "Mode: ..." transcript entry.
                _chatModel.RestoreMode(state.AgentMode);

                SelectTabByKey(state.ActiveTab);

                if (state.ChatViewMode == ChatViewMode.History)
                {
                    ShowHistoryView();
                }
                else
                {
                    var toOpen = _chatModel.ResolveConversationToOpen(state.LastConversationId);
                    if (toOpen != null)
                    {
                        OpenConversationCore(toOpen.Id, markDirty: false);
                        ShowConversationView();
                    }
                    else if (string.IsNullOrWhiteSpace(state.LastConversationId) && _conversations.Count > 0)
                    {
                        // The previous session ended with the chat closed: nothing is activated, so the
                        // history stays on screen (the conversation view needs an active chat).
                        ShowHistoryView();
                    }
                    else
                    {
                        // The remembered chat was deleted in another session - start a fresh one.
                        StartNewChatCore(markDirty: false);
                        ShowConversationView();
                    }
                }
            }
            finally
            {
                _restoringUiState = false;
            }
        }

        private static string MainTabToKey(MainTab tab)
        {
            switch (tab)
            {
                case MainTab.Spec: return "Spec";
                case MainTab.KnowledgeBase: return "KnowledgeBase";
                case MainTab.Settings: return "Settings";
                case MainTab.Packages: return "Packages";
                case MainTab.Controls: return "Controls";
                default: return "Chat";
            }
        }

        private static MainTab KeyToMainTab(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return MainTab.Chat;

            switch (key.Trim())
            {
                case "Spec": return MainTab.Spec;
                case "KnowledgeBase": return MainTab.KnowledgeBase;
                case "Settings": return MainTab.Settings;
                case "Packages": return MainTab.Packages;
                case "Controls": return MainTab.Controls;
                default: return MainTab.Chat;
            }
        }

        private void SelectTabByKey(MainTab tab)
        {
            var key = MainTabToKey(tab);
            var tabItem = MainTabs.FindTab(key);
            if (tabItem != null && tabItem.Visibility == Visibility.Visible)
                MainTabs.TrySelectTab(key);
        }

        private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControlsGalleryHostVisibility();
            if (_restoringUiState)
                return;
            PersistUiState();
        }

        /// <summary>Writes the current chat/tab state so the next session can restore it.</summary>
        private void PersistUiState()
        {
            if (_restoringUiState)
                return;

            _chatModel.PersistUiState(KeyToMainTab(MainTabs.SelectedTabKey));
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

        /// <summary>Flushes the transcript to the history store; the scenario lives in the model.</summary>
        private void SaveHistoryNow()
        {
            _chatModel.SaveHistoryNow();
        }

        private void StartNewChatCore(bool markDirty = true)
        {
            _chatCts?.Cancel();
            ResetResume();
            ResetProviderTranscript();

            // Saving the previous conversation, clearing the transcript and the queue is the
            // model scenario; the host only resets its own agent and checkpoint state.
            _chatModel.StartNewChat(markDirty);

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = null;
            _iterationNumber = 0;

            ShowConversationView();
        }
        private void OpenConversationCore(string id, bool markDirty = true)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            if (string.Equals(id, _chatModel.ActiveConversationId, StringComparison.Ordinal))
                return;

            _chatCts?.Cancel();
            ResetResume();

            if (!_chatModel.OpenConversation(id, markDirty))
            {
                if (!string.IsNullOrEmpty(_chatModel.LastError))
                    AppendExtensionError(_chatModel.LastError);
                return;
            }

            // Checkpoint indicators and scrolling are view/agent concerns, not model state.
            RehydrateCheckpoints();
            ChatPanel.MessagesList.ScrollToEnd();

            // Restore the canonical provider transcript persisted with the conversation, so the exact
            // native tool_calls/tool_call_id structure survives a reopen and the provider's prompt cache
            // stays hot. Legacy conversations without a persisted transcript fall back to the flat
            // reconstruction below (the live agent loop is unaffected by that fallback).
            var restoredTranscript = ProviderTranscriptCodec.Deserialize(_chatModel.ActiveChatConversation?.ProviderTranscriptJson);
            _providerTranscript = restoredTranscript.Count > 0 ? restoredTranscript : BuildTranscript();

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = GetLastCheckpointHash();
            _iterationNumber = 0;

            PersistUiState();
        }

        /// <summary>
        /// Makes sure the chat can receive a message and shows it. Every entry point that produces a message
        /// outside the input box calls this: a closed chat gets a fresh conversation (the model scenario)
        /// instead of swallowing the message, and the tab returns to the conversation view.
        /// </summary>
        private void EnsureChatReadyToReceive()
        {
            _chatModel.EnsureActiveConversation();
            ShowConversationView();
        }

        private void ShowConversationView()
        {
            ChatPanel.ShowConversationView();
            PersistUiState();
        }

        private void ShowHistoryView()
        {
            ChatPanel.ShowHistoryView();
            PersistUiState();
        }

        /// <summary>
        /// Drops the host-side state that belonged to the removed conversation (resume snapshot,
        /// checkpoint baseline, save timer) and persists the new session state.
        /// </summary>
        private void ResetAgentStateAfterConversationRemoved()
        {
            _historySaveTimer.Stop();
            ResetResume();
            ResetProviderTranscript();

            _baselineCheckpointCreated = false;
            _lastCheckpointHash = null;
            _iterationNumber = 0;

            PersistUiState();
        }

        /// <summary>
        /// Rebuilds derived checkpoint values from the persisted commit hashes after a
        /// conversation is reloaded. Visibility flags and the sequence follow from the
        /// transcript alone, so the green commit id and Restore action survive a restart.
        /// </summary>
        private void RehydrateCheckpoints()
        {
            var checkpoints = _messages
                .Where(m => m.Kind == ChatMessageKind.Checkpoint && m.Payload is CheckpointPayload)
                .Select(m => (CheckpointPayload)m.Payload!)
                .ToList();

            for (var i = 0; i < checkpoints.Count; i++)
            {
                var payload = checkpoints[i];
                payload.Sequence = i + 1;
                payload.HasChanges = i < checkpoints.Count - 1;
                payload.HasDiff = i > 0;
                payload.TaskId = _currentConversation?.Id ?? "";
            }

            RehydrateCheckpointMetadata();
        }

        /// <summary>
        /// Best-effort restore of checkpoint message/time from the shadow git repository.
        /// These values are not persisted and are recomputed from the commits themselves.
        /// </summary>
        private async void RehydrateCheckpointMetadata()
        {
            var ws = GetOpenSolutionDirectory();
            var taskId = _currentConversation?.Id;
            if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(taskId))
                return;

            var checkpoints = _messages
                .Where(m => m.Kind == ChatMessageKind.Checkpoint && m.Payload is CheckpointPayload p && !string.IsNullOrWhiteSpace(p.CommitHash))
                .Select(m => (CheckpointPayload)m.Payload!)
                .ToList();
            if (checkpoints.Count == 0)
                return;

            try
            {
                var service = _checkpoints.GetOrCreate(taskId, ws);
                if (!service.IsInitialized)
                    await service.InitializeAsync(CancellationToken.None).ConfigureAwait(true);

                var commits = await Task.Run(() => service.GetCheckpointsAsync(CancellationToken.None)).ConfigureAwait(true);

                var byHash = new Dictionary<string, Checkpoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var commit in commits)
                {
                    if (!string.IsNullOrWhiteSpace(commit.Ref))
                        byHash[commit.Ref] = commit;
                }

                foreach (var payload in checkpoints)
                {
                    if (byHash.TryGetValue(payload.CommitHash ?? "", out var commit))
                    {
                        payload.Message = commit.Message ?? "";
                        payload.CreatedAtUtc = commit.CreatedAtUtc.UtcDateTime;
                        payload.WorkspaceDir = ws;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("Checkpoint metadata rehydration failed: " + ex.Message);
            }
        }

        private string? GetLastCheckpointHash()
        {
            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Kind == ChatMessageKind.Checkpoint
                    && _messages[i].Payload is CheckpointPayload payload
                    && !string.IsNullOrWhiteSpace(payload.CommitHash))
                {
                    return payload.CommitHash;
                }
            }
            return null;
        }

        /// <summary>
        /// Mode toggle handler. The two-way binding has already written <see cref="ChatModel.Mode"/>
        /// (whose setter records the transcript entry), so the host only persists the session.
        /// </summary>
        private IntPtr GetOwner()
        {
            var window = System.Windows.Window.GetWindow(this);
            return window == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }

        private const double SettingsToastMaxHeight = 160.0;

        private void ShowSettingsToast(string text, NotificationKind kind = NotificationKind.Success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowSettingsToast(text, kind)));
                return;
            }
            _settingsToast.Kind = kind;
            _settingsToast.Message = text ?? "";
            _settingsToast.Visibility = Visibility.Visible;
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
            SlideSettingsToast(_settingsToast.ActualHeight, 0,
                () => _settingsToast.Visibility = Visibility.Collapsed);
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
            _settingsToast.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            if (onCompleted != null)
                animation.Completed += (_, __) => onCompleted();
            _settingsToast.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
        }

        private const double DeployToastMaxHeight = 120.0;

        private void ShowDeployToast(string text, bool success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowDeployToast(text, success)));
                return;
            }
            SpecPanel.DeployToast.Kind = success ? NotificationKind.Success : NotificationKind.Warning;
            SpecPanel.DeployToast.Message = text ?? "";
            SpecPanel.DeployToast.Visibility = Visibility.Visible;
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
            SlideDeployToast(SpecPanel.DeployToast.ActualHeight, 0,
                () => SpecPanel.DeployToast.Visibility = Visibility.Collapsed);
        }

        private void OnDeployToastTick(object? sender, EventArgs e)
        {
            HideDeployToast();
        }

        private void OnDeployToastClose(object sender, RoutedEventArgs e)
        {
            HideDeployToast();
        }

        private void OnVoiceErrorClose(object sender, RoutedEventArgs e)
        {
            _voiceErrorToast.Visibility = Visibility.Collapsed;
        }

        private void ShowCheckpointRestoreError(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCheckpointRestoreError(text)));
                return;
            }

            _checkpointRestoreErrorToast.Kind = NotificationKind.Error;
            _checkpointRestoreErrorToast.Message = text ?? "";
            _checkpointRestoreErrorToast.Visibility = Visibility.Visible;
            // No auto-hide: the error banner stays until the user closes it.
        }

        private void OnCheckpointRestoreErrorClose(object sender, RoutedEventArgs e)
        {
            _checkpointRestoreErrorToast.Visibility = Visibility.Collapsed;
        }

        private void SlideDeployToast(double from, double to, Action? onCompleted = null)
        {
            SpecPanel.DeployToast.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            if (onCompleted != null)
                animation.Completed += (_, __) => onCompleted();
            SpecPanel.DeployToast.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
        }

        private void OnSettingsModelSaved(object? sender, EventArgs e)
        {
            _settings = _settingsModel.ToSettingsData();
            _chatModel.CacheLossDropRatioPercent = _settings.CacheLossDropRatioPercent;
            _rpaSettingsModel.Save();
            SettingsPanel.ApiKeyBox.Password = _settings.ApiKey ?? "";
            (_chatModel.ChildModel as LogAnalyzer)?.InvalidateProviders();
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            RecreateAiProvider();
            _stt = SpeechToTextFactory.Create(_settings);
            RebuildAgentToolsCore();
            RefreshMcpToolsAsync();
            ApplyControlsVisibility();
            UpdateCreateRobotVisibility();
            ShowSettingsToast("Settings saved. Provider: " + (_ai is StubAiProvider ? "stub (offline)" : "OpenAI-compatible"));
        }

        private void OnSettingsNotify(object? sender, Notify notify)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSettingsNotify(sender, notify)));
                return;
            }
            ShowSettingsToast(notify.Message, (NotificationKind)notify.NotifyLevel);
        }

        private void OnApiKeyChanged(object sender, RoutedEventArgs e)
        {
            _settingsModel.ApiKey = SettingsPanel.ApiKeyBox.Password ?? "";
        }

        private void ApplyControlsVisibility()
        {
            if (ControlsTab == null)
                return;
            ControlsTab.Visibility = (_settings != null && _settings.ShowControls)
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateControlsGalleryHostVisibility();
        }

        private void UpdateControlsGalleryHostVisibility()
        {
            if (ControlsPanel != null)
                ControlsPanel.IsHostTabVisible = ControlsTab.Visibility == Visibility.Visible
                    && ReferenceEquals(MainTabs.SelectedItem, ControlsTab);
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
            if (_optionsWarningToast == null)
                return;

            _optionsWarningToast.Kind = NotificationKind.Warning;
            _optionsWarningToast.Message = message ?? string.Empty;
            _optionsWarningToast.Visibility = Visibility.Visible;
        }

        private void HideOptionsWarning()
        {
            if (_optionsWarningToast != null)
                _optionsWarningToast.Visibility = Visibility.Collapsed;
        }

        private void OnOptionsWarningClose(object sender, RoutedEventArgs e)
        {
            HideOptionsWarning();
        }

    }
}
