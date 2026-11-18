using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Agent.Core.Context;
using Agent.Core.Models;
using Agent.Toolkit.Wpf.Controls;
using RPAWriter2.Controls;
using RPAWriter2.Services;

namespace RPAWriter2
{
    /// <summary>
    /// IChatHost implementation + ChatControl wiring. The host owns the agent loop, commit/robot
    /// services, toasts and workspace scoping; the toolkit ChatControl owns the chat view.
    /// </summary>
    public partial class MainWindowControl : IChatHost
    {
        // Host chrome injected into the ChatControl slots.
        private CommitEditorControl _commitEditor;
        private CreateRobotControl _createRobot;
        private NotificationBanner _voiceErrorToast;
        private NotificationBanner _optionsWarningToast;
        private NotificationBanner _commitResultBar;
        private NotificationBanner _checkpointRestoreErrorToast;
        private NotificationBanner _promptCacheLossToast;

        /// <summary>Wires the toolkit ChatControl to this host and injects the host slots.</summary>
        private void WireChatPanel()
        {
            ChatPanel.Host = this;
            ChatPanel.Settings = _settings;
            ChatPanel.DataContext = _chatModel;
            ChatPanel.AttachServices(_mic, _stt);
            ChatPanel.VoiceError += OnVoiceError;

            ChatPanel.MessagesList.CopyMessageRequested += OnChatMessageCopyRequested;
            ChatPanel.MessagesList.QuestionAnswered += OnChatMessageQuestionAnswered;
            ChatPanel.MessagesList.QuestionSkipped += OnChatMessageQuestionSkipped;
            ChatPanel.MessagesList.CheckpointRestoreRequested += OnCheckpointRestoreRequested;
            ChatPanel.MessagesList.DiffOpenRequested += OnDiffOpenRequested;
            ChatPanel.MessagesList.ApprovalDecided += OnChatMessageApprovalDecided;
            ChatPanel.Queue.SteerRequested += OnSteerRequested;

            _voiceErrorToast = new NotificationBanner();
            _voiceErrorToast.CloseRequested += OnVoiceErrorClose;
            _voiceErrorToast.SendToChatRequested += OnNotificationSendToChat;
            ChatPanel.VoiceErrorToastHost.Content = _voiceErrorToast;

            _optionsWarningToast = new NotificationBanner();
            _optionsWarningToast.CloseRequested += OnOptionsWarningClose;
            _optionsWarningToast.SendToChatRequested += OnNotificationSendToChat;
            ChatPanel.OptionsWarningToastHost.Content = _optionsWarningToast;

            _commitResultBar = new NotificationBanner();
            _commitResultBar.CloseRequested += OnCloseCommitResult;
            _commitResultBar.SendToChatRequested += OnNotificationSendToChat;
            ChatPanel.CommitResultBarHost.Content = _commitResultBar;

            _checkpointRestoreErrorToast = new NotificationBanner();
            _checkpointRestoreErrorToast.CloseRequested += OnCheckpointRestoreErrorClose;
            _checkpointRestoreErrorToast.SendToChatRequested += OnNotificationSendToChat;
            ChatPanel.CheckpointRestoreErrorToastHost.Content = _checkpointRestoreErrorToast;

            _promptCacheLossToast = new NotificationBanner();
            _promptCacheLossToast.CloseRequested += OnPromptCacheLossToastClose;
            ChatPanel.PromptCacheLossToastHost.Content = _promptCacheLossToast;
            ChatPanel.PromptCacheLost += OnPromptCacheLost;

            _commitEditor = new CommitEditorControl();
            _commitEditor.CommitRequested += (_, message) => CommitAsync(message);
            _commitEditor.AutoGenerateRequested += (_, __) => AutoGenerateCommitMessageAsync();
            _commitEditor.CloseRequested += (_, __) => CloseCommitControl();
            _commitEditor.SendToChatRequested += (_, message) => SubmitErrorToChatAsync(message);
            ChatPanel.CommitEditorHost.Content = _commitEditor;

            _createRobot = new CreateRobotControl();
            _createRobot.CreateRequested += (_, number) => CreateRobotAsync(number);
            ChatPanel.CreateRobotHost.Content = _createRobot;
        }

        private void OnVoiceError(object? sender, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnVoiceError(sender, message)));
                return;
            }
            _voiceErrorToast.Kind = NotificationKind.Error;
            _voiceErrorToast.Message = message ?? "";
            _voiceErrorToast.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------
        // IChatHost
        // ------------------------------------------------------------------

        public Task SendChatAsync(string text, IReadOnlyList<ChatAttachment> attachments)
            => SendChatTextAsync(text, attachments);

        public void CancelAgentRun()
        {
            _chatModel.HasResumableRun = true;
            _pendingMessages.Clear();
            _chatCts?.Cancel();
        }

        public async Task ResumeAgentRunAsync()
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

        public bool IsAgentRunning => _chatCts != null;

        public void OpenCommitControl() => _ = OpenCommitControlAsync();

        public void SendBuildErrors() => _ = SendBuildErrorsAsync();

        public void OpenCreateRobot()
        {
            UpdateCreateRobotVisibility();
        }

        public void RebuildAgentTools() => RebuildAgentToolsCore();

        public void StartNewChat() => StartNewChatCore();

        public void OpenConversation(string id) => OpenConversationCore(id);

        public void OnConversationRemoved() => ResetAgentStateAfterConversationRemoved();

        public void ShowDocsIndex() => ShowDocsIndexCore();

        public async Task CompactNowAsync()
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
                var compactedMessages = condensed.Select(m => new ChatMessage
                {
                    Kind = m.Role == "assistant" ? ChatMessageKind.AgentText
                         : m.Role == "user" ? ChatMessageKind.User
                         : ChatMessageKind.SystemNotice,
                    Text = m.Content
                }).ToList();
                _chatModel.ApplyCompactedTranscript(compactedMessages);
                _providerTranscript = (condensed ?? transcript).ToList();
                PersistProviderTranscript();
                AppendCompaction("Context compacted (summarized).");
                ScheduleHistorySave();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Compact failed: " + ex.Message);
            }
        }

        public void RequestMemoryUpdate()
        {
            ChatPanel.SetInputText(ParseMemoryBankMode() == MemoryBankMode.Convention
                ? "update memory bank: read memory-bank/*.md files with read_files, then update the relevant files with editor."
                : "update memory bank: read the current memory bank with read_memory, then update the relevant files with update_memory.");
        }

        private async Task OpenCommitControlAsync()
        {
            try
            {
                _commitEditor.SetStatus("");
                HideCommitResult();
                var solutionDir = GetOpenSolutionDirectory();
                if (solutionDir == null)
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }
                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                ShowCommitEditor(branch);
                _commitEditor.FocusMessage();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Commit control: failed to open.");
                ShowCommitWarning("Could not open the commit control: " + ex.Message);
            }
        }

        private async Task SendBuildErrorsAsync()
        {
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

        private async void CommitAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _commitEditor.SetStatus("Commit message is required.");
                _commitEditor.FocusMessage();
                return;
            }

            SetCommitBusy(true);
            _commitEditor.SetStatus("Committing…");
            try
            {
                _commitEditor.SetStatus("Saving documents…");
                if (!await TrySaveAllDocumentsAsync())
                {
                    _commitEditor.SetStatus("Some documents could not be saved. Save them manually and retry.");
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
                await _git.StageAllAsync(repoRoot);
                await _git.CommitAsync(repoRoot, message);

                _commitEditor.SetMessage("");
                CloseCommitControl();
                ShowCommitResult("Committed to " + branch + ": " + message, true);
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

        private async void AutoGenerateCommitMessageAsync()
        {
            if (_ai == null)
            {
                _commitEditor.SetStatus("AI provider is not available.");
                return;
            }

            SetCommitBusy(true);
            _commitEditor.SetStatus("Generating commit message…");
            try
            {
                _commitEditor.SetStatus("Saving documents…");
                if (!await TrySaveAllDocumentsAsync())
                {
                    _commitEditor.SetStatus("Some documents could not be saved. Save them manually and retry.");
                    return;
                }

                var solutionDir = GetOpenSolutionDirectory();
                if (solutionDir == null)
                {
                    ShowCommitWarning("No open solution. Open a solution and retry.");
                    return;
                }

                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var message = await GenerateCommitMessageAsync(repoRoot);
                if (string.IsNullOrWhiteSpace(message))
                {
                    _commitEditor.SetStatus("No changes detected. If you have unsaved edits in Visual Studio, save them (Ctrl+Shift+S) and retry.");
                    return;
                }

                _commitEditor.SetMessage(message);
                _commitEditor.SetStatus("");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Commit auto-generate failed.");
                _commitEditor.SetStatus(ex.Message);
            }
            finally
            {
                SetCommitBusy(false);
            }
        }

        private async void CreateRobotAsync(string number)
        {
            if (string.IsNullOrWhiteSpace(number) || !number.All(char.IsDigit))
            {
                AppendChat("Specify the robot number (digits only).");
                return;
            }

            var solutionName = "RPA-" + number;
            try
            {
                _outputWindow.Activate();
                var result = await _robotTemplate.CreateRobotAsync(
                    solutionName, _rpaSettings.RobotsRootPath, _rpaSettings.RobotTemplateName,
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

        private void ShowCommitEditor(string branch)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCommitEditor(branch)));
                return;
            }
            _commitEditor.ShowEditor(branch);
            ChatPanel.CommitEditorHost.Visibility = Visibility.Visible;
        }

        private void ShowCommitWarning(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCommitWarning(message)));
                return;
            }
            _commitEditor.ShowWarning(message);
            ChatPanel.CommitEditorHost.Visibility = Visibility.Visible;
        }

        private void CloseCommitControl()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CloseCommitControl));
                return;
            }
            _commitEditor.Close();
            ChatPanel.CommitEditorHost.Visibility = Visibility.Collapsed;
        }

        private void SetCommitBusy(bool busy)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetCommitBusy(busy)));
                return;
            }
            _commitEditor.SetBusy(busy);
        }

        private const int CommitResultAutoHideSeconds = 5;

        private void ShowCommitResult(string message, bool success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCommitResult(message, success)));
                return;
            }
            _commitResultBar.Kind = success ? NotificationKind.Success : NotificationKind.Error;
            _commitResultBar.Message = message;
            _commitResultBar.AutoHideSeconds = success ? CommitResultAutoHideSeconds : 0;
            _commitResultBar.Visibility = Visibility.Visible;
        }

        private void HideCommitResult()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideCommitResult));
                return;
            }
            _commitResultBar.Visibility = Visibility.Collapsed;
            _commitResultBar.Message = "";
        }

        private void OnCloseCommitResult(object sender, RoutedEventArgs e) => HideCommitResult();

        private void OnPromptCacheLost(object sender, PromptCacheLossInfo info)
        {
            if (info == null)
                return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnPromptCacheLost(sender, info)));
                return;
            }
            _promptCacheLossToast.Kind = NotificationKind.Warning;
            _promptCacheLossToast.Message = info.Message;
            _promptCacheLossToast.AutoHideSeconds = 10;
            _promptCacheLossToast.ShowClose = true;
            _promptCacheLossToast.Visibility = Visibility.Visible;
        }

        private void OnPromptCacheLossToastClose(object sender, RoutedEventArgs e)
        {
            _promptCacheLossToast.Visibility = Visibility.Collapsed;
            _promptCacheLossToast.Message = "";
        }
    }
}




