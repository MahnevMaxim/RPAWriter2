using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// Knowledge base tab model: owns the robot-knowledge-base import scenario (override/continue,
    /// pause, stop, refresh) and the panel state. Import progress and messages stay on the tab
    /// (through <see cref="StatusText"/>, <see cref="Progress"/> and <see cref="Messages"/>), never
    /// in the chat.
    /// <para>
    /// The model is UI-free by contract (<c>documentation/13-model-layer.md</c>): it is constructed,
    /// exercised and asserted without a view model and without a view.
    /// </para>
    /// </summary>
    public sealed class KnowledgeBaseModel : BaseModel
    {
        private readonly Func<IAiProvider> _aiProvider;
        private readonly Func<SettingsData> _settingsProvider;
        private readonly Func<string, IKnowledgeBase> _knowledgeBaseFactory;
        private readonly IFileWriter _fileWriter;
        private readonly SynchronizationContext _syncContext;

        private CancellationTokenSource _importCts;
        private volatile bool _importPaused;
        private bool _isImporting;
        private string _statusText = "";
        private double _progress;
        private bool _autoScroll = true;
        private KnowledgeBaseFormationMode _importMode = KnowledgeBaseFormationMode.Continue;

        /// <summary>Creates the model with all external dependencies injected.</summary>
        public KnowledgeBaseModel(
            Func<IAiProvider> aiProvider,
            Func<SettingsData> settingsProvider,
            Func<string, IKnowledgeBase> knowledgeBaseFactory,
            IFileWriter fileWriter,
            SynchronizationContext syncContext = null)
        {
            _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _knowledgeBaseFactory = knowledgeBaseFactory ?? throw new ArgumentNullException(nameof(knowledgeBaseFactory));
            _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
            _syncContext = syncContext;
        }

        #region State

        /// <summary>Lines appended to the "Messages" box (import result, warnings, refresh output).</summary>
        public ObservableCollection<string> Messages { get; } = new ObservableCollection<string>();

        /// <summary>True while the robot import is running (drives button enablement).</summary>
        public bool IsImporting
        {
            get => _isImporting;
            private set
            {
                if (SetProperty(ref _isImporting, value))
                    OnPropertyChanged(nameof(CanImport));
            }
        }

        /// <summary>True while the import is paused (drives the Pause/Resume button label).</summary>
        public bool IsPaused
        {
            get => _importPaused;
            private set
            {
                if (_importPaused != value)
                {
                    _importPaused = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PauseButtonText));
                }
            }
        }

        /// <summary>Status line above the progress bar.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>Import progress in percent (0–100).</summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        /// <summary>True when new messages should auto-scroll the messages box.</summary>
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        /// <summary>Override vs. Continue mode of the next import.</summary>
        public KnowledgeBaseFormationMode ImportMode
        {
            get => _importMode;
            set => SetProperty(ref _importMode, value);
        }

        /// <summary>Combo index of <see cref="ImportMode"/>: 0 = Override, 1 = Continue.</summary>
        public int ImportModeIndex
        {
            get => _importMode == KnowledgeBaseFormationMode.Continue ? 1 : 0;
            set => ImportMode = value == 1 ? KnowledgeBaseFormationMode.Continue : KnowledgeBaseFormationMode.Override;
        }

        /// <summary>False while importing; drives the Import button and mode combo.</summary>
        public bool CanImport => !IsImporting;

        /// <summary>Label of the Pause button: "Resume" while paused, "Pause" otherwise.</summary>
        public string PauseButtonText => IsPaused ? "Resume" : "Pause";

        /// <summary>Single text projection of <see cref="Messages"/> for the read-only messages box.</summary>
        public string MessagesText => string.Join(Environment.NewLine, Messages);

        #endregion

        #region Commands

        /// <summary>Command target: import the knowledge base from the robots directory.</summary>
        public void ImportFromRobotsCommand() => _ = ImportFromRobotsAsync();

        /// <summary>Awaitable form of <see cref="ImportFromRobotsCommand"/> (used by tests).</summary>
        public async Task ImportFromRobotsAsync()
        {
            var kbPath = RequireKbPath();
            if (kbPath == null)
                return;

            var robotsRoot = (_settingsProvider()?.RobotsRootPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(robotsRoot))
            {
                StatusText = "Specify the robot solutions directory in the \"Settings\" tab first.";
                return;
            }

            if (!_fileWriter.DirectoryExists(robotsRoot))
            {
                StatusText = "Robot solutions directory does not exist: " + robotsRoot;
                return;
            }

            _importPaused = false;
            IsPaused = false;
            _importCts = new CancellationTokenSource();
            IsImporting = true;
            StatusText = ImportMode == KnowledgeBaseFormationMode.Continue ? "Resuming import…" : "Importing robots…";
            Progress = 0;

            var service = new RobotKnowledgeBaseImportService(_aiProvider());
            service.MessageReported += OnImportMessage;
            try
            {
                var progress = new UiProgress<string>(m => RunOnUiThread(() => StatusText = m));
                var progressValue = new UiProgress<double>(v => RunOnUiThread(() => Progress = v));
                var result = await service.ImportFromRobotsAsync(
                    robotsRoot, kbPath, progress, progressValue, _importCts.Token, () => _importPaused, ImportMode);

                var sb = new StringBuilder();
                sb.AppendLine("Robots processed: " + result.RobotsProcessed);
                sb.AppendLine("Spec files written: " + result.SpecFilesWritten);
                sb.AppendLine("Task files written: " + result.TaskFilesWritten);
                sb.AppendLine("Tasks added to index: " + result.TasksAdded);
                foreach (var warning in result.Warnings)
                    sb.AppendLine("Warning: " + warning);
                AppendMessage(sb.ToString().TrimEnd());
                var importSummary = "Knowledge base imported from robots: " + result.RobotsProcessed + " robot(s), " + result.TasksAdded + " task(s).";
                StatusText = importSummary;
            }
            catch (OperationCanceledException)
            {
                AppendMessage("Import stopped by user.");
                StatusText = "Import stopped.";
            }
            catch (Exception ex)
            {
                AppendMessage("Import failed.\n\n" + ex + "\n\nSelect \"Continue (resume)\" and run again to resume from the last completed robot.");
                StatusText = "Import failed. Select \"Continue (resume)\" and run again to resume from the last completed robot.";
                Report(ex.Message, NotifyLevel.Error);
            }
            finally
            {
                service.MessageReported -= OnImportMessage;
                IsImporting = false;
                IsPaused = false;
                _importPaused = false;
                _importCts.Dispose();
                _importCts = null;
            }
        }

        /// <summary>Command target: pause or resume the running import.</summary>
        public void PauseCommand()
        {
            if (_importCts == null)
                return;

            _importPaused = !_importPaused;
            IsPaused = _importPaused;
            StatusText = _importPaused ? "Paused — click Resume to continue." : "Importing robots…";
        }

        /// <summary>Command target: cancel the running import.</summary>
        public void StopCommand()
        {
            _importCts?.Cancel();
            StatusText = "Stopping…";
        }

        /// <summary>Command target: reload the knowledge base record list.</summary>
        public void RefreshCommand() => _ = RefreshAsync();

        /// <summary>Awaitable form of <see cref="RefreshCommand"/> (used by tests).</summary>
        public async Task RefreshAsync()
        {
            var kbPath = RequireKbPath();
            if (kbPath == null)
                return;

            try
            {
                var kb = _knowledgeBaseFactory(kbPath);
                var records = await kb.GetAllAsync();
                StatusText = "Solutions in the knowledge base: " + records.Count;
                foreach (var r in records)
                    AppendMessage((r.Name ?? "<unnamed>") + " — tasks: " + (r.Tasks?.Count ?? 0));
            }
            catch (Exception ex)
            {
                Report(ex.Message, NotifyLevel.Error);
            }
        }

        #endregion

        #region Helpers

        private void RunOnUiThread(Action action)
        {
            if (_syncContext == null)
            {
                action();
                return;
            }

            _syncContext.Post(_ => action(), null);
        }

        private void Report(string message, NotifyLevel level)
            => RunOnUiThread(() => NotifyMessage(new Notify { Message = message, NotifyLevel = level }));

        private void AppendMessage(string text)
        {
            RunOnUiThread(() =>
            {
                Messages.Add(text);
                OnPropertyChanged(nameof(MessagesText));
            });
        }

        private string RequireKbPath()
        {
            var path = (_settingsProvider()?.KnowledgeBasePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Knowledge base directory is not configured. Set the path in the \"Settings\" tab.";
                return null;
            }

            return path;
        }

        private void OnImportMessage(object sender, string message) => AppendMessage(message);

        /// <summary>
        /// Minimal <see cref="IProgress{T}"/> that reports synchronously through a delegate; the
        /// delegate is responsible for marshaling to the UI thread. Unlike <see cref="Progress{T}"/>
        /// it captures no <see cref="SynchronizationContext"/>, so it behaves identically headless
        /// and under a dispatcher.
        /// </summary>
        private sealed class UiProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;

            public UiProgress(Action<T> handler) => _handler = handler;

            public void Report(T value) => _handler(value);
        }

        #endregion
    }
}
