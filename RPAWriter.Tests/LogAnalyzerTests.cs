using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Models;
using RPAWriter.Mvvm;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Tests of the "Analyze logs" model: provider discovery, task/log loading, robot-name history
    /// and the two analysis scenarios. The model is exercised headless — no view model, no view,
    /// no Visual Studio, no file system (see documentation/13-model-layer.md, section 18).
    /// </summary>
    [TestFixture]
    public class LogAnalyzerTests
    {
        private FakeLogProvider _provider;
        private FakeLogProviderSource _source;
        private FakeOutputWindowTextSource _outputWindow;
        private RecordingChatSink _sink;
        private InMemoryLogAnalysisSettings _settings;
        private ProdLogAnalysisService _analysis;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeLogProvider();
            _source = new FakeLogProviderSource { Fallback = _provider };
            _source.Discovered.Add(_provider);
            _outputWindow = new FakeOutputWindowTextSource();
            _sink = new RecordingChatSink();
            _settings = new InMemoryLogAnalysisSettings();
            _analysis = new ProdLogAnalysisService(null, "PROD-HEADER", "OUTPUT-HEADER");
        }

        private LogAnalyzer CreateAnalyzer()
        {
            return new LogAnalyzer(_source, _outputWindow, _sink, _sink, _settings, _analysis);
        }

        #region Construction and lifecycle

        [Test]
        public void Model_CanBeConstructedWithoutViewModelAndView()
        {
            var analyzer = CreateAnalyzer();

            Assert.That(analyzer, Is.Not.Null);
            Assert.That(analyzer.ViewModel, Is.Null, "the model must start without a view model");
            Assert.DoesNotThrow(() => analyzer.Run());
        }

        [Test]
        public void Model_ExposesTheInitialState()
        {
            var analyzer = CreateAnalyzer();

            Assert.That(analyzer.Providers, Is.Empty);
            Assert.That(analyzer.Tasks, Is.Empty);
            Assert.That(analyzer.Logs, Is.Empty);
            Assert.That(analyzer.RobotNameSuggestions, Is.Empty);
            Assert.That(analyzer.StatusText, Is.Empty);
            Assert.That(analyzer.IsBusy, Is.False);
            Assert.That(analyzer.LogsLoaded, Is.False);
            Assert.That(analyzer.SelectedProvider, Is.Null);
            Assert.That(analyzer.SelectedTask, Is.Null);
        }

        [Test]
        public void Model_StartsWithAllLevelsSelected()
        {
            var analyzer = CreateAnalyzer();

            Assert.That(analyzer.MinLevelOptions, Has.Count.EqualTo(Enum.GetValues(typeof(LogLevel)).Length + 1));
            Assert.That(analyzer.MinLevelOptions[0].Title, Is.EqualTo("All"));
            Assert.That(analyzer.MinLevelOptions[0].Value, Is.Null);
            Assert.That(analyzer.SelectedMinLevel, Is.SameAs(analyzer.MinLevelOptions[0]));
            Assert.That(analyzer.MinLevelOptions.Select(o => o.Title),
                Is.EqualTo(new[] { "All", "Trace", "Debug", "Info", "Warning", "Error", "Fatal" }));
        }

        [Test]
        public void Run_LoadsRobotNameHistoryFromSettings()
        {
            _settings.RobotNameHistory = new List<string> { "RPA-1212", "RPA-7" };
            var analyzer = CreateAnalyzer();

            analyzer.Run();

            Assert.That(analyzer.CurrentRobotName, Is.EqualTo("RPA-1212"));
            Assert.That(analyzer.RobotNameSuggestions, Is.EqualTo(new[] { "RPA-7" }));
            Assert.That(analyzer.IsSettingsLoaded, Is.True);
        }

        [Test]
        public void Run_NormalizesStoredHistory()
        {
            _settings.RobotNameHistory = new List<string> { " RPA-1 ", "", "rpa-1", "RPA-2" };
            var analyzer = CreateAnalyzer();

            analyzer.Run();

            Assert.That(analyzer.CurrentRobotName, Is.EqualTo("RPA-1"));
            Assert.That(analyzer.RobotNameSuggestions, Is.EqualTo(new[] { "RPA-2" }));
        }

        [Test]
        public void Run_WithEmptyHistoryUsesThePlaceholder()
        {
            var analyzer = CreateAnalyzer();

            analyzer.Run();

            Assert.That(analyzer.CurrentRobotName, Is.EqualTo(RobotNameHistory.DefaultRobotName));
        }

        [Test]
        public void Run_PrefillsTheRobotNameWithTheRememberedName()
        {
            _settings.RobotNameHistory = new List<string> { "RPA-1212", "RPA-7" };
            var analyzer = CreateAnalyzer();

            analyzer.Run();

            Assert.That(analyzer.RobotName, Is.EqualTo("RPA-1212"),
                "the robot box opens with the last name the user worked with");
        }

        [Test]
        public void Run_WithEmptyHistoryPrefillsThePlaceholder()
        {
            var analyzer = CreateAnalyzer();

            analyzer.Run();

            Assert.That(analyzer.RobotName, Is.EqualTo(RobotNameHistory.DefaultRobotName));
        }

        [Test]
        public void Run_DoesNotOverwriteARobotNameGivenBeforeStartup()
        {
            _settings.RobotNameHistory = new List<string> { "RPA-1212" };
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-42";

            analyzer.Run();

            Assert.That(analyzer.RobotName, Is.EqualTo("RPA-42"));
        }

        [Test]
        public void CanLoadTasks_RaisesPropertyChangedWhenItsInputsChange()
        {
            var analyzer = CreateAnalyzer();
            var changed = new List<string>();
            ((INotifyPropertyChanged)analyzer).PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            analyzer.RobotName = "RPA-1212";
            analyzer.SelectedProvider = _provider;

            Assert.That(changed, Does.Contain(nameof(LogAnalyzer.CanLoadTasks)),
                "the view guard reads CanLoadTasks, so it must be notified");
            Assert.That(analyzer.CanLoadTasks, Is.True);
        }

        #endregion Construction and lifecycle

        #region Providers

        [Test]
        public void Prepare_SelectsTheFirstDiscoveredProvider()
        {
            var second = new FakeLogProvider { Name = "second" };
            _source.Discovered.Add(second);
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();

            Assert.That(analyzer.Providers, Has.Count.EqualTo(2));
            Assert.That(analyzer.SelectedProvider, Is.SameAs(_provider));
            Assert.That(analyzer.StatusText, Is.Empty);
        }

        [Test]
        public void Prepare_WithoutDiscoveredProvidersFallsBackToTheStub()
        {
            _source.Discovered.Clear();
            var fallback = new FakeLogProvider { Name = "fallback" };
            _source.Fallback = fallback;
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();

            Assert.That(analyzer.Providers.Count, Is.EqualTo(1));
            Assert.That(analyzer.SelectedProvider, Is.SameAs(fallback));
            Assert.That(analyzer.StatusText, Is.Empty);
        }

        [Test]
        public void Prepare_WithoutAnyProviderReportsTheMessage()
        {
            _source.Discovered.Clear();
            _source.Fallback = null;
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();

            Assert.That(analyzer.Providers, Is.Empty);
            Assert.That(analyzer.SelectedProvider, Is.Null);
            Assert.That(analyzer.StatusText, Is.EqualTo(LogAnalyzer.NoProviderMessage));
        }

        [Test]
        public void Prepare_UsesTheDirectoryProvidedByTheSource()
        {
            _source.Directory = @"D:\LogProviders";
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();

            Assert.That(_source.LastDirectory, Is.EqualTo(@"D:\LogProviders"));
        }

        [Test]
        public void Prepare_IsIdempotentUntilInvalidated()
        {
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();
            analyzer.Prepare();
            Assert.That(_source.DiscoverCalls, Is.EqualTo(1));

            analyzer.InvalidateProviders();
            analyzer.Prepare();
            Assert.That(_source.DiscoverCalls, Is.EqualTo(2));
        }

        [Test]
        public void InvalidateProviders_DropsProvidersButKeepsLoadedData()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "log"));
            var analyzer = CreateAnalyzer();
            analyzer.Prepare();
            analyzer.RobotName = "RPA-1";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            analyzer.InvalidateProviders();

            Assert.That(analyzer.Providers, Is.Empty);
            Assert.That(analyzer.SelectedProvider, Is.Null);
            Assert.That(analyzer.Tasks, Has.Count.EqualTo(1), "saving the settings must not wipe the loaded tasks");
            Assert.That(analyzer.Logs, Has.Count.EqualTo(1), "saving the settings must not wipe the loaded logs");
            Assert.That(analyzer.LogsLoaded, Is.True);
        }

        [Test]
        public void Prepare_ReportsDiscoveryFailureInLastError()
        {
            _source.DiscoverError = new InvalidOperationException("broken folder");
            _source.Discovered.Clear();
            var analyzer = CreateAnalyzer();

            analyzer.Prepare();

            Assert.That(analyzer.LastError, Is.EqualTo("broken folder"));
            Assert.That(analyzer.Providers.Count, Is.EqualTo(1), "the fallback must still be used");
        }

        [Test]
        public void SelectProvider_IgnoresNull()
        {
            var analyzer = CreateAnalyzer();
            analyzer.Prepare();

            analyzer.SelectProvider(null);

            Assert.That(analyzer.SelectedProvider, Is.SameAs(_provider));
        }

        [Test]
        public void SelectProvider_UsesTheChosenProvider()
        {
            var second = new FakeLogProvider { Name = "second" };
            _source.Discovered.Add(second);
            var analyzer = CreateAnalyzer();
            analyzer.Prepare();

            analyzer.SelectProvider(second);

            Assert.That(analyzer.SelectedProvider, Is.SameAs(second));
        }

        #endregion Providers

        #region Load tasks

        [Test]
        public void LoadTasks_WithoutRobotNameAsksForIt()
        {
            var analyzer = CreateAnalyzer();

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.StatusText, Is.EqualTo(LogAnalyzer.EnterRobotNameMessage));
            Assert.That(_provider.TasksCalls, Is.Zero);
        }

        [Test]
        public void LoadTasks_WithoutProviderReportsIt()
        {
            _source.Discovered.Clear();
            _source.Fallback = null;
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.StatusText, Is.EqualTo(LogAnalyzer.NoProviderAvailableMessage));
        }

        [Test]
        public void LoadTasks_FillsTasksAndReportsTheCount()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1", Name = "First" });
            _provider.Tasks.Add(new ProdTask { TaskId = "T-2", Name = "Second" });
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.Tasks.Select(t => t.TaskId), Is.EqualTo(new[] { "T-1", "T-2" }));
            Assert.That(analyzer.StatusText, Is.EqualTo("2 task(s) loaded. Select a task to see its logs."));
            Assert.That(_provider.TasksCalls, Is.EqualTo(1));
            Assert.That(_provider.LastRobotName, Is.EqualTo("RPA-1212"));
            Assert.That(analyzer.IsBusy, Is.False);
        }

        [Test]
        public void LoadTasks_TrimsTheRobotName()
        {
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "  RPA-1212  ";

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.RobotName, Is.EqualTo("RPA-1212"));
            Assert.That(_provider.LastRobotName, Is.EqualTo("RPA-1212"));
            Assert.That(analyzer.CurrentRobotName, Is.EqualTo("RPA-1212"));
        }

        [Test]
        public void LoadTasks_SendsTheFilterQuery()
        {
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.SelectedMinLevel = analyzer.MinLevelOptions.First(o => o.Value == LogLevel.Warning);
            analyzer.FromDate = new DateTime(2024, 1, 1);
            analyzer.ToDate = new DateTime(2024, 1, 31);

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(_provider.LastTasksQuery.MinLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(_provider.LastTasksQuery.FromUtc!.Value, Is.EqualTo(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(_provider.LastTasksQuery.ToUtc!.Value.Date, Is.EqualTo(new DateTime(2024, 1, 31)));
        }

        [Test]
        public void LoadTasks_RemembersTheRobotNameOnce()
        {
            var analyzer = CreateAnalyzer();
            analyzer.Run();
            analyzer.RobotName = "RPA-1212";

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(_settings.RobotNameHistory, Is.EqualTo(new[] { "RPA-1212" }));
            Assert.That(_settings.SaveCount, Is.EqualTo(1), "an unchanged history must not be saved twice");
        }

        [Test]
        public void LoadTasks_ClearsPreviousTaskLogsAndSelection()
        {
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "previous"));
            _provider.Tasks.Add(new ProdTask { TaskId = "T-old" });
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();
            Assert.That(analyzer.Logs, Has.Count.EqualTo(1));

            _provider.Tasks.Clear();
            _provider.Tasks.Add(new ProdTask { TaskId = "T-new" });
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.Logs, Is.Empty);
            Assert.That(analyzer.SelectedTask, Is.Null);
            Assert.That(analyzer.Tasks.Single().TaskId, Is.EqualTo("T-new"));
        }

        [Test]
        public void LoadTasks_ProviderFailureSetsStatusAndErrorCard()
        {
            _provider.TasksError = new InvalidOperationException("server down");
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";

            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.StatusText, Is.EqualTo("Failed to load tasks: server down"));
            Assert.That(analyzer.LastError, Is.EqualTo("server down"));
            Assert.That(_sink.Errors.Single(), Does.Contain("server down"));
            Assert.That(analyzer.IsBusy, Is.False, "IsBusy must be cleared when the provider throws");
        }

        #endregion Load tasks

        #region Load logs

        [Test]
        public void RefreshLogs_WithoutSelectedTaskClearsLogsSilently()
        {
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "ignored"));
            var analyzer = CreateAnalyzer();
            analyzer.Prepare();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();

            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.Logs, Is.Empty);
            Assert.That(_provider.LogsCalls, Is.Zero);
            Assert.That(analyzer.StatusText, Does.Contain("task(s) loaded"));
        }

        [Test]
        public void RefreshLogs_FillsLogsAndReportsTheCount()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "first"));
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Error, "second"));
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];

            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.Logs, Has.Count.EqualTo(2));
            Assert.That(analyzer.LogsLoaded, Is.True);
            Assert.That(analyzer.StatusText, Is.EqualTo("2 log entry/entries loaded."));
            Assert.That(_provider.LastTask.TaskId, Is.EqualTo("T-1"));
        }

        [Test]
        public void RefreshLogs_SendsTheLevelFilter()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.SelectedMinLevel = analyzer.MinLevelOptions.First(o => o.Value == LogLevel.Error);

            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            Assert.That(_provider.LastLogsQuery.MinLevel, Is.EqualTo(LogLevel.Error));
        }

        [Test]
        public void RefreshLogs_ProviderFailureSetsStatusAndErrorCard()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            _provider.LogsError = new InvalidOperationException("timeout");
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];

            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            Assert.That(analyzer.StatusText, Is.EqualTo("Failed to load logs: timeout"));
            Assert.That(_sink.Errors, Has.Count.EqualTo(1));
            Assert.That(analyzer.IsBusy, Is.False);
        }

        #endregion Load logs

        #region Analyze

        [Test]
        public void Analyze_WithoutLoadedLogsAsksToLoadThem()
        {
            var analyzer = CreateAnalyzer();

            var sent = analyzer.AnalyzeFromProdLogsAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.False);
            Assert.That(analyzer.StatusText, Is.EqualTo(LogAnalyzer.LoadLogsFirstMessage));
            Assert.That(_sink.Prompts, Is.Empty);
        }

        [Test]
        public void Analyze_WithEmptyLogsReportsNoEntries()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            var sent = analyzer.AnalyzeFromProdLogsAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.False);
            Assert.That(analyzer.StatusText, Is.EqualTo(LogAnalyzer.NoLogsToAnalyzeMessage));
            Assert.That(_sink.Prompts, Is.Empty);
        }

        [Test]
        public void Analyze_SendsOnePromptBuiltFromTheModelLogs()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Error, "connection reset", "SAP",
                new DateTime(2024, 5, 6, 7, 8, 9)));
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "queue item received"));
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            var sent = analyzer.AnalyzeFromProdLogsAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.True);
            Assert.That(_sink.Prompts, Has.Count.EqualTo(1));
            Assert.That(_sink.Prompts[0], Does.StartWith("PROD-HEADER"));
            Assert.That(_sink.Prompts[0], Does.Contain("Total entries: 2"));
            Assert.That(_sink.Prompts[0], Does.Contain("connection reset"));
            Assert.That(_sink.Prompts[0], Does.Contain("Full log:"));
        }

        [Test]
        public void Analyze_SendFailureIsReportedAndReturnsFalse()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            _provider.Logs.Add(FakeProdLog.Create(LogLevel.Info, "line"));
            _sink.SendError = new InvalidOperationException("chat unavailable");
            var analyzer = CreateAnalyzer();
            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksAsync().GetAwaiter().GetResult();
            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsAsync().GetAwaiter().GetResult();

            var sent = analyzer.AnalyzeFromProdLogsAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.False);
            Assert.That(analyzer.LastError, Is.EqualTo("chat unavailable"));
            Assert.That(_sink.Errors.Single(), Does.Contain("chat unavailable"));
        }

        [Test]
        public void AnalyzeFromOutputWindow_SendsTheWindowText()
        {
            _outputWindow.Text = "Build started...\n1 error";
            var analyzer = CreateAnalyzer();

            var sent = analyzer.AnalyzeFromOutputWindowAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.True);
            Assert.That(_outputWindow.Calls, Is.EqualTo(1));
            Assert.That(_sink.Prompts.Single(), Is.EqualTo("OUTPUT-HEADER\n\nBuild started...\n1 error"));
        }

        [Test]
        public void AnalyzeFromOutputWindow_EmptyWindowPostsANotice()
        {
            _outputWindow.Text = "   ";
            var analyzer = CreateAnalyzer();

            var sent = analyzer.AnalyzeFromOutputWindowAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.False);
            Assert.That(_sink.Prompts, Is.Empty);
            Assert.That(_sink.Notices.Single(), Is.EqualTo(LogAnalyzer.EmptyOutputWindowMessage));
        }

        [Test]
        public void AnalyzeFromOutputWindow_FailureIsReported()
        {
            _outputWindow.Error = new InvalidOperationException("no output pane");
            var analyzer = CreateAnalyzer();

            var sent = analyzer.AnalyzeFromOutputWindowAsync().GetAwaiter().GetResult();

            Assert.That(sent, Is.False);
            Assert.That(analyzer.LastError, Is.EqualTo("no output pane"));
            Assert.That(_sink.Errors, Has.Count.EqualTo(1));
        }

        #endregion Analyze

        #region Robot name history

        [Test]
        public void AddRobotNameToHistory_PersistsAndUpdatesSuggestions()
        {
            _settings.RobotNameHistory = new List<string> { "RPA-1212" };
            var analyzer = CreateAnalyzer();
            analyzer.Run();

            Assert.That(analyzer.AddRobotNameToHistory("RPA-7"), Is.True);

            Assert.That(_settings.RobotNameHistory, Is.EqualTo(new[] { "RPA-7", "RPA-1212" }));
            Assert.That(analyzer.RobotNameSuggestions, Is.EqualTo(new[] { "RPA-1212" }));
            Assert.That(_settings.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void AddRobotNameToHistory_IgnoresPlaceholderAndBlank()
        {
            var analyzer = CreateAnalyzer();
            analyzer.Run();

            Assert.That(analyzer.AddRobotNameToHistory("RPA-"), Is.False);
            Assert.That(analyzer.AddRobotNameToHistory("  "), Is.False);
            Assert.That(_settings.SaveCount, Is.Zero);
        }

        [Test]
        public void AddRobotNameToHistory_ReportsSaveFailureButKeepsTheName()
        {
            _settings.SaveError = new InvalidOperationException("settings read-only");
            var analyzer = CreateAnalyzer();
            analyzer.Run();

            var added = analyzer.AddRobotNameToHistory("RPA-1212");

            Assert.That(added, Is.True);
            Assert.That(analyzer.CurrentRobotName, Is.EqualTo("RPA-1212"));
            Assert.That(analyzer.LastError, Is.EqualTo("settings read-only"));
        }

        #endregion Robot name history

        #region Commands and wiring

        [Test]
        public void Commands_ArePublicParameterlessAndNotOverloaded()
        {
            var names = new[]
            {
                nameof(LogAnalyzer.PrepareCommand),
                nameof(LogAnalyzer.LoadTasksCommand),
                nameof(LogAnalyzer.RefreshLogsCommand),
                nameof(LogAnalyzer.AnalyzeOutputWindowCommand),
                nameof(LogAnalyzer.AnalyzeCommand)
            };

            foreach (var name in names)
            {
                var methods = typeof(LogAnalyzer).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == name)
                    .ToArray();

                Assert.That(methods, Has.Length.EqualTo(1), name + " must not be overloaded");
                Assert.That(methods[0].GetParameters(), Is.Empty, name + " must be parameterless");
                Assert.That(methods[0].ReturnType, Is.EqualTo(typeof(void)), name + " must return void for SendCommand");
            }
        }

        [Test]
        public void Commands_ExecuteTheScenarios()
        {
            _provider.Tasks.Add(new ProdTask { TaskId = "T-1" });
            var analyzer = CreateAnalyzer();

            analyzer.PrepareCommand();
            Assert.That(_source.DiscoverCalls, Is.EqualTo(1));

            analyzer.RobotName = "RPA-1212";
            analyzer.LoadTasksCommand();
            WaitFor(() => analyzer.Tasks.Count == 1);
            Assert.That(analyzer.Tasks, Has.Count.EqualTo(1));

            analyzer.SelectedTask = analyzer.Tasks[0];
            analyzer.RefreshLogsCommand();
            WaitFor(() => analyzer.LogsLoaded);
            Assert.That(analyzer.LogsLoaded, Is.True);
        }

        [Test]
        public void SendCommand_ResolvesTheCommandByName()
        {
            var analyzer = CreateAnalyzer();

            analyzer.SendCommand(nameof(LogAnalyzer.PrepareCommand));

            Assert.That(_source.DiscoverCalls, Is.EqualTo(1));
        }

        [Test]
        public void ChatModel_CarriesTheLogAnalyzerChildAndRunsIt()
        {
            _settings.RobotNameHistory = new List<string> { "RPA-999" };
            var analyzer = CreateAnalyzer();
            var chat = new ChatModel(new InMemoryChatHistoryStore(), null, analyzer);

            chat.Run();

            Assert.That(chat.LogAnalyzer, Is.SameAs(analyzer));
            Assert.That(analyzer.IsSettingsLoaded, Is.True, "ChatModel.Run must start the child model");
            Assert.That(analyzer.CurrentRobotName, Is.EqualTo("RPA-999"));
        }

        [Test]
        public void ChatModel_WorksWithoutTheLogAnalyzer()
        {
            var chat = new ChatModel(new InMemoryChatHistoryStore());

            Assert.That(chat.LogAnalyzer, Is.Null);
            Assert.DoesNotThrow(() => chat.Run());
        }

        [Test]
        public void Model_HasNoUiDependencies()
        {
            var assembly = typeof(LogAnalyzer).Assembly;
            var references = assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();

            Assert.That(references, Does.Not.Contain("PresentationFramework"));
            Assert.That(references, Does.Not.Contain("RPAWriter2"));
        }

        private static void WaitFor(Func<bool> condition, int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;
                Task.Delay(5).GetAwaiter().GetResult();
            }
        }

        #endregion Commands and wiring
    }
}
