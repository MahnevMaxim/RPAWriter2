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
using RPAWriter.Core.Mcp;
using RPAWriter.Core.Checkpoints;
using RPAWriter2.Services;
using RPAWriter.Models;
using RPAWriter.Mvvm;
using RPAWriter2.ViewModels;
using RPAWriter2.Mvvm;
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
    public partial class MainWindowControl : UserControl
    {
        private SettingsData _settings;
        private IAiProvider _ai;
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
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
        private AgentRunner? _agentRunner;
        private readonly List<McpClient> _mcpClients = new List<McpClient>();
        private IReadOnlyList<IAgentTool> _mcpAdapters = new List<IAgentTool>();
        private AgentResumeSnapshot? _resumableSnapshot;
        // Canonical provider transcript of the last completed turn (Cline "apiConversationHistory"
        // equivalent). It is the source for the next RunAsync, not the visible Messages collection.
        private List<ChatMessageData> _providerTranscript = new List<ChatMessageData>();
        private AgentMode _resumableMode;
        private ContextBudget? _resumableBudget;
        private ITokenUsageProvider? _tokenUsage;
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
        private readonly ObservableCollection<MessageSample> _messageSamples = new ObservableCollection<MessageSample>();

        private CancellationTokenSource? _chatCts;
        private TaskCompletionSource<string>? _questionTcs;
        private ChatMessage? _activeQuestion;
        private readonly DispatcherTimer _settingsToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _deployToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _notificationAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _workspacePollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private const string CommitBranchName = "dev";

        private static string CommitMessageSystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-message.txt", PromptDefaults.CommitMessageSystemPrompt);

        private static string CommitRobotSummarySystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-robot-summary.txt", PromptDefaults.CommitRobotSummarySystemPrompt);

        private static string CommitOtherSummarySystemPrompt =>
            PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.Tasks, "commit-other-summary.txt", PromptDefaults.CommitOtherSummarySystemPrompt);

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

        public MainWindowControl()
        {
            ApplyWorkspaceScope();

            _chatModel = new ChatModel(
                new SqliteChatHistoryStore(),
                new JsonUiStateStore(),
                chatSink: new ChatPromptSinkAdapter((prompt, _) => SendChatTextAsync(prompt)),
                workspaceProvider: _workspaceProvider);
            // The geometry parser must be ready before InitializeComponent: the
            // ClearInputButton icon (MaterialIconKind.Broom) is resolved via a binding
            // during XAML load and would otherwise fall back to an empty geometry.
            MaterialIconDataProvider.InitializeGeometryParser(s => Geometry.Parse(s));
            this.InitializeComponent();
            _settingsToastTimer.Tick += OnSettingsToastTick;
            _deployToastTimer.Tick += OnDeployToastTick;
            _notificationAutoHideTimer.Tick += OnNotificationAutoHideTick;
            MainTabs.SelectionChanged += OnMainTabsSelectionChanged;
            ControlsInnerTabs.SelectionChanged += OnControlsInnerTabsSelectionChanged;
            ChatMessages.Messages = _messages;
            MessageQueue.Messages = _pendingMessages;
            ChatMessages.Filter = message => !ReferenceEquals(message, _pinnedMessage);
            ChatMessages.CopyMessageRequested += OnChatMessageCopyRequested;
            ChatMessages.QuestionAnswered += OnChatMessageQuestionAnswered;
            ChatMessages.QuestionSkipped += OnChatMessageQuestionSkipped;
            ChatMessages.CheckpointRestoreRequested += OnCheckpointRestoreRequested;
            ChatMessages.DiffOpenRequested += OnDiffOpenRequested;
            AttachmentsList.ItemsSource = _attachments;
            SpecMessages.CopyMessageRequested += OnChatMessageCopyRequested;
            HistoryList.ItemsSource = _conversations;
            _packagesView = CollectionViewSource.GetDefaultView(_packages);
            _packagesView.Filter = FilterPackages;
            PackagesList.ItemsSource = _packagesView;
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
            MvvmFactory.Attach(_settingsModel, _settingsViewModel, SettingsTab);
            _settings = _settingsModel.ToSettingsData();
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            ApiKeyBox.PasswordChanged += OnApiKeyChanged;

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
                () => _settings,
                SynchronizationContext.Current);
            _specTabModel.Notify += OnSpecTabNotify;
            MvvmFactory.Attach(_specTabModel, _specTabViewModel, SpecTab);

            _knowledgeBaseModel = new KnowledgeBaseModel(
                () => _ai,
                () => _settings,
                kbPath => new FileKnowledgeBase(kbPath),
                new VsFileWriter(),
                SynchronizationContext.Current);
            _knowledgeBaseModel.Notify += OnKnowledgeBaseNotify;
            _knowledgeBaseModel.PropertyChanged += OnKnowledgeBaseModelPropertyChanged;
            MvvmFactory.Attach(_knowledgeBaseModel, _knowledgeBaseViewModel, KnowledgeBaseTab);

            SpecMessages.Messages = _specTabModel.SpecMessages;

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
            RefreshMcpToolsAsync();
            HookSolutionEvents();
            _workspaceProvider.WorkspaceChanged += OnWorkspaceChanged;
            _workspaceProvider.Initialize();
            _workspacePollTimer.Tick += OnWorkspacePollTick;
            _workspacePollTimer.Start();
            UpdateCreateRobotVisibility();
            UpdateWorkspaceIndicator();
            _chatModel.LogAnalyzer = CreateLogAnalyzer();
            MvvmFactory.Attach(_chatModel, _chatViewModel, this);
            _chatModel.PropertyChanged += OnChatModelPropertyChanged;
            InitializeChatHistory();
        }

        /// <summary>Re-renders the layout parts that depend on chat model state.</summary>
        private void OnChatModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatModel.PinnedMessage))
                RefreshPinnedMessage();
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
            PushContextWindow();
            PushEstimatedInputTokens();
        }

        private string KbPath()
        {
            return (_settings?.KnowledgeBasePath ?? "").Trim();
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
        /// Shows an interactive question in the chat (with answer buttons) and waits for the
        /// user's answer. Invoked by the ask_question tool handler, possibly on a background thread.
        /// </summary>
        private async Task<string> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

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
            ChatMessages.Append(message);
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
            foreach (var m in _specTabModel.SpecMessages)
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
            if (!Dispatcher.CheckAccess())
                Dispatcher.BeginInvoke(new Action(() => _chatModel.SetEstimatedInputTokens(e.EstimatedInputTokens)));
            else
                _chatModel.SetEstimatedInputTokens(e.EstimatedInputTokens);

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

        private void OnTokenUsageUpdated(object? sender, TokenUsage usage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnTokenUsageUpdated(sender, usage)));
                return;
            }
            _chatModel.RecordTokenUsage(usage);
        }

        private void PushContextWindow()
        {
            var provider = _tokenUsage;
            var budget = BuildContextBudget();
            _chatModel.SetContextWindow(
                provider?.ContextWindowSize ?? 0,
                provider != null ? budget.CompactThresholdTokens : 0,
                provider != null ? budget.MaxInputTokens : 0);
        }

        private void PushEstimatedInputTokens()
        {
            var usedTokens = _chatCts == null
                ? new TokenEstimator().Estimate("", _providerTranscript)
                : _chatModel.ActiveChatConversation?.UsedTokens ?? 0;
            _chatModel.SetEstimatedInputTokens(usedTokens);
        }

        private void ShowError(Exception ex)
        {
            AppendExtensionError(ex);
        }

        private void ShowNotice(string message, NotificationKind kind = NotificationKind.Info)
        {
            NoticeBar.Kind = kind;
            NoticeBar.Message = message;
            NoticeBar.Visibility = Visibility.Visible;
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
                    KbMessagesBox.ScrollToEnd();
            }));
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
            bool noWorkspace;
            try
            {
                noWorkspace = string.IsNullOrWhiteSpace(GetOpenSolutionDirectory());
            }
            catch
            {
                noWorkspace = true;
            }
            var showRobotCreator = noWorkspace && (_settings?.ShowRobotCreator ?? true);
            CreateRobotPanel.Visibility = showRobotCreator ? Visibility.Visible : Visibility.Collapsed;
            RestorePackagesPanel.Visibility = noWorkspace ? Visibility.Collapsed : Visibility.Visible;
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

        private void UpdateWorkspaceIndicator()
        {
            if (WorkspaceIndicatorText == null)
                return;

            var workspace = _workspaceProvider.GetCurrentWorkspace();
            if (workspace == null)
            {
                WorkspaceIndicatorText.Text = "No workspace open";
                WorkspaceIndicatorText.ToolTip = null;
                return;
            }

            WorkspaceIndicatorText.Text = "Workspace: " + workspace.Name;
            WorkspaceIndicatorText.ToolTip = workspace.Path;
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
            ResetProviderTranscript();
            _settingsModel.ReloadWorkspaceScope();
            _settings = _settingsModel.ToSettingsData();
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            UpdateWorkspaceIndicator();
            ShowNotice("Workspace state reset.", NotificationKind.Info);
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

        private void OnClearInput(object sender, RoutedEventArgs e)
        {
            InputBox.Clear();
            InputBox.Focus();
        }

        private async void OnDoItClick(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            InputBox.Clear();
            await _chatModel.DoItCommand(text);
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

                var deployProjectName = (_settings?.DeployProjectName ?? "").Trim();

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
                    ShowDeployToast("The 'Publish' solution configuration is not configured. Create it in Build → Configuration Manager and retry.", success: false);
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
            AnalyzeLogsPanel.Visibility = AnalyzeLogsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnCloseAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
            ProdLogsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Child model of the panel; owned by <see cref="ChatModel"/>, may be absent in headless runs.</summary>
        private LogAnalyzer? LogAnalyzer => _chatModel.LogAnalyzer;

        /// <summary>Builds the log analysis model with its host adapters (discovery, Output window, chat, settings).</summary>
        private LogAnalyzer CreateLogAnalyzer()
        {
            return new LogAnalyzer(
                new LogProviderSourceAdapter(() => _settings?.LogProvidersPath ?? ""),
                new OutputWindowTextSourceAdapter(_ => _buildFeedback.GetOutputWindowTextAsync()),
                new ChatPromptSinkAdapter((prompt, _) =>
                {
                    SendChatPromptDetached(prompt);
                    return Task.CompletedTask;
                }),
                new UserMessageSinkAdapter(AppendChat, AppendExtensionError),
                new SettingsLogAnalysisAdapter(() => _settings));
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
            analyzer.RobotName = (RobotNameBox?.Text ?? "").Trim();

            if (MinLevelCombo?.SelectedItem is LogLevelOption option)
                analyzer.SelectedMinLevel = option;

            analyzer.FromDate = FromDatePicker?.SelectedDate;
            analyzer.ToDate = ToDatePicker?.SelectedDate;
            analyzer.SelectedTask = TasksGrid?.SelectedItem as ProdTask;
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
                    AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                    ProdLogsPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogsFromProdServers(object sender, RoutedEventArgs e)
        {
            try
            {
                ProdLogsPanel.Visibility = Visibility.Visible;

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

        private void OnRobotNameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = LoadProdTasksFromModelAsync();
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
            if (ProdLogsPanel.Visibility != Visibility.Visible) return;

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
                    AnalyzeLogsPanel.Visibility = Visibility.Collapsed;
                    ProdLogsPanel.Visibility = Visibility.Collapsed;
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
            var path = (_settings?.PackagesPath ?? "").Trim();
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

#pragma warning restore VSTHRD100

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

        private void ShowChatStatusError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowChatStatusError(message)));
                return;
            }
            _chatModel.ShowStatusError(message);
        }

        private void OnCancelSend(object sender, RoutedEventArgs e)
        {
            // Manual Cancel: remember to offer "Resume task" once the run has stopped.
            _chatModel.HasResumableRun = true;
            _pendingMessages.Clear();
            _chatCts?.Cancel();
        }

        #pragma warning disable VSTHRD100 // WPF event handlers are async void

        private async void OnResumeTask(object sender, RoutedEventArgs e)
        {
            if (_agentRunner == null || _resumableSnapshot == null)
                return;

            var snapshot = _resumableSnapshot;
            var mode = _resumableMode;
            var budget = _resumableBudget ?? BuildContextBudget();

            ResetResume();
            RemoveCancelledNotice();
            RemoveStaleThinkingPlaceholder();

            await RunResumeTurnAsync(snapshot, mode, budget);
        }

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
            // "Send errors" is an external entry point: it needs a chat even when the report turns out to
            // be the "nothing to send" notice.
            EnsureChatReadyToReceive();

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
            // (or the queue entry) is created.
            _chatModel.EnsureActiveConversation();

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
            await RunAgentTurnAsync(_chatModel.Mode, budget, "input chars=" + promptText.Length + ", images=" + images.Count, thinking,
                (ct, progress, resumeProgress) => _agentRunner.RunAsync(history, promptText, images, _chatModel.Mode, budget, ct, progress, resumeProgress));
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
                if (!Dispatcher.CheckAccess())
                    Dispatcher.Invoke(new Action(() => { _chatCts = null; PushEstimatedInputTokens(); }));
                else
                {
                    _chatCts = null;
                    PushEstimatedInputTokens();
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

            await RunAgentTurnAsync(mode, budget, "resume", thinking,
                (ct, progress, resumeProgress) => _agentRunner.ResumeAsync(snapshot, mode, budget, ct, progress, resumeProgress));
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
                        // Fallback reconstruction from persisted history: the exact tool-call
                        // arguments are not stored, so both the request and the result are emitted
                        // in a flat form the provider can still consume.
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
                            transcript.Add(new ChatMessageData { Role = "tool", Content = "Tool \"" + toolName + "\" returned:\n" + (payload?.Result ?? "") });
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
                _agentRunner.ToolRepetitionDetected -= OnAgentToolRepetition;
                _agentRunner.CompactionStarted -= OnAgentCompactionStarted;
                _agentRunner.CompactionFinished -= OnAgentCompactionFinished;
            }
            var toolCallingMode = string.Equals(_settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowedPermissions = BuildAllowedPermissions();
            _agentRunner = new AgentRunner(_ai, registry, mode => AgentPromptBuilder.Build(mode, registry, toolCallingMode, allowedPermissions, _settings?.UserName ?? "", _settings?.AddressByName ?? false, memoryBankMode), toolCallingMode: toolCallingMode, allowedPermissions: allowedPermissions);
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
            _agentRunner.ToolRepetitionDetected += OnAgentToolRepetition;
            _agentRunner.CompactionStarted += OnAgentCompactionStarted;
            _agentRunner.CompactionFinished += OnAgentCompactionFinished;
            UpdateMemoryBankUi();
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
            RebuildAgentTools();
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
            SetCompactIndicator(true);
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
            SetCompactIndicator(_compactionDepth > 0);
        }

        /// <summary>Shows or hides the blinking red dot on the "Compact now" button.</summary>
        private void SetCompactIndicator(bool compacting)
        {
            if (CompactIndicator == null)
                return;

            if (compacting)
            {
                CompactIndicator.Visibility = Visibility.Visible;
                var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(500))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                CompactIndicator.BeginAnimation(OpacityProperty, animation);
            }
            else
            {
                CompactIndicator.BeginAnimation(OpacityProperty, null);
                CompactIndicator.Opacity = 1.0;
                CompactIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnCompactNow(object sender, RoutedEventArgs e)
        {
            if (_agentRunner == null || _chatCts != null)
                return;

            var transcript = _providerTranscript.Count > 0 ? _providerTranscript : BuildTranscript();
            if (transcript.Count == 0)
            {
                AppendChat("Nothing to compact yet.");
                return;
            }

            try
            {
                var condensed = await _agentRunner.CompactAsync(transcript, BuildContextBudget(), CancellationToken.None);

                // Replace only the visible/working transcript (which is what the next turn is built
                // from); the model keeps the append-only persisted history intact.
                var compactedMessages = condensed.Select(m => new ChatMessage
                {
                    Kind = m.Role == "assistant" ? ChatMessageKind.AgentText
                         : m.Role == "user" ? ChatMessageKind.User
                         : ChatMessageKind.SystemNotice,
                    Text = m.Content
                }).ToList();
                _chatModel.ApplyCompactedTranscript(compactedMessages);
                _providerTranscript = (condensed ?? transcript).ToList();
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
                () => _specTabModel.CurrentSpec,
                KbPath,
                () => (_settings?.RobotsRootPath ?? "").Trim(),
                () => (_settings?.RobotTemplateName ?? "").Trim(),
                GetOpenSolutionDirectory,
                () => _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2),
                _buildFeedback,
                _robotTemplate,
                AskQuestionAsync,
                () => (_settings?.SkillsPath ?? "").Trim(),
                () => (IReadOnlyList<string>)(_settings?.AdditionalReadPaths ?? new List<string>()),
                memoryBankMode,
                RunSubAgentAsync,
                _settings.AllowSubagents,
                _settings.SubagentMaxDepth,
                () => _chatModel.Mode);
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

                SelectTabByHeader(state.ActiveTab);

                if (state.ChatViewMode == ChatViewMode.History)
                {
                    ShowHistoryView();
                }
                else
                {
                    var toOpen = _chatModel.ResolveConversationToOpen(state.LastConversationId);
                    if (toOpen != null)
                    {
                        OpenConversation(toOpen.Id, markDirty: false);
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
                        StartNewChat(markDirty: false);
                        ShowConversationView();
                    }
                }
            }
            finally
            {
                _restoringUiState = false;
            }
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

            _chatModel.PersistUiState(HeaderToMainTab((MainTabs.SelectedItem as TabItem)?.Header as string));
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

        private void StartNewChat(bool markDirty = true)
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
        private void OpenConversation(string id, bool markDirty = true)
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
            ChatMessages.ScrollToEnd();

            // Rebuild the canonical provider transcript from the loaded messages (fallback path:
            // tool arguments are not persisted, so this is a flat approximation).
            _providerTranscript = BuildTranscript();

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
            _chatModel.ShowConversationView();
            ApplyChatViewMode();
            PersistUiState();
        }

        private void ShowHistoryView()
        {
            _chatModel.ShowHistoryView();
            ApplyChatViewMode();
            PersistUiState();
        }

        private void ApplyChatViewMode()
        {
            var conversation = _chatViewMode == ChatViewMode.Conversation;
            ChatToolbarPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            MessagesHostPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatSplitter.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            ChatInputPanel.Visibility = conversation ? Visibility.Visible : Visibility.Collapsed;
            PinnedHeaderPanel.Visibility = conversation && _pinnedMessage != null ? Visibility.Visible : Visibility.Collapsed;
            HistoryViewPanel.Visibility = conversation ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// The close button of the conversation header: closes the chat. Closing means "no active
        /// conversation" — the model saves the transcript, drops it from memory and sets
        /// <see cref="ChatModel.ActiveChatConversation"/> to <c>null</c> - so nothing keeps landing in the
        /// chat and the next start does not restore it.
        /// <para>
        /// This is not "History": browsing the saved chats (<see cref="OnShowHistory"/>) keeps the
        /// conversation active.
        /// </para>
        /// </summary>
        private void OnCloseChat(object sender, RoutedEventArgs e)
        {
            // A turn that is still streaming has no conversation to write into any more.
            _chatCts?.Cancel();

            _chatModel.CloseActiveChat();

            // The model switched the view to History; the host only applies the layout and drops the
            // agent state that belonged to the closed conversation.
            ApplyChatViewMode();
            ResetAgentStateAfterConversationRemoved();
        }

        /// <summary>
        /// Browses the saved chats: the view switches to the history list while the active conversation
        /// stays active. Messages produced by any entry point keep landing in it and bring the
        /// conversation view back (<see cref="EnsureChatReadyToReceive"/>).
        /// </summary>
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

            var deletedActive = string.Equals(conversation.Id, _chatModel.ActiveConversationId, StringComparison.Ordinal);

            if (!_chatModel.DeleteConversation(conversation.Id))
            {
                AppendExtensionError(_chatModel.LastError);
                return;
            }

            if (deletedActive)
                ResetAgentStateAfterConversationRemoved();
        }

        private void OnClearHistory(object sender, RoutedEventArgs e)
        {
            var result = MessageDialog.Show(
                "Clear history",
                "Delete all chats? This cannot be undone.",
                MessageDialogCommandSet.YesNo);
            if (result != MessageDialogCommand.Yes)
                return;

            if (!_chatModel.ClearHistory())
            {
                AppendExtensionError(_chatModel.LastError);
                return;
            }

            ResetAgentStateAfterConversationRemoved();
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
            ChatContextIndicator.ShowDetails = _pinnedExpanded;
        }

        private void RefreshPinnedMessage()
        {
            // The pinned message itself is computed by the model; the host only renders it.
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
        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (_restoringUiState)
                return;

            PersistUiState();
        }

        private void OnOpenConverterToolWindow(object sender, RoutedEventArgs e)
        {
            ConverterToolWindowCommand.Instance?.ShowWindow();
        }

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
            SettingsToast.Kind = kind;
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

        private void OnSettingsModelSaved(object? sender, EventArgs e)
        {
            _settings = _settingsModel.ToSettingsData();
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            _chatModel.LogAnalyzer?.InvalidateProviders();
            LogService.Configure(_settings.LogFilePath, _settings.ChatLogFilePath);
            RecreateAiProvider();
            _stt = SpeechToTextFactory.Create(_settings);
            RebuildAgentTools();
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

        private void OnBrowseMcpRegistry(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("https://registry.modelcontextprotocol.io");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to open MCP registry: " + ex.Message);
            }
        }

        private void OnApiKeyChanged(object sender, RoutedEventArgs e)
        {
            _settingsModel.ApiKey = ApiKeyBox.Password ?? "";
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
                new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Mode = "Act", Text = "Mode: Act" },
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
