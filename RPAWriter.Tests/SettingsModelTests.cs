using System;
using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter.Models;
using RPAWriter.Mvvm;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SettingsModelTests
    {
        private sealed class InMemorySettingsStore : ISettingsStore
        {
            public SettingsData Stored { get; set; } = new SettingsData();
            public int SaveCount { get; private set; }

            public SettingsData Load() => Stored;

            public void Save(SettingsData settings)
            {
                Stored = settings;
                SaveCount++;
            }
        }

        private sealed class ReloadableAiProvider : StubAiProvider, IPromptReloadable
        {
            public int ReloadCount { get; private set; }

            public void ReloadPromptsFromDisk() => ReloadCount++;
        }

        private sealed class InMemoryFileWriter : IFileWriter
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly HashSet<string> Directories = new HashSet<string>();

            public bool FileExists(string path) => Files.ContainsKey(path);

            public string ReadAllText(string path) => Files.TryGetValue(path, out var value) ? value : null;

            public void WriteAllText(string path, string content) => Files[path] = content;

            public void CreateDirectory(string path) => Directories.Add(path);

            public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
                => Files[destinationPath] = Files.TryGetValue(sourcePath, out var value) ? value : "";

            public bool DirectoryExists(string path) => Directories.Contains(path);

            public IReadOnlyList<string> EnumerateFiles(string path)
            {
                var result = new List<string>();
                foreach (var key in Files.Keys)
                {
                    if (key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                        result.Add(key);
                }
                return result;
            }

            public void DeleteDirectory(string path, bool recursive) => Directories.Remove(path);
        }

        private sealed class FakeFileOpener : IFileOpener
        {
            public string OpenedPath;

            public void Open(string path) => OpenedPath = path;
        }

        private sealed class FakeFolderPicker : IFolderPicker
        {
            public string Result;
            public string Title;
            public string InitialDirectory;

            public string PickFolder(string title, string initialDirectory)
            {
                Title = title;
                InitialDirectory = initialDirectory;
                return Result;
            }
        }

        [Test]
        public void Model_CanBeConstructedWithoutViewModelAndView()
        {
            var model = new SettingsModel(new InMemorySettingsStore());

            Assert.That(model.ViewModel, Is.Null);
        }

        [Test]
        public void Run_LoadsValuesFromStore()
        {
            var store = new InMemorySettingsStore
            {
                Stored = new SettingsData
                {
                    BaseUrl = "https://example.com/v1",
                    Model = "deepseek-chat",
                    Temperature = 0.7,
                    KnowledgeBasePath = "C:\\kb",
                    ToolCallingMode = "Native"
                }
            };
            var model = new SettingsModel(store);

            model.Run();

            Assert.That(model.BaseUrl, Is.EqualTo("https://example.com/v1"));
            Assert.That(model.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(model.Temperature, Is.EqualTo(0.7));
            Assert.That(model.KnowledgeBasePath, Is.EqualTo("C:\\kb"));
            Assert.That(model.ToolCallingMode, Is.EqualTo("Native"));
        }

        [Test]
        public void DeployProjectName_RoundTripsThroughSaveAndLoad()
        {
            var store = new InMemorySettingsStore();
            var model = new SettingsModel(store);
            model.Run();

            model.DeployProjectName = "RPA-12345";
            model.SaveSettingsCommand();

            var reloaded = new SettingsModel(store);
            reloaded.Run();

            Assert.That(reloaded.DeployProjectName, Is.EqualTo("RPA-12345"));
        }

        [Test]
        public void NoRetryOn4xx_DefaultsToTrue()
        {
            var model = new SettingsModel(new InMemorySettingsStore());
            model.Run();

            Assert.That(model.NoRetryOn4xx, Is.True);
        }

        [Test]
        public void NoRetryOn4xx_RoundTripsThroughSaveAndLoad()
        {
            var store = new InMemorySettingsStore();
            var model = new SettingsModel(store);
            model.Run();

            model.NoRetryOn4xx = false;
            model.SaveSettingsCommand();

            var reloaded = new SettingsModel(store);
            reloaded.Run();

            Assert.That(reloaded.NoRetryOn4xx, Is.False);
        }

        [Test]
        public void SaveSettingsCommand_PersistsAndRaisesEvent()
        {
            var store = new InMemorySettingsStore();
            var model = new SettingsModel(store);
            model.Run();

            var raised = 0;
            model.SettingsSaved += (sender, e) => raised++;

            model.Model = "gpt-4o";
            model.SaveSettingsCommand();

            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.Stored.Model, Is.EqualTo("gpt-4o"));
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void SaveSettingsCommand_NormalizesInvalidValues()
        {
            var store = new InMemorySettingsStore();
            var model = new SettingsModel(store);
            model.Run();

            model.ContextWindowSize = 0;
            model.Temperature = 1.5;
            model.SaveSettingsCommand();

            Assert.That(store.Stored.ContextWindowSize, Is.EqualTo(409600));
            Assert.That(model.Temperature, Is.EqualTo(0.2));
        }

        [Test]
        public void ComboIndexes_RoundTripStringValues()
        {
            var model = new SettingsModel(new InMemorySettingsStore());

            model.ToolCallingModeIndex = 1;
            Assert.That(model.ToolCallingMode, Is.EqualTo("Native"));
            Assert.That(model.ToolCallingModeIndex, Is.EqualTo(1));

            model.MemoryBankModeIndex = 2;
            Assert.That(model.MemoryBankMode, Is.EqualTo("Convention"));

            model.SecurityAuditModeIndex = 1;
            Assert.That(model.SecurityAuditMode, Is.EqualTo("CredentialsOnly"));

            model.ReasoningEffortIndex = 7;
            Assert.That(model.ReasoningEffort, Is.EqualTo("max"));

            model.SpeechToTextProviderIndex = 2;
            Assert.That(model.SpeechToTextProvider, Is.EqualTo("Vosk"));
        }

        [Test]
        public void TemperatureText_FormatsSingleDecimal()
        {
            var model = new SettingsModel(new InMemorySettingsStore());

            model.Temperature = 0.5;

            Assert.That(model.TemperatureText, Is.EqualTo("0.5"));
        }

        [Test]
        public void RefreshPromptsFromDiskCommand_ReloadsWhenProviderSupportsIt()
        {
            var provider = new ReloadableAiProvider();
            var model = new SettingsModel(new InMemorySettingsStore(), aiProvider: () => provider);

            model.RefreshPromptsFromDiskCommand();

            Assert.That(provider.ReloadCount, Is.EqualTo(1));
        }

        [Test]
        public void RefreshPromptsFromDiskCommand_NotifiesWhenUnavailable()
        {
            var model = new SettingsModel(new InMemorySettingsStore(), aiProvider: () => new StubAiProvider());
            Notify notify = null;
            model.Notify += (sender, n) => notify = n;

            model.RefreshPromptsFromDiskCommand();

            Assert.That(notify, Is.Not.Null);
            Assert.That(notify.NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }

        [Test]
        public void OpenLogCommand_CreatesAndOpensResolvedPath()
        {
            var writer = new InMemoryFileWriter();
            var opener = new FakeFileOpener();
            var model = new SettingsModel(
                new InMemorySettingsStore(),
                fileWriter: writer,
                fileOpener: opener,
                logFilePathResolver: () => "C:\\logs\\ext.log");

            model.OpenLogCommand();

            Assert.That(opener.OpenedPath, Is.EqualTo("C:\\logs\\ext.log"));
            Assert.That(writer.Files.ContainsKey("C:\\logs\\ext.log"), Is.True);
        }

        [Test]
        public void OpenLogCommand_NotifiesWhenPathUnavailable()
        {
            var model = new SettingsModel(new InMemorySettingsStore());
            Notify notify = null;
            model.Notify += (sender, n) => notify = n;

            model.OpenLogCommand();

            Assert.That(notify, Is.Not.Null);
            Assert.That(notify.NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }

        [Test]
        public void OpenChatLogCommand_UsesChatLogResolver()
        {
            var opener = new FakeFileOpener();
            var model = new SettingsModel(
                new InMemorySettingsStore(),
                fileOpener: opener,
                chatLogFilePathResolver: () => "C:\\logs\\chat.log");

            model.OpenChatLogCommand();

            Assert.That(opener.OpenedPath, Is.EqualTo("C:\\logs\\chat.log"));
        }

        [Test]
        public void ExportCommands_NotifyWhenSourceDirectoryMissing()
        {
            var model = new SettingsModel(new InMemorySettingsStore(), fileWriter: new InMemoryFileWriter());
            Notify notify = null;
            model.Notify += (sender, n) => notify = n;

            model.ExportDocumentationCommand();

            Assert.That(notify, Is.Not.Null);
            Assert.That(notify.NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
            Assert.That(notify.Message, Does.Contain("directory not found"));
        }

        [Test]
        public void AddReadPathCommand_AddsPickedFolder()
        {
            var picker = new FakeFolderPicker { Result = @"C:\Extra" };
            var model = new SettingsModel(new InMemorySettingsStore(), folderPicker: picker);

            model.AddReadPathCommand();

            Assert.That(model.AdditionalReadPaths, Is.EquivalentTo(new[] { @"C:\Extra" }));
            Assert.That(model.SelectedReadPath, Is.EqualTo(@"C:\Extra"));
        }

        [Test]
        public void AddReadPathCommand_IgnoresEmptyAndDuplicates()
        {
            var picker = new FakeFolderPicker { Result = @"C:\Extra" };
            var model = new SettingsModel(new InMemorySettingsStore(), folderPicker: picker);

            model.AddReadPathCommand();
            picker.Result = "   ";
            model.AddReadPathCommand();
            picker.Result = @"c:\extra";
            model.AddReadPathCommand();

            Assert.That(model.AdditionalReadPaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddReadPathCommand_NoPicker_DoesNothing()
        {
            var model = new SettingsModel(new InMemorySettingsStore());

            Assert.DoesNotThrow(() => model.AddReadPathCommand());
            Assert.That(model.AdditionalReadPaths, Is.Empty);
        }

        [Test]
        public void RemoveReadPathCommand_RemovesSelected()
        {
            var picker = new FakeFolderPicker { Result = @"C:\Extra" };
            var model = new SettingsModel(new InMemorySettingsStore(), folderPicker: picker);
            model.AddReadPathCommand();

            model.RemoveReadPathCommand();

            Assert.That(model.AdditionalReadPaths, Is.Empty);
            Assert.That(model.SelectedReadPath, Is.Null);
        }

        [Test]
        public void Save_RoundTripsAdditionalReadPaths()
        {
            var store = new InMemorySettingsStore();
            var picker = new FakeFolderPicker { Result = @"C:\Extra" };
            var model = new SettingsModel(store, folderPicker: picker);
            model.AddReadPathCommand();

            model.SaveSettingsCommand();

            var reloaded = new SettingsModel(store);
            reloaded.Run();

            Assert.That(reloaded.AdditionalReadPaths, Is.EquivalentTo(new[] { @"C:\Extra" }));
        }

        [Test]
        public void Save_RoundTripsReadPolicyFlags()
        {
            var store = new InMemorySettingsStore();
            var model = new SettingsModel(store);
            model.DenyServiceDirectories = false;
            model.DenySensitiveFiles = false;

            model.SaveSettingsCommand();

            var reloaded = new SettingsModel(store);
            reloaded.Run();

            Assert.That(reloaded.DenyServiceDirectories, Is.False);
            Assert.That(reloaded.DenySensitiveFiles, Is.False);
        }
    }
}
