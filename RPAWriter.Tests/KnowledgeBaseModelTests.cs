using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Models;
using RPAWriter.Mvvm;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class KnowledgeBaseModelTests
    {
        #region Test doubles

        private sealed class InMemoryFileWriter : IFileWriter
        {
            public readonly HashSet<string> Directories = new HashSet<string>();
            public bool FileExists(string path) => false;
            public string ReadAllText(string path) => null;
            public void WriteAllText(string path, string content) { }
            public void CreateDirectory(string path) => Directories.Add(path);
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { }
            public bool DirectoryExists(string path) => Directories.Contains(path);
            public IReadOnlyList<string> EnumerateFiles(string path) => new List<string>();
            public void DeleteDirectory(string path, bool recursive) => Directories.Remove(path);
        }

        private sealed class InMemoryKnowledgeBase : IKnowledgeBase
        {
            public readonly List<SolutionRecord> Records = new List<SolutionRecord>();
            public Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SolutionRecord>>(Records);
            public Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SimilarTaskMatch>>(new List<SimilarTaskMatch>());
            public Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SolutionRecord>(null);
        }

        #endregion

        private static KnowledgeBaseModel CreateModel(
            SettingsData settings = null,
            IFileWriter fileWriter = null,
            InMemoryKnowledgeBase kb = null)
        {
            return new KnowledgeBaseModel(
                () => new StubAiProvider(),
                () => settings ?? new SettingsData { KnowledgeBasePath = "C:\\kb", RobotsRootPath = "C:\\robots" },
                path => kb ?? new InMemoryKnowledgeBase(),
                fileWriter ?? new InMemoryFileWriter());
        }

        [Test]
        public void Model_CanBeConstructedWithoutViewModelAndView()
        {
            var model = CreateModel();

            Assert.That(model.ViewModel, Is.Null);
            Assert.That(model.IsImporting, Is.False);
            Assert.That(model.IsPaused, Is.False);
            Assert.That(model.CanImport, Is.True);
            Assert.That(model.ImportMode, Is.EqualTo(KnowledgeBaseFormationMode.Continue));
        }

        [Test]
        public async Task ImportFromRobotsAsync_WhenNoKbPath_SetsStatus()
        {
            var model = CreateModel(settings: new SettingsData { KnowledgeBasePath = "", RobotsRootPath = "C:\\robots" });

            await model.ImportFromRobotsAsync();

            Assert.That(model.StatusText, Does.Contain("Knowledge base directory is not configured"));
        }

        [Test]
        public async Task ImportFromRobotsAsync_WhenNoRobotsRoot_SetsStatus()
        {
            var model = CreateModel(settings: new SettingsData { KnowledgeBasePath = "C:\\kb", RobotsRootPath = "" });

            await model.ImportFromRobotsAsync();

            Assert.That(model.StatusText, Does.Contain("Specify the robot solutions directory"));
        }

        [Test]
        public async Task ImportFromRobotsAsync_WhenRobotsRootMissing_SetsStatus()
        {
            var fileWriter = new InMemoryFileWriter();
            var model = CreateModel(fileWriter: fileWriter);

            await model.ImportFromRobotsAsync();

            Assert.That(model.StatusText, Does.Contain("does not exist"));
        }

        [Test]
        public async Task RefreshAsync_WhenNoKbPath_SetsStatus()
        {
            var model = CreateModel(settings: new SettingsData { KnowledgeBasePath = "", RobotsRootPath = "C:\\robots" });

            await model.RefreshAsync();

            Assert.That(model.StatusText, Does.Contain("Knowledge base directory is not configured"));
        }

        [Test]
        public async Task RefreshAsync_LoadsRecords()
        {
            var kb = new InMemoryKnowledgeBase();
            kb.Records.Add(new SolutionRecord { Name = "RPA-1", Tasks = new List<TaskDescriptor> { new TaskDescriptor { Action = "open" } } });
            var model = CreateModel(kb: kb);

            await model.RefreshAsync();

            Assert.That(model.StatusText, Is.EqualTo("Solutions in the knowledge base: 1"));
            Assert.That(model.Messages, Has.Count.EqualTo(1));
            Assert.That(model.Messages[0], Does.Contain("RPA-1"));
        }

        [Test]
        public void ImportModeIndex_RoundTripsMode()
        {
            var model = CreateModel();

            model.ImportModeIndex = 0;
            Assert.That(model.ImportMode, Is.EqualTo(KnowledgeBaseFormationMode.Override));

            model.ImportModeIndex = 1;
            Assert.That(model.ImportMode, Is.EqualTo(KnowledgeBaseFormationMode.Continue));
            Assert.That(model.ImportModeIndex, Is.EqualTo(1));
        }

        [Test]
        public void PauseCommand_WhenNotImporting_DoesNothing()
        {
            var model = CreateModel();

            model.PauseCommand();

            Assert.That(model.IsPaused, Is.False);
            Assert.That(model.PauseButtonText, Is.EqualTo("Pause"));
        }
    }
}
