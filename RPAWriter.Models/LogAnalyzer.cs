using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// "Analyze logs" model: what the Analyze-logs panel does from the moment the user opens it.
    /// It owns the discovered providers, the robot-name filter, the task/log lists and the two
    /// analysis scenarios (Output window, production servers).
    /// <para>
    /// The model is UI-free by contract (<c>documentation/13-model-layer.md</c>): it is constructed
    /// and asserted in tests without a <see cref="BaseViewModel"/> and without a view. Panel
    /// visibility, focus and combo wiring stay in the view; everything external (provider discovery,
    /// Output window text, chat prompt delivery, settings) is injected as an interface.
    /// </para>
    /// </summary>
    public sealed class LogAnalyzer : BaseModel
    {
        #region Messages

        /// <summary>Shown when the robot name box is empty.</summary>
        public const string EnterRobotNameMessage = "Enter a robot name (e.g. RPA-1212).";

        /// <summary>Shown when no provider could be found at all.</summary>
        public const string NoProviderMessage =
            "No log provider found. Place a provider DLL into the \"Log providers directory\" (Settings) and retry.";

        /// <summary>Shown when the robot name is known but no provider is selected.</summary>
        public const string NoProviderAvailableMessage = "No log provider available.";

        /// <summary>Shown when "Analyze" is pressed before any log was loaded.</summary>
        public const string LoadLogsFirstMessage = "Load logs first.";

        /// <summary>Shown when the loaded task has no log entries.</summary>
        public const string NoLogsToAnalyzeMessage = "No log entries to analyze.";

        /// <summary>Posted into the chat when the Output window has no text.</summary>
        public const string EmptyOutputWindowMessage = "The Visual Studio Output window is empty.";

        #endregion Messages

        private readonly ILogProviderSource _providerSource;
        private readonly IOutputWindowTextSource _outputWindow;
        private readonly IChatPromptSink _chatSink;
        private readonly IUserMessageSink _userMessages;
        private readonly ILogAnalysisSettings _settings;
        private readonly ProdLogAnalysisService _analysis;

        private IProdLogProvider _selectedProvider;
        private ProdTask _selectedTask;
        private string _statusText = "";
        private bool _isBusy;
        private string _robotName = "";
        private LogLevelOption _selectedMinLevel;
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private bool _providersLoaded;
        private bool _logsLoaded;
        private bool _settingsLoaded;
        private string _lastError = "";

        /// <summary>
        /// Creates the model. Every dependency is an interface: the model never reads the file
        /// system, the Output window or the chat collections on its own.
        /// </summary>
        public LogAnalyzer(
            ILogProviderSource providerSource,
            IOutputWindowTextSource outputWindow,
            IChatPromptSink chatSink,
            IUserMessageSink userMessages,
            ILogAnalysisSettings settings,
            ProdLogAnalysisService analysis = null)
        {
            _providerSource = providerSource ?? throw new ArgumentNullException(nameof(providerSource));
            _outputWindow = outputWindow ?? throw new ArgumentNullException(nameof(outputWindow));
            _chatSink = chatSink ?? throw new ArgumentNullException(nameof(chatSink));
            _userMessages = userMessages ?? throw new ArgumentNullException(nameof(userMessages));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _analysis = analysis ?? new ProdLogAnalysisService();

            Providers = new ObservableCollection<IProdLogProvider>();
            Tasks = new ObservableCollection<ProdTask>();
            Logs = new ObservableCollection<IProdLog>();
            RobotNameSuggestions = new ObservableCollection<string>();
            MinLevelOptions = new ObservableCollection<LogLevelOption>(LogLevelOption.CreateDefault());
            _selectedMinLevel = MinLevelOptions[0];
        }

        #region State

        /// <summary>Providers found on this machine (external ones, or the offline stub).</summary>
        public ObservableCollection<IProdLogProvider> Providers { get; }

        /// <summary>Selected provider; drives every task/log request.</summary>
        public IProdLogProvider SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (SetProperty(ref _selectedProvider, value))
                    OnPropertyChanged(nameof(CanLoadTasks));
            }
        }

        /// <summary>Tasks returned by the provider for the current robot name and filter.</summary>
        public ObservableCollection<ProdTask> Tasks { get; }

        /// <summary>Selected task; the host calls <see cref="RefreshLogsAsync"/> when it changes.</summary>
        public ProdTask SelectedTask
        {
            get => _selectedTask;
            set => SetProperty(ref _selectedTask, value);
        }

        /// <summary>Log entries of <see cref="SelectedTask"/>; the single source the analysis reads.</summary>
        public ObservableCollection<IProdLog> Logs { get; }

        /// <summary>Robot names other than the current one, offered as suggestions.</summary>
        public ObservableCollection<string> RobotNameSuggestions { get; }

        /// <summary>Entries of the "minimum level" filter ("All" first).</summary>
        public ObservableCollection<LogLevelOption> MinLevelOptions { get; }

        /// <summary>Message shown under the filter row (progress, result counts, errors).</summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>True while a provider call is in flight.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Robot name typed by the user (trimmed on use).</summary>
        public string RobotName
        {
            get => _robotName;
            set
            {
                if (SetProperty(ref _robotName, value ?? ""))
                    OnPropertyChanged(nameof(CanLoadTasks));
            }
        }

        /// <summary>Name to show in the robot box: the most recent remembered name, or the placeholder.</summary>
        public string CurrentRobotName => RobotNameHistory.ResolveCurrent(RobotNamesHistory);

        /// <summary>Minimum level of the provider filter.</summary>
        public LogLevelOption SelectedMinLevel
        {
            get => _selectedMinLevel;
            set => SetProperty(ref _selectedMinLevel, value);
        }

        /// <summary>Lower bound of the run date range (<c>null</c> = unbounded).</summary>
        public DateTime? FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        /// <summary>Upper bound of the run date range (<c>null</c> = unbounded).</summary>
        public DateTime? ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

        /// <summary>
        /// True once a log request finished. Distinguishes "nothing loaded yet" from "the task has
        /// no entries" in the analysis (both used to be <c>null</c> vs empty on the grid).
        /// </summary>
        public bool LogsLoaded
        {
            get => _logsLoaded;
            private set => SetProperty(ref _logsLoaded, value);
        }

        /// <summary>Last non-fatal failure; empty when none. The view decides how to show it.</summary>
        public string LastError
        {
            get => _lastError;
            private set => SetProperty(ref _lastError, value);
        }

        /// <summary>
        /// <c>true</c> when a task request can be sent: a robot name is typed and a provider is selected.
        /// Replaces the old view guard that read the panel visibility and the text box directly.
        /// </summary>
        public bool CanLoadTasks => !string.IsNullOrWhiteSpace(RobotName) && _selectedProvider != null;

        private List<string> RobotNamesHistory { get; set; } = new List<string>();

        #endregion State

        #region Lifecycle

        /// <summary>Loads the persisted robot-name history. Idempotent and UI-free.</summary>
        public override void Run()
        {
            LoadRobotNameHistory();
        }

        /// <summary>Reads the remembered robot names and updates the suggestions.</summary>
        public void LoadRobotNameHistory()
        {
            var stored = _settings.GetRobotNameHistory();
            RobotNamesHistory = RobotNameHistory.Normalize(stored);
            ApplyRobotNames();
            // Default of the robot box: the last name this user worked with (or the placeholder).
            // The old view did the same in SyncRobotNameBox; a default of the input value is not a
            // layout decision, so it belongs to the model.
            if (string.IsNullOrWhiteSpace(_robotName))
                RobotName = CurrentRobotName;

            _settingsLoaded = true;
        }

        /// <summary>
        /// Forgets provider discovery so the next access re-reads the configured folder.
        /// Panel data (tasks, logs) is kept: saving the settings must not wipe what the user has
        /// already loaded, which is how the view behaved before the model existed.
        /// </summary>
        public void InvalidateProviders()
        {
            _providersLoaded = false;
            Providers.Clear();
            SelectedProvider = null;
        }

        private void ApplyRobotNames()
        {
            RobotNameSuggestions.Clear();
            foreach (var name in RobotNameHistory.ResolveSuggestions(RobotNamesHistory))
                RobotNameSuggestions.Add(name);

            OnPropertyChanged(nameof(CurrentRobotName));
        }

        #endregion Lifecycle

        #region Providers

        /// <summary>
        /// Fills <see cref="Providers"/> once: external providers when the folder has any, otherwise
        /// the offline stub. Re-entrant calls are no-ops until <see cref="InvalidateProviders"/>.
        /// </summary>
        public void EnsureProviders()
        {
            if (_providersLoaded)
                return;

            _providersLoaded = true;

            IReadOnlyList<IProdLogProvider> discovered;
            try
            {
                discovered = _providerSource.Discover(_providerSource.ResolveDirectory());
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                discovered = null;
            }

            Providers.Clear();
            if (discovered != null && discovered.Count > 0)
            {
                foreach (var provider in discovered)
                    Providers.Add(provider);
            }
            else
            {
                var fallback = _providerSource.CreateFallback();
                if (fallback != null)
                    Providers.Add(fallback);
            }

            SelectedProvider = Providers.FirstOrDefault();
        }

        /// <summary>
        /// Opens the panel: discovers the providers and explains the outcome in <see cref="StatusText"/>.
        /// Called when the user switches the source to "production servers".
        /// </summary>
        public void Prepare()
        {
            LastError = "";
            EnsureProviders();

            if (SelectedProvider == null)
                StatusText = NoProviderMessage;
        }

        /// <summary>Selects the provider the user picked in the combo box.</summary>
        public void SelectProvider(IProdLogProvider provider)
        {
            if (provider != null)
                SelectedProvider = provider;
        }

        #endregion Providers

        #region Queries

        /// <summary>Query sent to the provider for both task and log requests.</summary>
        public ProdLogQuery BuildQuery()
        {
            return ProdLogQueryBuilder.Build(_selectedMinLevel?.Value, _fromDate, _toDate);
        }

        #endregion Queries

        #region Load scenarios

        /// <summary>
        /// Loads the tasks of <see cref="RobotName"/>. Remembering the name, clearing the previous
        /// logs, progress and result texts all belong to this scenario.
        /// </summary>
        public async Task LoadTasksAsync(CancellationToken cancellationToken = default)
        {
            if (IsBusy)
                return;

            LastError = "";
            var robotName = (RobotName ?? "").Trim();
            RobotName = robotName;

            if (string.IsNullOrWhiteSpace(robotName))
            {
                StatusText = EnterRobotNameMessage;
                return;
            }

            AddRobotNameToHistory(robotName);
            EnsureProviders();

            if (SelectedProvider == null)
            {
                StatusText = NoProviderAvailableMessage;
                return;
            }

            StatusText = "Loading tasks…";
            SelectedTask = null;
            Logs.Clear();
            LogsLoaded = false;

            IsBusy = true;
            try
            {
                var tasks = await SelectedProvider.GetTasksAsync(robotName, BuildQuery(), cancellationToken);

                Tasks.Clear();
                if (tasks != null)
                {
                    foreach (var task in tasks)
                        Tasks.Add(task);
                }

                StatusText = Tasks.Count + " task(s) loaded. Select a task to see its logs.";
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load tasks: " + ex.Message;
                LastError = ex.Message;
                await _userMessages.ErrorAsync(ex.ToString(), cancellationToken);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Loads the log entries of <see cref="SelectedTask"/>. Without a task (or a provider) the
        /// logs are cleared and nothing is reported, matching the old selection handler.
        /// </summary>
        public async Task RefreshLogsAsync(CancellationToken cancellationToken = default)
        {
            if (IsBusy)
                return;

            LastError = "";
            var task = _selectedTask;
            if (task == null || _selectedProvider == null)
            {
                Logs.Clear();
                LogsLoaded = false;
                return;
            }

            StatusText = "Loading logs…";

            IsBusy = true;
            try
            {
                var logs = await _selectedProvider.GetLogsAsync(task, BuildQuery(), cancellationToken);

                Logs.Clear();
                if (logs != null)
                {
                    foreach (var log in logs)
                        Logs.Add(log);
                }

                LogsLoaded = true;
                StatusText = Logs.Count + " log entry/entries loaded.";
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load logs: " + ex.Message;
                LastError = ex.Message;
                await _userMessages.ErrorAsync(ex.ToString(), cancellationToken);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion Load scenarios

        #region Analyze scenarios

        /// <summary>
        /// "Analyze logs → Output window": reads the Output window, builds the prompt and sends it to
        /// the chat. Returns <c>false</c> when there was nothing to send (empty window, failure).
        /// </summary>
        public async Task<bool> AnalyzeFromOutputWindowAsync(CancellationToken cancellationToken = default)
        {
            LastError = "";
            try
            {
                var text = await _outputWindow.GetTextAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(text))
                {
                    await _userMessages.NoticeAsync(EmptyOutputWindowMessage, cancellationToken);
                    return false;
                }

                var prompt = _analysis.BuildOutputWindowPrompt(text);
                await _chatSink.SendPromptAsync(prompt, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                await _userMessages.ErrorAsync(ex.ToString(), cancellationToken);
                return false;
            }
        }

        /// <summary>
        /// "Analyze logs → production servers": analyzes <see cref="Logs"/> (the model collection,
        /// never the grid), builds the prompt and sends it to the chat.
        /// Returns <c>false</c> when there was nothing to analyze.
        /// </summary>
        public async Task<bool> AnalyzeFromProdLogsAsync(CancellationToken cancellationToken = default)
        {
            LastError = "";

            if (!LogsLoaded)
            {
                StatusText = LoadLogsFirstMessage;
                return false;
            }

            var logs = Logs.ToList();
            if (logs.Count == 0)
            {
                StatusText = NoLogsToAnalyzeMessage;
                return false;
            }

            try
            {
                var analysis = _analysis.Analyze(logs);
                var prompt = _analysis.BuildPrompt(analysis, logs);
                await _chatSink.SendPromptAsync(prompt, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                await _userMessages.ErrorAsync(ex.ToString(), cancellationToken);
                return false;
            }
        }

        #endregion Analyze scenarios

        #region Commands

        // Public, non-overloaded wrappers: BaseModel.SendCommand resolves the XAML command name by
        // reflection (documentation/13-model-layer.md, rule 4).

        /// <summary>Command wrapper for <see cref="Prepare"/>.</summary>
        public void PrepareCommand() => Prepare();

        /// <summary>Command wrapper for <see cref="LoadTasksAsync"/>.</summary>
        public void LoadTasksCommand() => _ = LoadTasksAsync();

        /// <summary>Command wrapper for <see cref="RefreshLogsAsync"/>.</summary>
        public void RefreshLogsCommand() => _ = RefreshLogsAsync();

        /// <summary>Command wrapper for <see cref="AnalyzeFromOutputWindowAsync"/>.</summary>
        public void AnalyzeOutputWindowCommand() => _ = AnalyzeFromOutputWindowAsync();

        /// <summary>Command wrapper for <see cref="AnalyzeFromProdLogsAsync"/>.</summary>
        public void AnalyzeCommand() => _ = AnalyzeFromProdLogsAsync();

        #endregion Commands

        #region Robot name history

        /// <summary>
        /// Remembers <paramref name="name"/> (see <see cref="RobotNameHistory.Add"/>) and persists it
        /// when the list actually changed.
        /// </summary>
        /// <returns><c>true</c> when the history changed.</returns>
        public bool AddRobotNameToHistory(string name)
        {
            var changed = RobotNameHistory.Add(RobotNamesHistory, name);
            if (!changed)
                return false;

            ApplyRobotNames();
            try
            {
                _settings.SetRobotNameHistory(new List<string>(RobotNamesHistory));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            return true;
        }

        /// <summary>True once <see cref="LoadRobotNameHistory"/> ran (used by tests and the host).</summary>
        public bool IsSettingsLoaded => _settingsLoaded;

        #endregion Robot name history
    }
}
