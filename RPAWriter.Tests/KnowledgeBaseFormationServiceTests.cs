using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class KnowledgeBaseFormationServiceTests
    {
        private sealed class FakeDocumentReader : IDocumentReader
        {
            public Func<string, SpecModel> OnRead { get; set; }

            public SpecModel Read(string path) => OnRead?.Invoke(path) ?? new SpecModel { RawText = path };
        }

        private sealed class FakeAiProvider : IAiProvider
        {
            public Func<string, IReadOnlyList<TaskDescriptor>> OnDecompose { get; set; }
            public Func<TaskDescriptor, IReadOnlyList<RobotSourceFile>, SolutionRecord> OnMap { get; set; }

            public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
                => Task.FromResult(new SpecAnalysis());

            public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
                => Task.FromResult(new RobotPlan());

            public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<TaskDescriptor>>(OnDecompose?.Invoke(text) ?? new List<TaskDescriptor>());

            public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => Task.FromResult(new SpecModel());

            public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => Task.FromResult(OnMap?.Invoke(task, robotFiles) ?? new SolutionRecord());

            public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse());

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse());

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse());
        }

        private sealed class FakeKnowledgeBase : IKnowledgeBase
        {
            public List<SolutionRecord> Added { get; } = new List<SolutionRecord>();

            public Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default)
            {
                Added.Add(record);
                return Task.CompletedTask;
            }

            public Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default)
                => Task.FromResult(0);

            public Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SolutionRecord>>(Added);

            public Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<SimilarTaskMatch>>(new List<SimilarTaskMatch>());

            public Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default)
                => Task.FromResult<SolutionRecord>(null);
        }

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            var reader = new FakeDocumentReader();
            var ai = new FakeAiProvider();
            var kb = new FakeKnowledgeBase();

            Assert.That(() => new KnowledgeBaseFormationService(null, ai, kb), Throws.ArgumentNullException);
            Assert.That(() => new KnowledgeBaseFormationService(reader, null, kb), Throws.ArgumentNullException);
            Assert.That(() => new KnowledgeBaseFormationService(reader, ai, null), Throws.ArgumentNullException);
        }

        [Test]
        public async Task BuildFromRobotDocsAsync_NullPaths_ReturnsZero()
        {
            var service = new KnowledgeBaseFormationService(new FakeDocumentReader(), new FakeAiProvider(), new FakeKnowledgeBase());

            var added = await service.BuildFromRobotDocsAsync(null, "code");

            Assert.That(added, Is.EqualTo(0));
        }

        [Test]
        public async Task BuildFromRobotDocsAsync_AddsSolutionPerTask()
        {
            var reader = new FakeDocumentReader { OnRead = path => new SpecModel { SourcePath = path, RawText = "spec text" } };
            var ai = new FakeAiProvider
            {
                OnDecompose = _ => new List<TaskDescriptor>
                {
                    new TaskDescriptor { Action = "open", Target = "site" },
                    new TaskDescriptor { Action = "read", Target = "table" }
                },
                OnMap = (task, _) => new SolutionRecord { Name = task.Action }
            };
            var kb = new FakeKnowledgeBase();

            var service = new KnowledgeBaseFormationService(reader, ai, kb);
            var added = await service.BuildFromRobotDocsAsync(new[] { "doc1.docx" }, "robot code");

            Assert.That(added, Is.EqualTo(2));
            Assert.That(kb.Added.Count, Is.EqualTo(2));
            Assert.That(kb.Added[0].Name, Is.EqualTo("open"));
            Assert.That(kb.Added[0].SourceRobot, Is.EqualTo("doc1.docx"));
            Assert.That(kb.Added[0].Documentation, Is.EqualTo("spec text"));
        }

        [Test]
        public void BuildFromRobotDocsAsync_Cancelled_Throws()
        {
            var ai = new FakeAiProvider
            {
                OnDecompose = _ => new List<TaskDescriptor> { new TaskDescriptor { Action = "open" } }
            };
            var service = new KnowledgeBaseFormationService(new FakeDocumentReader(), ai, new FakeKnowledgeBase());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await service.BuildFromRobotDocsAsync(new[] { "doc" }, "code", cts.Token),
                Throws.Exception.TypeOf<OperationCanceledException>());
        }
    }
}
