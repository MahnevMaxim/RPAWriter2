using System;
using System.Collections.Generic;
using System.IO;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// Settings tab model: owns the editable extension settings and the save scenario.
    /// <para>
    /// The model is UI-free by contract (<c>documentation/13-model-layer.md</c>): it is constructed
    /// and asserted without a view model and without a view. Persistence is injected through
    /// <see cref="ISettingsStore"/>. Every editable value is an observable property so the XAML
    /// binds TwoWay and the code-behind no longer pushes/pulls control values.
    /// </para>
    /// </summary>
    public sealed class SettingsModel : BaseModel
    {
        private readonly ISettingsStore _store;

        private string _baseUrl = "https://api.openai.com/v1";
        private string _apiKey = "";
        private string _model = "gpt-4o-mini";
        private double _temperature = 0.2;
        private string _reasoningEffort = "";
        private string _knowledgeBasePath = "";
        private string _robotsRootPath = "";
        private string _robotTemplateName = "";
        private bool _showRobotCreator = true;
        private string _packagesPath = "";
        private string _rulesPath = "";
        private string _skillsPath = "";
        private int _contextWindowSize = 409600;
        private int _timeoutSeconds = 300;
        private int _retryTimeoutMinutes = 30;
        private int _retryBaseDelaySeconds = 1;
        private int _retryMaxDelaySeconds = 30;
        private double _inputPricePerMillion = 0.27;
        private double _cacheHitPricePerMillion = 0.07;
        private double _outputPricePerMillion = 1.10;
        private bool _autoCompact = true;
        private double _compactThreshold = 0.85;
        private double _reserveFraction = 0.12;
        private int _keepRecentMessages = 6;
        private int _toolResultMaxChars = 12000;
        private bool _useToolRole = true;
        private bool _showControls = false;
        private string _toolCallingMode = "JsonChat";
        private string _memoryBankMode = "Disabled";
        private string _securityAuditMode = "Full";
        private int _maxIterations = 8;
        private int _maxStagnantRepeats = 3;
        private bool _stagnantLoopNudge = false;
        private bool _allowReadFiles = true;
        private bool _allowEditFiles = true;
        private bool _allowExecuteCommand = false;
        private bool _allowWebFetch = false;
        private bool _allowMcp = false;
        private bool _allowSubagents = false;
        private int _subagentMaxIterations = 4;
        private int _subagentMaxDepth = 2;
        private string _userName = "";
        private bool _addressByName = false;
        private string _logFilePath = "";
        private string _chatLogFilePath = "";
        private string _logProvidersPath = "";
        private string _speechToTextProvider = "Auto";
        private string _speechToTextModel = "whisper-1";
        private string _speechToTextLanguage = "ru";
        private string _voskModelPath = "";
        private bool _speechAutoSend = false;
        private bool _autoCaptureRuntimeExceptions = true;
        private bool _checkpointsEnabled = true;
        private List<string> _robotNameHistory = new List<string>();

        private readonly IWorkspaceLogSettingsStore _workspaceLogStore;
        private readonly IWorkspaceProvider _workspaceProvider;
        private readonly ISecretsStore _secretsStore;
        private readonly Func<IAiProvider> _aiProvider;
        private readonly IFolderPicker _folderPicker;
        private readonly ISolutionPathProvider _solutionPathProvider;
        private readonly IFileWriter _fileWriter;
        private readonly IFileOpener _fileOpener;
        private readonly Func<string> _logFilePathResolver;
        private readonly Func<string> _chatLogFilePathResolver;

        // Global fallback values of the log paths, kept so Save can write them back unchanged when
        // the effective (UI) values are workspace-scoped instead.
        private string _globalLogFilePath = "";
        private string _globalChatLogFilePath = "";

        /// <summary>Raised after the settings were normalized and persisted.</summary>
        public event EventHandler SettingsSaved;

        public SettingsModel(
            ISettingsStore settingsStore,
            IWorkspaceLogSettingsStore workspaceLogStore = null,
            IWorkspaceProvider workspaceProvider = null,
            ISecretsStore secretsStore = null,
            Func<IAiProvider> aiProvider = null,
            IFolderPicker folderPicker = null,
            ISolutionPathProvider solutionPathProvider = null,
            IFileWriter fileWriter = null,
            IFileOpener fileOpener = null,
            Func<string> logFilePathResolver = null,
            Func<string> chatLogFilePathResolver = null)
        {
            _store = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _workspaceLogStore = workspaceLogStore;
            _workspaceProvider = workspaceProvider;
            _secretsStore = secretsStore;
            _aiProvider = aiProvider;
            _folderPicker = folderPicker;
            _solutionPathProvider = solutionPathProvider;
            _fileWriter = fileWriter;
            _fileOpener = fileOpener;
            _logFilePathResolver = logFilePathResolver;
            _chatLogFilePathResolver = chatLogFilePathResolver;
        }

        /// <summary>Loads persisted settings into the observable properties.</summary>
        public override void Run() => Load();

        /// <summary>Reads the persisted settings and applies them to the observable properties.</summary>
        public void Load()
        {
            var data = _store.Load();
            Apply(data);

            _globalLogFilePath = data?.LogFilePath ?? "";
            _globalChatLogFilePath = data?.ChatLogFilePath ?? "";

            ApplyWorkspaceLogSettings();

            // The API key lives in the secrets store, not in the settings JSON. Fall back to the
            // legacy JSON value so an existing key is migrated transparently on the first save.
            var secret = _secretsStore?.LoadApiKey();
            ApiKey = !string.IsNullOrEmpty(secret) ? secret : (data?.ApiKey ?? "");
        }

        /// <summary>Builds a <see cref="SettingsData"/> snapshot from the current property values.</summary>
        public SettingsData ToSettingsData()
        {
            return new SettingsData
            {
                BaseUrl = _baseUrl,
                ApiKey = _apiKey,
                Model = _model,
                Temperature = _temperature,
                ReasoningEffort = _reasoningEffort,
                KnowledgeBasePath = _knowledgeBasePath,
                RobotsRootPath = _robotsRootPath,
                RobotTemplateName = _robotTemplateName,
                ShowRobotCreator = _showRobotCreator,
                PackagesPath = _packagesPath,
                RulesPath = _rulesPath,
                SkillsPath = _skillsPath,
                ContextWindowSize = _contextWindowSize,
                TimeoutSeconds = _timeoutSeconds,
                RetryTimeoutMinutes = _retryTimeoutMinutes,
                RetryBaseDelaySeconds = _retryBaseDelaySeconds,
                RetryMaxDelaySeconds = _retryMaxDelaySeconds,
                InputPricePerMillion = _inputPricePerMillion,
                CacheHitPricePerMillion = _cacheHitPricePerMillion,
                OutputPricePerMillion = _outputPricePerMillion,
                AutoCompact = _autoCompact,
                CompactThreshold = _compactThreshold,
                ReserveFraction = _reserveFraction,
                KeepRecentMessages = _keepRecentMessages,
                ToolResultMaxChars = _toolResultMaxChars,
                UseToolRole = _useToolRole,
                ShowControls = _showControls,
                ToolCallingMode = _toolCallingMode,
                MemoryBankMode = _memoryBankMode,
                SecurityAuditMode = _securityAuditMode,
                MaxIterations = _maxIterations,
                MaxStagnantRepeats = _maxStagnantRepeats,
                StagnantLoopNudge = _stagnantLoopNudge,
                AllowReadFiles = _allowReadFiles,
                AllowEditFiles = _allowEditFiles,
                AllowExecuteCommand = _allowExecuteCommand,
                AllowWebFetch = _allowWebFetch,
                AllowMcp = _allowMcp,
                AllowSubagents = _allowSubagents,
                SubagentMaxIterations = _subagentMaxIterations,
                SubagentMaxDepth = _subagentMaxDepth,
                UserName = _userName,
                AddressByName = _addressByName,
                LogFilePath = _logFilePath,
                ChatLogFilePath = _chatLogFilePath,
                LogProvidersPath = _logProvidersPath,
                SpeechToTextProvider = _speechToTextProvider,
                SpeechToTextModel = _speechToTextModel,
                SpeechToTextLanguage = _speechToTextLanguage,
                VoskModelPath = _voskModelPath,
                SpeechAutoSend = _speechAutoSend,
                AutoCaptureRuntimeExceptions = _autoCaptureRuntimeExceptions,
                CheckpointsEnabled = _checkpointsEnabled,
                RobotNameHistory = new List<string>(_robotNameHistory)
            };
        }

        /// <summary>Validates, persists and raises <see cref="SettingsSaved"/>.</summary>
        public void SaveSettingsCommand()
        {
            var data = ToSettingsData();
            // The log analyzer's settings seam may have remembered a robot name since this model
            // loaded; re-read the persisted history so Save never clobbers it.
            data.RobotNameHistory = _store.Load().RobotNameHistory;
            SettingsData.Normalize(data);
            Apply(data);

            // Split persistence: log paths go to the workspace scope (when a workspace is open),
            // the API key goes to the secrets store, everything else stays global.
            var workspace = _workspaceProvider?.GetCurrentWorkspace();
            if (workspace != null && _workspaceLogStore != null)
            {
                _workspaceLogStore.Save(new WorkspaceLogSettings
                {
                    LogFilePath = _logFilePath,
                    ChatLogFilePath = _chatLogFilePath
                });

                // Preserve the global fallback values instead of duplicating the workspace ones.
                data.LogFilePath = _globalLogFilePath;
                data.ChatLogFilePath = _globalChatLogFilePath;
            }

            data.ApiKey = "";
            if (_secretsStore != null)
            {
                if (string.IsNullOrEmpty(_apiKey))
                    _secretsStore.ClearApiKey();
                else
                    _secretsStore.SaveApiKey(_apiKey);
            }

            _store.Save(data);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Re-applies the workspace-scoped log settings after the open solution changed. The host
        /// calls this on <see cref="IWorkspaceProvider.WorkspaceChanged"/> so the effective log paths
        /// follow the newly opened/closed workspace.
        /// </summary>
        public void ReloadWorkspaceScope() => ApplyWorkspaceLogSettings();

        /// <summary>
        /// Composes the effective log paths: workspace values (when set) win over the global
        /// fallback; the global fallback wins over the default "next to the extension".
        /// </summary>
        private void ApplyWorkspaceLogSettings()
        {
            var workspaceLogs = LoadWorkspaceLogSettings();

            LogFilePath = FirstNonEmpty(workspaceLogs.LogFilePath, _globalLogFilePath);
            ChatLogFilePath = FirstNonEmpty(workspaceLogs.ChatLogFilePath, _globalChatLogFilePath);
        }

        private WorkspaceLogSettings LoadWorkspaceLogSettings()
        {
            if (_workspaceProvider?.GetCurrentWorkspace() == null || _workspaceLogStore == null)
                return new WorkspaceLogSettings();

            try
            {
                return _workspaceLogStore.Load() ?? new WorkspaceLogSettings();
            }
            catch
            {
                return new WorkspaceLogSettings();
            }
        }

        private static string FirstNonEmpty(string first, string second)
            => string.IsNullOrEmpty(first) ? (second ?? "") : first;


        private void Apply(SettingsData data)
        {
            if (data == null)
                return;

            BaseUrl = data.BaseUrl;
            ApiKey = data.ApiKey;
            Model = data.Model;
            Temperature = data.Temperature;
            ReasoningEffort = data.ReasoningEffort;
            KnowledgeBasePath = data.KnowledgeBasePath;
            RobotsRootPath = data.RobotsRootPath;
            RobotTemplateName = data.RobotTemplateName;
            ShowRobotCreator = data.ShowRobotCreator;
            PackagesPath = data.PackagesPath;
            RulesPath = data.RulesPath;
            SkillsPath = data.SkillsPath;
            ContextWindowSize = data.ContextWindowSize;
            TimeoutSeconds = data.TimeoutSeconds;
            RetryTimeoutMinutes = data.RetryTimeoutMinutes;
            RetryBaseDelaySeconds = data.RetryBaseDelaySeconds;
            RetryMaxDelaySeconds = data.RetryMaxDelaySeconds;
            InputPricePerMillion = data.InputPricePerMillion;
            CacheHitPricePerMillion = data.CacheHitPricePerMillion;
            OutputPricePerMillion = data.OutputPricePerMillion;
            AutoCompact = data.AutoCompact;
            CompactThreshold = data.CompactThreshold;
            ReserveFraction = data.ReserveFraction;
            KeepRecentMessages = data.KeepRecentMessages;
            ToolResultMaxChars = data.ToolResultMaxChars;
            UseToolRole = data.UseToolRole;
            ShowControls = data.ShowControls;
            ToolCallingMode = data.ToolCallingMode;
            MemoryBankMode = data.MemoryBankMode;
            SecurityAuditMode = data.SecurityAuditMode;
            MaxIterations = data.MaxIterations;
            MaxStagnantRepeats = data.MaxStagnantRepeats;
            StagnantLoopNudge = data.StagnantLoopNudge;
            AllowReadFiles = data.AllowReadFiles;
            AllowEditFiles = data.AllowEditFiles;
            AllowExecuteCommand = data.AllowExecuteCommand;
            AllowWebFetch = data.AllowWebFetch;
            AllowMcp = data.AllowMcp;
            AllowSubagents = data.AllowSubagents;
            SubagentMaxIterations = data.SubagentMaxIterations;
            SubagentMaxDepth = data.SubagentMaxDepth;
            UserName = data.UserName;
            AddressByName = data.AddressByName;
            LogFilePath = data.LogFilePath;
            ChatLogFilePath = data.ChatLogFilePath;
            LogProvidersPath = data.LogProvidersPath;
            SpeechToTextProvider = data.SpeechToTextProvider;
            SpeechToTextModel = data.SpeechToTextModel;
            SpeechToTextLanguage = data.SpeechToTextLanguage;
            VoskModelPath = data.VoskModelPath;
            SpeechAutoSend = data.SpeechAutoSend;
            AutoCaptureRuntimeExceptions = data.AutoCaptureRuntimeExceptions;
            CheckpointsEnabled = data.CheckpointsEnabled;
            _robotNameHistory = data.RobotNameHistory == null
                ? new List<string>()
                : new List<string>(data.RobotNameHistory);
        }

        /// <summary>Formatted temperature for the slider caption (single decimal).</summary>
        public string TemperatureText => _temperature.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        #region File/prompt utility commands

        /// <summary>Command target: re-read the prompt templates from disk (when the provider supports it).</summary>
        public void RefreshPromptsFromDiskCommand()
        {
            var ai = _aiProvider?.Invoke();
            if (ai is IPromptReloadable reloadable)
            {
                reloadable.ReloadPromptsFromDisk();
                NotifyMessage(new Notify
                {
                    Message = "Prompts reloaded from disk: " + PromptPaths.ResolvePromptsDirectory(),
                    NotifyLevel = NotifyLevel.Success
                });
            }
            else
            {
                NotifyMessage(new Notify
                {
                    Message = "Prompts reload is unavailable for the current provider (stub/offline).",
                    NotifyLevel = NotifyLevel.Warning
                });
            }
        }

        /// <summary>Command target: copy the shipped user documentation to a chosen folder.</summary>
        public void ExportDocumentationCommand()
            => ExportDirectory(HelpDocumentation.ResolveDocsDirectory(), "Docs", "Documentation");

        /// <summary>Command target: copy the shipped prompt templates to a chosen folder.</summary>
        public void ExportPromptsCommand()
            => ExportDirectory(PromptPaths.ResolvePromptsDirectory(), "Prompts", "Prompts");

        /// <summary>Command target: open the extension log file in an external editor.</summary>
        public void OpenLogCommand()
            => OpenLogFile(ResolveLogFilePath(_logFilePathResolver, LogFilePath), "Log file path is not available.", "log");

        /// <summary>Command target: open the chat log file in an external editor.</summary>
        public void OpenChatLogCommand()
            => OpenLogFile(ResolveLogFilePath(_chatLogFilePathResolver, ChatLogFilePath), "Chat log file path is not available.", "chat log");

        #endregion

        #region File/prompt utility helpers

        private string ResolveLogFilePath(Func<string> resolver, string effective)
        {
            if (resolver != null)
                return resolver() ?? "";
            return effective ?? "";
        }

        private void ExportDirectory(string sourceDir, string targetSubFolder, string label)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !DirectoryExists(sourceDir))
            {
                NotifyMessage(new Notify { Message = label + " directory not found.", NotifyLevel = NotifyLevel.Warning });
                return;
            }

            if (_folderPicker == null)
                return;

            var initial = _solutionPathProvider?.GetSolutionDirectory() ?? "";
            var targetRoot = _folderPicker.PickFolder("Select a folder to export " + label + " to", initial);
            if (string.IsNullOrWhiteSpace(targetRoot))
                return;

            try
            {
                var targetDir = Path.Combine(targetRoot, targetSubFolder);
                CopyDirectory(sourceDir, targetDir);
                NotifyMessage(new Notify { Message = label + " exported to " + targetDir, NotifyLevel = NotifyLevel.Success });
            }
            catch (Exception ex)
            {
                NotifyMessage(new Notify { Message = "Failed to export " + label + ": " + ex.Message, NotifyLevel = NotifyLevel.Error });
            }
        }

        private bool DirectoryExists(string path)
            => _fileWriter != null ? _fileWriter.DirectoryExists(path) : Directory.Exists(path);

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            if (_fileWriter == null)
                return;

            _fileWriter.CreateDirectory(targetDir);

            foreach (var file in _fileWriter.EnumerateFiles(sourceDir))
            {
                var relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dest = Path.Combine(targetDir, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    _fileWriter.CreateDirectory(destDir);
                _fileWriter.CopyFile(file, dest, overwrite: true);
            }
        }

        private void OpenLogFile(string logFile, string unavailableMessage, string logKind)
        {
            if (string.IsNullOrWhiteSpace(logFile))
            {
                NotifyMessage(new Notify { Message = unavailableMessage, NotifyLevel = NotifyLevel.Warning });
                return;
            }

            try
            {
                if (_fileWriter != null)
                {
                    var dir = Path.GetDirectoryName(logFile);
                    if (!string.IsNullOrEmpty(dir))
                        _fileWriter.CreateDirectory(dir);
                    if (!_fileWriter.FileExists(logFile))
                        _fileWriter.WriteAllText(logFile, "");
                }

                _fileOpener?.Open(logFile);
            }
            catch (Exception ex)
            {
                NotifyMessage(new Notify { Message = "Failed to open " + logKind + ": " + ex.Message, NotifyLevel = NotifyLevel.Error });
            }
        }

        #endregion

        #region Properties

        public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }

        public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }

        public string Model { get => _model; set => SetProperty(ref _model, value); }

        public double Temperature
        {
            get => _temperature;
            set
            {
                if (SetProperty(ref _temperature, value))
                    OnPropertyChanged(nameof(TemperatureText));
            }
        }

        public string ReasoningEffort
        {
            get => _reasoningEffort;
            set
            {
                if (SetProperty(ref _reasoningEffort, value))
                    OnPropertyChanged(nameof(ReasoningEffortIndex));
            }
        }

        public string KnowledgeBasePath { get => _knowledgeBasePath; set => SetProperty(ref _knowledgeBasePath, value); }

        public string RobotsRootPath { get => _robotsRootPath; set => SetProperty(ref _robotsRootPath, value); }

        public string RobotTemplateName { get => _robotTemplateName; set => SetProperty(ref _robotTemplateName, value); }

        public bool ShowRobotCreator { get => _showRobotCreator; set => SetProperty(ref _showRobotCreator, value); }

        public string PackagesPath { get => _packagesPath; set => SetProperty(ref _packagesPath, value); }

        public string RulesPath { get => _rulesPath; set => SetProperty(ref _rulesPath, value); }

        public string SkillsPath { get => _skillsPath; set => SetProperty(ref _skillsPath, value); }

        public int ContextWindowSize { get => _contextWindowSize; set => SetProperty(ref _contextWindowSize, value); }

        public int TimeoutSeconds { get => _timeoutSeconds; set => SetProperty(ref _timeoutSeconds, value); }

        public int RetryTimeoutMinutes { get => _retryTimeoutMinutes; set => SetProperty(ref _retryTimeoutMinutes, value); }

        public int RetryBaseDelaySeconds { get => _retryBaseDelaySeconds; set => SetProperty(ref _retryBaseDelaySeconds, value); }

        public int RetryMaxDelaySeconds { get => _retryMaxDelaySeconds; set => SetProperty(ref _retryMaxDelaySeconds, value); }

        public double InputPricePerMillion { get => _inputPricePerMillion; set => SetProperty(ref _inputPricePerMillion, value); }

        public double CacheHitPricePerMillion { get => _cacheHitPricePerMillion; set => SetProperty(ref _cacheHitPricePerMillion, value); }

        public double OutputPricePerMillion { get => _outputPricePerMillion; set => SetProperty(ref _outputPricePerMillion, value); }

        public bool AutoCompact { get => _autoCompact; set => SetProperty(ref _autoCompact, value); }

        public double CompactThreshold { get => _compactThreshold; set => SetProperty(ref _compactThreshold, value); }

        public double ReserveFraction { get => _reserveFraction; set => SetProperty(ref _reserveFraction, value); }

        public int KeepRecentMessages { get => _keepRecentMessages; set => SetProperty(ref _keepRecentMessages, value); }

        public int ToolResultMaxChars { get => _toolResultMaxChars; set => SetProperty(ref _toolResultMaxChars, value); }

        public bool UseToolRole { get => _useToolRole; set => SetProperty(ref _useToolRole, value); }

        public bool ShowControls { get => _showControls; set => SetProperty(ref _showControls, value); }

        public string ToolCallingMode
        {
            get => _toolCallingMode;
            set
            {
                if (SetProperty(ref _toolCallingMode, value))
                    OnPropertyChanged(nameof(ToolCallingModeIndex));
            }
        }

        public string MemoryBankMode
        {
            get => _memoryBankMode;
            set
            {
                if (SetProperty(ref _memoryBankMode, value))
                    OnPropertyChanged(nameof(MemoryBankModeIndex));
            }
        }

        public string SecurityAuditMode
        {
            get => _securityAuditMode;
            set
            {
                if (SetProperty(ref _securityAuditMode, value))
                    OnPropertyChanged(nameof(SecurityAuditModeIndex));
            }
        }

        public int MaxIterations { get => _maxIterations; set => SetProperty(ref _maxIterations, value); }

        public int MaxStagnantRepeats { get => _maxStagnantRepeats; set => SetProperty(ref _maxStagnantRepeats, value); }

        public bool StagnantLoopNudge { get => _stagnantLoopNudge; set => SetProperty(ref _stagnantLoopNudge, value); }

        public bool AllowReadFiles { get => _allowReadFiles; set => SetProperty(ref _allowReadFiles, value); }

        public bool AllowEditFiles { get => _allowEditFiles; set => SetProperty(ref _allowEditFiles, value); }

        public bool AllowExecuteCommand { get => _allowExecuteCommand; set => SetProperty(ref _allowExecuteCommand, value); }

        public bool AllowWebFetch { get => _allowWebFetch; set => SetProperty(ref _allowWebFetch, value); }

        public bool AllowMcp { get => _allowMcp; set => SetProperty(ref _allowMcp, value); }

        public bool AllowSubagents { get => _allowSubagents; set => SetProperty(ref _allowSubagents, value); }

        public int SubagentMaxIterations { get => _subagentMaxIterations; set => SetProperty(ref _subagentMaxIterations, value); }

        public int SubagentMaxDepth { get => _subagentMaxDepth; set => SetProperty(ref _subagentMaxDepth, value); }

        public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

        public bool AddressByName { get => _addressByName; set => SetProperty(ref _addressByName, value); }

        public string LogFilePath { get => _logFilePath; set => SetProperty(ref _logFilePath, value); }

        public string ChatLogFilePath { get => _chatLogFilePath; set => SetProperty(ref _chatLogFilePath, value); }

        public string LogProvidersPath { get => _logProvidersPath; set => SetProperty(ref _logProvidersPath, value); }

        public string SpeechToTextProvider
        {
            get => _speechToTextProvider;
            set
            {
                if (SetProperty(ref _speechToTextProvider, value))
                    OnPropertyChanged(nameof(SpeechToTextProviderIndex));
            }
        }

        public string SpeechToTextModel { get => _speechToTextModel; set => SetProperty(ref _speechToTextModel, value); }

        public string SpeechToTextLanguage { get => _speechToTextLanguage; set => SetProperty(ref _speechToTextLanguage, value); }

        public string VoskModelPath { get => _voskModelPath; set => SetProperty(ref _voskModelPath, value); }

        public bool SpeechAutoSend { get => _speechAutoSend; set => SetProperty(ref _speechAutoSend, value); }

        public bool AutoCaptureRuntimeExceptions { get => _autoCaptureRuntimeExceptions; set => SetProperty(ref _autoCaptureRuntimeExceptions, value); }

        public bool CheckpointsEnabled { get => _checkpointsEnabled; set => SetProperty(ref _checkpointsEnabled, value); }

        /// <summary>Recently used production robot names (managed by the log analyzer, persisted on save).</summary>
        public IReadOnlyList<string> RobotNameHistory => _robotNameHistory;

        /// <summary>Combo index of <see cref="ToolCallingMode"/>: 0 = JsonChat, 1 = Native.</summary>
        public int ToolCallingModeIndex
        {
            get => string.Equals(_toolCallingMode, "Native", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            set => ToolCallingMode = value == 1 ? "Native" : "JsonChat";
        }

        /// <summary>Combo index of <see cref="MemoryBankMode"/>: 0 = Disabled, 1 = Tool, 2 = Convention.</summary>
        public int MemoryBankModeIndex
        {
            get
            {
                if (string.Equals(_memoryBankMode, "Tool", StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(_memoryBankMode, "Convention", StringComparison.OrdinalIgnoreCase)) return 2;
                return 0;
            }
            set => MemoryBankMode = MemoryBankIndexToMode(value);
        }

        /// <summary>Combo index of <see cref="SecurityAuditMode"/>: 0 = Full, 1 = CredentialsOnly.</summary>
        public int SecurityAuditModeIndex
        {
            get => string.Equals(_securityAuditMode, "CredentialsOnly", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            set => SecurityAuditMode = value == 1 ? "CredentialsOnly" : "Full";
        }

        /// <summary>Combo index of <see cref="ReasoningEffort"/>: 0 = Default, 1..7 = none..max.</summary>
        public int ReasoningEffortIndex
        {
            get
            {
                if (string.IsNullOrEmpty(_reasoningEffort)) return 0;
                if (string.Equals(_reasoningEffort, "none", StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(_reasoningEffort, "minimal", StringComparison.OrdinalIgnoreCase)) return 2;
                if (string.Equals(_reasoningEffort, "low", StringComparison.OrdinalIgnoreCase)) return 3;
                if (string.Equals(_reasoningEffort, "medium", StringComparison.OrdinalIgnoreCase)) return 4;
                if (string.Equals(_reasoningEffort, "high", StringComparison.OrdinalIgnoreCase)) return 5;
                if (string.Equals(_reasoningEffort, "xhigh", StringComparison.OrdinalIgnoreCase)) return 6;
                if (string.Equals(_reasoningEffort, "max", StringComparison.OrdinalIgnoreCase)) return 7;
                return 0;
            }
            set => ReasoningEffort = ReasoningEffortIndexToName(value);
        }

        /// <summary>Combo index of <see cref="SpeechToTextProvider"/>: 0 = Auto, 1 = OpenAiWhisper, 2 = Vosk.</summary>
        public int SpeechToTextProviderIndex
        {
            get
            {
                if (string.Equals(_speechToTextProvider, "OpenAiWhisper", StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(_speechToTextProvider, "Vosk", StringComparison.OrdinalIgnoreCase)) return 2;
                return 0;
            }
            set => SpeechToTextProvider = SpeechToTextProviderIndexToName(value);
        }

        private static string MemoryBankIndexToMode(int index)
        {
            if (index == 2) return "Convention";
            if (index == 1) return "Tool";
            return "Disabled";
        }

        private static string ReasoningEffortIndexToName(int index)
        {
            switch (index)
            {
                case 1: return "none";
                case 2: return "minimal";
                case 3: return "low";
                case 4: return "medium";
                case 5: return "high";
                case 6: return "xhigh";
                case 7: return "max";
                default: return "";
            }
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

        #endregion Properties
    }
}
