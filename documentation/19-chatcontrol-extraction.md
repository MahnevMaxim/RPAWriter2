# 19 — ChatControl extraction (Phase 3)

Goal: extract the generic agent-chat UI from `MainWindowControl` into the reusable
`Agent.Toolkit.Wpf` toolkit as a `ChatControl`, keeping RPA/VS/Git-specific pieces in the
host (`MainWindowControl`), while preserving the exact visual appearance.

## Status

- ✅ Generic controls the chat depends on already moved to the toolkit:
  - `SegmentedToggle` / `SegmentedToggleItem`
  - `TopOverflowWrapPanel`
  - Converters `AgentModeToToggleValueConverter`, `EnumMatchConverter`, `AllTrueMultiConverter`
  - Theme `AgentTheme.xaml` (brushes, `IconButtonStyle`, `ChatInputActionButtonStyle`, etc.)
- ✅ The chat tab itself (`MainWindowControl.xaml`) and its code-behind are extracted:
  - `ChatControl.xaml(.cs)` (~483 + ~730 lines) lives in `Agent.Toolkit.Wpf` and owns the full chat
    view + generic logic; the host implements `IChatHost` and injects its chrome (4 toasts,
    `CommitEditorControl`, `CreateRobotControl`) into the 6 named slots.
  - `SettingsControl.xaml(.cs)` owns the generic settings view; RPA/VS sections are host controls
    (`RpaSettingsSection`, `VsSettingsSection`) injected via `InsertHostSection` + host slots.
  - `MainWindowControl.xaml.cs` shrank ~900 lines; `MicrophoneRecorder` implements
    `IMicrophoneRecorder`.
- ✅ Repacked: `AgentToolkit`/`AgentToolkit.Wpf` **1.10.0**; `RPAWriter2.csproj` is back on NuGet
  package references.

## Why this is the hard part

`MainWindowControl.xaml.cs` is 4609 lines. The chat feature is interleaved with the other
tabs (Spec, Knowledge Base, Packages, Deploy/Logs, Settings, a Controls gallery) and with
host services: `GitService`, `CheckpointServiceFactory`, `OutputWindowService`,
`RoslynWorkspaceService`, `RobotTemplateService`, `IRpaAiProvider`, `MessageDialog` (VS),
`VsWorkspaceProvider`, `EnvDTE.DTE2`, `Package.GetGlobalService`. A clean extraction needs
a host contract so the generic `ChatControl` never references any of these.

## Proposed architecture

- `Agent.Toolkit.Wpf.Controls.ChatControl` owns:
  - The chat XAML (toolbar, history, messages list, input, Permissions/Docs overlays,
    pinned message, Cancel/Resume status, attachments).
  - All generic view logic (input, attachments, clipboard, message samples, history view,
    Permissions/Docs rendering, mode toggle, queue status).
  - A `ChatModel` + `ChatViewModel` data context (both are in `Agent.Models` / `Agent.Mvvm`,
    already referenced by the toolkit).
- `MainWindowControl` (host) keeps:
  - The agent loop (`RunSingleTurnAsync`, `RunAgentTurnAsync`, `RunResumeTurnAsync`,
    `RunSubAgentAsync`), tool registry/MCP, context budget, permissions.
  - Commit + checkpoints (Git/DTE), "Send errors" + compilation-error context, create robot,
    deploy, log analyzer, packages, workspace.
- Host ↔ ChatControl wiring:
  - **Events out** (ChatControl → host): `SendRequested`, `CommitRequested`,
    `SendErrorsRequested`, `CreateRobotRequested`, `NewChatRequested`, etc.
  - **Calls in** (host → ChatControl): `AppendChat`, `AppendChatMessage`, `ShowCommitEditor`,
    `ShowNotice`, `ShowVoiceError`, `SetMicState`, etc.
  - **Slots** (`ContentControl`): the 5 `NotificationBanner` toasts + commit editor +
    create-robot panel are host content injected into named slots.

## Generic vs host method map (chat feature)

Legend: **G** = generic (moves to `ChatControl`), **H** = host (stays), **S** = split
(signature/glue moves, body stays in host).

### Message list / transcript rendering
| Method | Class | Notes |
|---|---|---|
| `AppendChatMessage`, `AppendChat`, `AppendToolCall`, `AppendSystemMessage`, `AppendExtensionError` | G | only touch `ChatModel`/`ChatViewModel` |
| `FormatMessageForCopy` | G | pure string |
| `OnCopyConversation`, `OnCopySpecOutput`, `OnCopyMessageSample`, `OnSendMessageSample`, `OnCodeScrollPreviewMouseWheel`, `OnCopyPinnedMessage` | G | clipboard/WPF only |
| `AppendSelectionToInput` | G | input box only |
| `OnChatMessageApprovalDecided`, `OnChatMessageCopyRequested`, `OnChatMessageQuestionAnswered`, `OnChatMessageQuestionSkipped` | S | glue is G; `_chatCts`/question TCS resolution is H |

### Input / attachments / mic
| Method | Class | Notes |
|---|---|---|
| `OnSend`, `OnInputKeyDown`, `OnClearInput`, `OnDoItClick` | S | view part G; delegates to `SendMessageAsync` (H) |
| `OnAttach`, `OnRemoveAttachment`, `UpdateAttachmentsVisibility`, `IsImageExtension`, `GetImageMediaType`, `IsProbablyBinary`, `DecodeText` | G | `OpenFileDialog` (Microsoft.Win32) is G |
| `OnMicClick`, `OnMicLevelChanged` | S | mic/STT services are injected; implementations are host |
| `ShowVoiceError` | G | writes to a host slot |

### Agent loop (core host logic)
| Method | Class | Notes |
|---|---|---|
| `SendMessageAsync`, `SendChatTextAsync` | S | view-enqueue G; agent run H |
| `RunSingleTurnAsync`, `RunAgentTurnAsync`, `RunResumeTurnAsync`, `RunSubAgentAsync` | H | `AgentRunner`, `IRpaAiProvider`, tool registry |
| `EnqueuePending`, `DequeueNext`, `OnSteerRequested`, `TakeSteer` | S | queue is `ChatModel` (G); `AgentRunner` poll (H) |
| `BuildAgentToolEnvironment`, `BuildToolRegistry`, `RebuildAgentTools`, `RefreshMcpToolsAsync` | H | AgentToolFactory, DTE, build feedback, Roslyn |
| `BuildContextBudget`, `BuildSubAgentBudget`, `BuildAllowedPermissions`, `GetDisabledAgentTools`, `ParseMemoryBankMode`, `GetReadRootsForPrompt` | H | `_settings` (host `SettingsData`) |
| `OnAgentRetryOccurred`, `OnAgentToolRepetition`, `OnAgentContextCompacted`, `OnAgentCompactionStarted/Finished` | H | agent lifecycle |
| `OnAiMessageSent`, `OnPromptAssembled`, `OnRetryAttempt`, `Hook/Unhook*Notifier`, `FormatPromptDump`, `TruncatePromptDump` | H | output-window logging |
| `AskQuestionAsync`, `ApproveToolAsync`, `Confirm` | H | `MessageDialog` (VS) |
| `InvokeOnDispatcherAsync` | G | pure dispatcher |
| `OnAgentToolExecuted` | S | `AppendToolCall` G; LogService G |

### Commit / checkpoints (host)
| Method | Class | Notes |
|---|---|---|
| `OnCommit`, `OnCommitSend`, `OnCommitAutoGenerate`, `GenerateCommitMessageAsync`, `TrySaveAllDocumentsAsync`, `OnCommitClose`, `ShowCommitEditor`, `ShowCommitWarning`, `ShowCommitResult`, `HideCommitResult`, `OnCloseCommitResult`, `CloseCommitControl`, `SetCommitBusy` | H | Git + DTE; the commit editor UI itself becomes a host control/slot |
| `OnCheckpointRestoreRequested`, `GetCheckpointServiceAsync`, `GetCheckpointServiceForCurrentTaskAsync`, `FindPreviousCheckpoint`, `AttachDiffToCheckpoint`, `SaveCheckpointAsync`, `AppendCheckpointIndicator`, `RehydrateCheckpoints`, `RehydrateCheckpointMetadata`, `GetLastCheckpointHash`, `OnDiffOpenRequested` | H | checkpoints/Git |
| `ShowCheckpointRestoreError` | H | toast (host slot) |

### Send errors / compilation errors (host)
| Method | Class |
|---|---|
| `OnSendErrors`, `BuildCompilationErrorsContextAsync`, `FormatNoCompilationErrorsAsync`, `OnNotificationSendToChat` | H |

### Create robot / deploy / logs / packages (host)
| Method | Class |
|---|---|
| `OnCreateRobot`, `UpdateCreateRobotVisibility`, `OnRobotNumberPreviewTextInput` | H |
| `OnDeploy`, `OnAnalyzeLogs*`, `OnLoadProdTasks`, `OnTaskSelected`, `OnMinLevelChanged`, `OnDateRangeChanged`, `LoadProdTasksFromModelAsync`, `RefreshLogsFromModelAsync`, `OnRestorePackages`, `OnRefreshPackages`, `OnInstallSelectedPackages`, `OnUpdateSelectedPackages`, `OnUninstallSelectedPackages`, `OnPackageFilterChanged`, `RefreshPackagesAsync` | H |

### History / workspace / tabs
| Method | Class | Notes |
|---|---|---|
| `InitializeChatHistory`, `RestoreUiState`, `ScheduleHistorySave`, `OnHistorySaveTick`, `PersistUiState`, `SelectTabByHeader` | S | persistence is `ChatModel` (G) but tab switching touches host tabs |
| `ShowConversationView`, `ShowHistoryView`, `ApplyChatViewMode`, `OnCloseChat`, `OnShowHistory`, `OnHistoryCardClick`, `OnDeleteHistoryCard`, `OnToggleImportant` | G | pure chat view |
| `StartNewChat`, `EnsureChatReadyToReceive` | S | `ChatModel` G; `ApplyWorkspaceScope` H |
| `OnWorkspaceChanged`, `OnWorkspacePollTick`, `UpdateWorkspaceIndicator`, `OnResetWorkspace`, `ApplyWorkspaceScope`, `ResetProviderTranscript` | H | `VsWorkspaceProvider`, `AppDataPaths` |
| `OnMainTabsSelectionChanged`, `OnWindowUnloaded` | H | host tabs/DTE |
| `RefreshPinnedMessage`, `UpdatePinnedExpandState`, `OnPinnedHeaderClick` | G | pure view |

### Permissions / Docs overlays
| Method | Class | Notes |
|---|---|---|
| `OnPermissionsClick`, `OnPermissionsClose`, `OnPermissionChanged` | S | checkboxes + `SettingsStore.Save` G (Agent.Core); `RebuildAgentTools` H |
| `OnDocsClick`, `OnDocsHome`, `OnDocsClose`, `ShowDocsIndex`, `HandleDocsLink`, `RenderDocsFile` | G | fully generic — `HelpDocumentation` is in Agent.Core.Services |
| `OnUpdateMemory`, `UpdateMemoryBankUi`, `OnCompactNow` | S | `_chatModel` G; `RebuildAgentTools`/agent H |

### Status / notice / cancel / resume
| Method | Class | Notes |
|---|---|---|
| `ShowError`, `ShowNotice`, `HideNotice`, `OnNoticeClose`, `ShowChatStatusError` | G | toasts are host slots |
| `OnCancelSend`, `OnResumeTask`, `RemoveCancelledNotice`, `RemoveStaleThinkingPlaceholder`, `ResetResume` | S | `_chatModel` G; `_chatCts`/`_agentRunner` H |

### Non-chat (stay in host, do not touch)
Spec tab (`OnSpecTabNotify`, `SendChatPromptDetached*`, `OnAnalyzeLogsFromOutputWindow`), Knowledge Base (`OnKnowledgeBaseNotify`, `OnKnowledgeBaseModelPropertyChanged`), `WireSettingsHostChrome`, `OnResetWorkspace`, settings toast methods (`ShowSettingsToast`/`HideSettingsToast`/`OnSettingsToastTick`), and the Controls gallery (`MessageSample`, `SvgIconSample`, `PaletteSample`).

## Host slots (ContentControl) for the ChatControl

1. `VoiceErrorToastSlot` — `NotificationBanner` (voice errors)
2. `OptionsWarningToastSlot` — `NotificationBanner` (options/notice)
3. `CommitResultBarSlot` — `NotificationBanner` (commit result)
4. `CheckpointRestoreErrorToastSlot` — `NotificationBanner` (checkpoint restore errors)
5. `CommitEditorSlot` — commit editor panel (message box, generate, diff/compare buttons + its own warning bar)
6. `CreateRobotPanelSlot` — create-robot panel

## Recommended build order

1. Create `Agent.Toolkit.Wpf/Controls/ChatControl.xaml(.cs)` with the full chat XAML + generic
   view logic, exposing the `IChatHost` contract (events + `AppendChat*` + slot injection).
2. Move the commit editor and create-robot panel into small host controls
   (`CommitEditorControl`, `CreateRobotControl`) injected into the slots.
3. Replace the Chat `TabItem` in `MainWindowControl.xaml` with `<toolkit:ChatControl/>` and
   rewrite `MainWindowControl.xaml.cs` to implement `IChatHost` (agent loop, commit,
   checkpoints, errors, create robot).
4. Rebuild after each step; compare visuals against a pre-change VSIX.


