using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SpecTabModelTests
    {
        #region Test doubles

        private sealed class FakeSolutionPathProvider : ISolutionPathProvider
        {
            public string Directory;
            public string GetSolutionDirectory() => Directory;
        }

        private sealed class FakeFilePicker : IFilePicker
        {
            public string NextFile;
            public string PickFile(string title, string filter) => NextFile;
        }

        private sealed class FakeFolderPicker : IFolderPicker
        {
            public string PickFolder(string title, string initialDirectory) => null;
        }

        private sealed class FakeOverwritePrompter : IFileOverwritePrompter
        {
            public FileOverwriteChoice Choice = FileOverwriteChoice.Keep;
            public FileOverwriteChoice AskOverwrite(string displayName) => Choice;
        }

        private sealed class FakeDocumentOpener : IDocumentOpener
        {
            public string OpenedPath;
            public Task OpenAsync(string path, CancellationToken cancellationToken = default)
            {
                OpenedPath = path;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeDocumentReader : IDocumentReader
        {
            public SpecModel Spec;
            public SpecModel Read(string path, string mediaOutputDir = null) => Spec;
        }

        private sealed class FakeRobotGenerator : IRobotGenerator
        {
            public string EmitWorkflowXaml(RobotPlan plan) => "<Workflow/>";
            public string EmitHelperCode(RobotPlan plan) => "class Actions {}";
        }

        private sealed class InMemoryFileWriter : IFileWriter
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly HashSet<string> Directories = new HashSet<string>();

            public bool FileExists(string path) => Files.ContainsKey(path);
            public string ReadAllText(string path) => Files.TryGetValue(path, out var v) ? v : null;
            public void WriteAllText(string path, string content) => Files[path] = content;
            public void CreateDirectory(string path) => Directories.Add(path);
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
                => Files[destinationPath] = Files.TryGetValue(sourcePath, out var v) ? v : "";
            public bool DirectoryExists(string path) => Directories.Contains(path);
            public IReadOnlyList<string> EnumerateFiles(string path) => Files.Keys.Where(k => k.StartsWith(path)).ToList();
            public void DeleteDirectory(string path, bool recursive) => Directories.Remove(path);
        }

        private sealed class InMemoryKnowledgeBase : IKnowledgeBase
        {
            public readonly List<SolutionRecord> Records = new List<SolutionRecord>();
            public Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default)
            {
                Records.Add(record);
                return Task.CompletedTask;
            }
            public Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default)
                => Task.FromResult(Records.RemoveAll(r => predicate(r)));
            public Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SolutionRecord>>(Records);
            public Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SimilarTaskMatch>>(new List<SimilarTaskMatch>());
            public Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default)
                => Task.FromResult<SolutionRecord>(null);
        }

        #endregion

        private static SpecTabModel CreateModel(
            Func<IAiProvider> aiProvider = null,
            FakeSolutionPathProvider solution = null,
            FakeFilePicker filePicker = null,
            InMemoryFileWriter fileWriter = null,
            FakeDocumentReader documentReader = null,
            InMemoryKnowledgeBase kb = null)
        {
            return new SpecTabModel(
                aiProvider ?? (() => new StubAiProvider()),
                solution ?? new FakeSolutionPathProvider { Directory = "C:\\sln" },
                filePicker ?? new FakeFilePicker { NextFile = "C:\\sln\\spec.docx" },
                new FakeFolderPicker(),
                fileWriter ?? new InMemoryFileWriter(),
                new FakeOverwritePrompter(),
                (title, message) => true,
                new FakeDocumentOpener(),
                documentReader ?? new FakeDocumentReader { Spec = new SpecModel { Name = "RPA-1", RawText = "Open browser.", MarkdownText = "# Spec" } },
                new FakeRobotGenerator(),
                path => kb ?? new InMemoryKnowledgeBase(),
                () => new SettingsData { KnowledgeBasePath = "C:\\kb" });
        }

        [Test]
        public void Model_CanBeConstructedWithoutViewModelAndView()
        {
            var model = CreateModel();

            Assert.That(model.ViewModel, Is.Null);
            Assert.That(model.CurrentSpec, Is.Null);
            Assert.That(model.IsImporting, Is.False);
            Assert.That(model.HasOutput, Is.False);
        }

        [Test]
        public async Task ImportSpecAsync_WhenNoSolution_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel(solution: new FakeSolutionPathProvider { Directory = null });
            model.Notify += (s, n) => notifications.Add(n);

            await model.ImportSpecAsync();

            Assert.That(model.CurrentSpec, Is.Null);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }

        [Test]
        public async Task ImportSpecAsync_ImportsSpecAndSetsState()
        {
            var fileWriter = new InMemoryFileWriter();
            var model = CreateModel(fileWriter: fileWriter);

            await model.ImportSpecAsync();

            Assert.That(model.CurrentSpec, Is.Not.Null);
            Assert.That(model.CurrentSpec.Name, Is.EqualTo("RPA-1"));
            Assert.That(model.IsImporting, Is.False);
            Assert.That(model.HasOutput, Is.True);
            Assert.That(model.SpecMessages, Is.Not.Empty);
            Assert.That(fileWriter.Files.ContainsKey("C:\\sln\\docs\\spec.md"), Is.True);
            Assert.That(fileWriter.Files.ContainsKey("C:\\sln\\docs\\tasks.md"), Is.True);
        }

        [Test]
        public void ClearSpecCommand_WhenNoSolution_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel(solution: new FakeSolutionPathProvider { Directory = null });
            model.Notify += (s, n) => notifications.Add(n);

            model.ClearSpecCommand();

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }

        [Test]
        public async Task GenerateAlgorithmAsync_WhenNoAlgorithmProvider_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel(aiProvider: () => null);
            model.Notify += (s, n) => notifications.Add(n);

            await model.GenerateAlgorithmAsync();

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }

        [Test]
        public async Task GenerateRobotAsync_WhenNoSpec_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel();
            model.Notify += (s, n) => notifications.Add(n);

            await model.GenerateRobotAsync();

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Message, Is.EqualTo("Import a spec first."));
        }

        [Test]
        public async Task FindSimilarAsync_WhenNoSpec_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel();
            model.Notify += (s, n) => notifications.Add(n);

            await model.FindSimilarAsync();

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Message, Is.EqualTo("Import a spec first."));
        }

        [Test]
        public async Task SecurityAuditAsync_WhenNoAiProvider_ReportsWarning()
        {
            var notifications = new List<Notify>();
            var model = CreateModel(aiProvider: () => null);
            model.Notify += (s, n) => notifications.Add(n);

            await model.SecurityAuditAsync();

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Warning));
        }
    }
}
