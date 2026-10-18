using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class RobotKnowledgeBaseImportTests
    {
        private string _root;
        private string _kb;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());
            _kb = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            if (Directory.Exists(_kb)) Directory.Delete(_kb, true);
        }

        [Test]
        public async Task ImportFromRobots_WritesSpecTasksAndIndex()
        {
            var robotDir = Path.Combine(_root, "RPA-0001");
            Directory.CreateDirectory(robotDir);
            File.WriteAllText(Path.Combine(robotDir, "Main.xaml"),
                "<Activity><InvokeMethod MethodName=\"OpenBrowser\" /></Activity>");
            File.WriteAllText(Path.Combine(robotDir, "RpaActions.cs"),
                "public static void OpenBrowser() { } public static void ReadTable() { }");

            var service = new RobotKnowledgeBaseImportService(new StubAiProvider());
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(result.SpecFilesWritten, Is.EqualTo(1));
            Assert.That(result.TaskFilesWritten, Is.EqualTo(1));
            Assert.That(result.TasksAdded, Is.GreaterThan(0));

            var specMd = File.ReadAllText(Path.Combine(_kb, "RPA-0001-spec.md"));
            Assert.That(specMd, Does.Contain("Сформировано:"));
            Assert.That(specMd, Does.Contain("UTC"));

            var tasksMd = File.ReadAllText(Path.Combine(_kb, "RPA-0001-tasks.md"));
            Assert.That(tasksMd, Does.Contain("Файл(ы) решения"));
            Assert.That(tasksMd, Does.Contain("Сформировано:"));

            var kb = new FileKnowledgeBase(_kb);
            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(result.TasksAdded));
            Assert.That(all.All(r => r.SolutionPaths != null && r.SolutionPaths.Count > 0), Is.True);
            Assert.That(all.All(r => r.SpecGeneratedUtc != default(DateTime)), Is.True);
        }

        [Test]
        public async Task ImportFromRobots_MissingRoot_ReturnsZero()
        {
            var service = new RobotKnowledgeBaseImportService(new StubAiProvider());
            var result = await service.ImportFromRobotsAsync(Path.Combine(_root, "does-not-exist"), _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(0));
            Assert.That(result.TasksAdded, Is.EqualTo(0));
        }

        [Test]
        public async Task ImportFromRobots_ReversesOnlyProjectFolderAndReachableFiles()
        {
            var solutionDir = Path.Combine(_root, "RPA-0001");
            var projectDir = Path.Combine(solutionDir, "RPA-0001");
            var auxDir = Path.Combine(solutionDir, "Common");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(auxDir);
            File.WriteAllText(Path.Combine(solutionDir, "RPA-0001.sln"), "");
            File.WriteAllText(Path.Combine(projectDir, "Main.xaml"),
                "<Activity><InvokeMethod TargetType=\"RPA0001.RpaActions\" MethodName=\"OpenBrowser\" /><InvokeWorkflowFile WorkflowFileName=\"Sub.xaml\" /></Activity>");
            File.WriteAllText(Path.Combine(projectDir, "Sub.xaml"),
                "<Activity><InvokeMethod TargetType=\"RPA0001.RpaActions\" MethodName=\"ReadTable\" /></Activity>");
            File.WriteAllText(Path.Combine(projectDir, "RpaActions.cs"),
                "public class RpaActions { public static void OpenBrowser() { } public static void ReadTable() { } }");
            File.WriteAllText(Path.Combine(auxDir, "Common.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(auxDir, "Common.cs"), "public class Common { }");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(ai.LastPlatform, Is.EqualTo(RobotPlatform.Mwf));
            Assert.That(ai.LastRobotName, Is.EqualTo("RPA-0001"));

            var paths = ai.LastFiles.Select(f => f.RelativePath.Replace('\\', '/')).ToList();
            Assert.That(paths, Does.Contain("RPA-0001/RPA-0001/Main.xaml"));
            Assert.That(paths, Does.Contain("RPA-0001/RPA-0001/Sub.xaml"));
            Assert.That(paths, Does.Contain("RPA-0001/RPA-0001/RpaActions.cs"));
            Assert.That(paths, Does.Not.Contain("RPA-0001/Common/Common.xaml"));
            Assert.That(paths, Does.Not.Contain("RPA-0001/Common/Common.cs"));
            Assert.That(result.Warnings, Is.Empty);
        }
        [Test]
        public async Task ImportFromRobots_CsprojWithWorkflowXaml_DetectedAsMwf()
        {
            var projectDir = Path.Combine(_root, "RPA-0001");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "RPA-0001.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(projectDir, "Workflow.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(projectDir, "RpaActions.cs"), "public class RpaActions { }");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(ai.LastPlatform, Is.EqualTo(RobotPlatform.Mwf));
        }

        [Test]
        public async Task ImportFromRobots_CsprojCodeOnly_DetectedAsWr()
        {
            var projectDir = Path.Combine(_root, "RPA-0001");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "RPA-0001.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(projectDir, "RpaActions.cs"), "public class RpaActions { }");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(ai.LastPlatform, Is.EqualTo(RobotPlatform.Wr));
        }


        [Test]
        public async Task ImportFromRobots_Mwf_SkipsObjAndBinWhenCsprojPresent()
        {
            var projectDir = Path.Combine(_root, "RPA-0001");
            var objDir = Path.Combine(projectDir, "obj");
            var binDir = Path.Combine(projectDir, "bin");
            Directory.CreateDirectory(objDir);
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(projectDir, "RPA-0001.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(projectDir, "Main.xaml"),
                "<Activity><InvokeWorkflowFile WorkflowFileName=Sub.xaml /></Activity>");
            File.WriteAllText(Path.Combine(projectDir, "Sub.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(objDir, "Generated.cs"), "public class Generated { }");
            File.WriteAllText(Path.Combine(objDir, "Main.g.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(binDir, "Copied.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(binDir, "Copied.cs"), "public class Copied { }");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(ai.LastPlatform, Is.EqualTo(RobotPlatform.Mwf));

            var paths = ai.LastFiles.Select(f => f.RelativePath).ToList();
            Assert.That(paths, Does.Contain(Path.Combine("RPA-0001", "Main.xaml")));
            Assert.That(paths, Does.Contain(Path.Combine("RPA-0001", "Sub.xaml")));

            var segments = ai.LastFiles.SelectMany(f => f.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Assert.That(segments.Any(s => s.Equals("obj", StringComparison.OrdinalIgnoreCase) || s.Equals("bin", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        [Test]
        public async Task ImportFromRobots_NoCsproj_KeepsObjFolder()
        {
            var projectDir = Path.Combine(_root, "RPA-0001");
            var objDir = Path.Combine(projectDir, "obj");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(projectDir, "Main.xaml"),
                "<Activity><InvokeWorkflowFile WorkflowFileName=Sub.xaml /></Activity>");
            File.WriteAllText(Path.Combine(objDir, "Sub.xaml"), "<Activity />");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            var paths = ai.LastFiles.Select(f => f.RelativePath).ToList();
            Assert.That(paths, Does.Contain(Path.Combine("RPA-0001", "obj", "Sub.xaml")));
        }

        [Test]
        public async Task ImportFromRobots_MissingEntryPoint_WarnsAndFallsBack()
        {
            var solutionDir = Path.Combine(_root, "RPA-0001");
            var projectDir = Path.Combine(solutionDir, "RPA-0001");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "Other.xaml"), "<Activity />");
            File.WriteAllText(Path.Combine(projectDir, "Other.cs"), "public class Other { }");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.Warnings.Any(w => w.Contains("main.xaml")), Is.True);
            Assert.That(ai.LastFiles.Count, Is.EqualTo(2));
            Assert.That(ai.LastFiles.All(f => string.IsNullOrEmpty(f.Content)), Is.True);
        }

        [Test]
        public async Task ImportFromRobots_CyclicReferences_TerminateWithoutDuplicates()
        {
            var projectDir = Path.Combine(_root, "RPA-0001");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "Main.xaml"),
                "<Activity><InvokeWorkflowFile WorkflowFileName=\"A.xaml\" /></Activity>");
            File.WriteAllText(Path.Combine(projectDir, "A.xaml"),
                "<Activity><InvokeWorkflowFile WorkflowFileName=\"B.xaml\" /></Activity>");
            File.WriteAllText(Path.Combine(projectDir, "B.xaml"),
                "<Activity><InvokeWorkflowFile WorkflowFileName=\"A.xaml\" /></Activity>");

            var ai = new RecordingAiProvider();
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            var paths = ai.LastFiles.Select(f => f.RelativePath.Replace('\\', '/')).ToList();
            Assert.That(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(paths.Count));
            Assert.That(paths.Count, Is.EqualTo(3));
            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
        }

        [Test]
        public async Task ImportFromRobots_Resume_SkipsCompletedRobots()
        {
            CreateRobotDir(_root, "RPA-0001");
            CreateRobotDir(_root, "RPA-0002");

            var failingAi = new ResumableTestAiProvider { FailOnRobot = "RPA-0002" };
            var service = new RobotKnowledgeBaseImportService(failingAi);

            Assert.ThrowsAsync<HttpRequestException>(async () => await service.ImportFromRobotsAsync(_root, _kb));

            Assert.That(File.Exists(Path.Combine(_kb, "RPA-0001-spec.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(_kb, "import-state.txt")), Is.True);

            var resumingAi = new ResumableTestAiProvider();
            var resumingService = new RobotKnowledgeBaseImportService(resumingAi);
            var result = await resumingService.ImportFromRobotsAsync(_root, _kb, mode: KnowledgeBaseFormationMode.Continue);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(resumingAi.ReconstructedRobots, Is.EquivalentTo(new[] { "RPA-0002" }));
        }

        [Test]
        public async Task ImportFromRobots_Override_ClearsStateAndRebuilds()
        {
            CreateRobotDir(_root, "RPA-0001");
            CreateRobotDir(_root, "RPA-0002");

            var failingAi = new ResumableTestAiProvider { FailOnRobot = "RPA-0002" };
            var service = new RobotKnowledgeBaseImportService(failingAi);

            Assert.ThrowsAsync<HttpRequestException>(async () => await service.ImportFromRobotsAsync(_root, _kb));

            Assert.That(File.Exists(Path.Combine(_kb, "RPA-0001-spec.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(_kb, "import-state.txt")), Is.True);
            var kbBefore = new FileKnowledgeBase(_kb);
            Assert.That((await kbBefore.GetAllAsync()).Count, Is.GreaterThan(0));

            var rebuildAi = new ResumableTestAiProvider();
            var rebuildService = new RobotKnowledgeBaseImportService(rebuildAi);
            var result = await rebuildService.ImportFromRobotsAsync(_root, _kb, mode: KnowledgeBaseFormationMode.Override);

            Assert.That(result.RobotsProcessed, Is.EqualTo(2));
            Assert.That(rebuildAi.ReconstructedRobots, Is.EquivalentTo(new[] { "RPA-0001", "RPA-0002" }));

            // Override must not accumulate records from the first (interrupted) run:
            // both robots are identical, so each must end up with the same record count.
            var kbAfter = new FileKnowledgeBase(_kb);
            var all = await kbAfter.GetAllAsync();
            var count1 = all.Count(r => r.SourceRobot == "RPA-0001");
            var count2 = all.Count(r => r.SourceRobot == "RPA-0002");
            Assert.That(count2, Is.GreaterThan(0));
            Assert.That(count1, Is.EqualTo(count2));
        }

        [Test]
        public async Task ImportFromRobots_MappingRetriesThenSucceeds()
        {
            CreateRobotDir(_root, "RPA-0001");

            var ai = new FlakySolutionAiProvider { FailuresBeforeSuccess = 2 };
            var service = new RobotKnowledgeBaseImportService(ai);
            var result = await service.ImportFromRobotsAsync(_root, _kb);

            Assert.That(result.RobotsProcessed, Is.EqualTo(1));
            Assert.That(result.TasksAdded, Is.EqualTo(1));
            Assert.That(ai.MappingCalls, Is.EqualTo(3));
        }

        [Test]
        public async Task ImportFromRobots_MappingFailsAllRetries_Throws()
        {
            CreateRobotDir(_root, "RPA-0001");

            var ai = new FlakySolutionAiProvider { AlwaysFail = true };
            var service = new RobotKnowledgeBaseImportService(ai);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ImportFromRobotsAsync(_root, _kb));
            Assert.That(ai.MappingCalls, Is.EqualTo(5));
        }

        private static void CreateRobotDir(string root, string robotName)
        {
            var dir = Path.Combine(root, robotName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Main.xaml"),
                "<Activity><InvokeMethod MethodName=\"OpenBrowser\" /></Activity>");
            File.WriteAllText(Path.Combine(dir, "RpaActions.cs"),
                "public static void OpenBrowser() { }");
        }

        private sealed class RecordingAiProvider : IAiProvider
        {
            public string LastRobotName { get; private set; }
            public RobotPlatform LastPlatform { get; private set; }
            public IReadOnlyList<RobotSourceFile> LastFiles { get; private set; }

            public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
            {
                LastRobotName = robotName;
                LastFiles = robotFiles;
                LastPlatform = platform;
                return Task.FromResult(new SpecModel { Name = robotName, Description = "d" });
            }

            public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<TaskDescriptor>>(new List<TaskDescriptor>());

            public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
                => Task.FromResult(new SpecAnalysis());

            public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
                => Task.FromResult(new RobotPlan { Name = "R" });

            public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => Task.FromResult(new SolutionRecord { Id = Guid.NewGuid().ToString("N") });

            public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse { Answer = "stub" });

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse { Answer = "stub" });

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse { Answer = "stub" });
        }

        private sealed class ResumableTestAiProvider : IAiProvider
        {
            private readonly StubAiProvider _inner = new StubAiProvider();
            public List<string> ReconstructedRobots { get; } = new List<string>();
            public string FailOnRobot { get; set; }

            public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
            {
                ReconstructedRobots.Add(robotName);
                if (robotName == FailOnRobot)
                    throw new HttpRequestException("AI provider unavailable (simulated)");
                return _inner.ReconstructSpecAsync(robotName, robotFiles, cancellationToken, platform);
            }

            public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
                => _inner.DecomposeTasksAsync(text, cancellationToken);

            public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
                => _inner.AnalyzeSpecAsync(spec, cancellationToken);

            public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
                => _inner.GenerateRobotAsync(analysis, cancellationToken);

            public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => _inner.MapTaskToSolutionAsync(task, robotFiles, cancellationToken, platform);

            public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(message, cancellationToken);

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(messages, systemPrompt, cancellationToken);

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(messages, systemPrompt, tools, cancellationToken);
        }

        private sealed class FlakySolutionAiProvider : IAiProvider
        {
            private readonly StubAiProvider _inner = new StubAiProvider();
            public int MappingCalls { get; private set; }
            public int FailuresBeforeSuccess { get; set; }
            public bool AlwaysFail { get; set; }

            public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => _inner.ReconstructSpecAsync(robotName, robotFiles, cancellationToken, platform);

            public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<TaskDescriptor>>(new List<TaskDescriptor>
                {
                    new TaskDescriptor { Action = "open", Description = "Open browser" }
                });

            public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
                => _inner.AnalyzeSpecAsync(spec, cancellationToken);

            public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
                => _inner.GenerateRobotAsync(analysis, cancellationToken);

            public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
            {
                MappingCalls++;
                if (AlwaysFail || MappingCalls <= FailuresBeforeSuccess)
                    throw new InvalidOperationException("JsonToken EndConstructor is not valid for closing JsonType Object.");
                return Task.FromResult(new SolutionRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SolutionPaths = new List<string> { "Main.xaml" }
                });
            }

            public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(message, cancellationToken);

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(messages, systemPrompt, cancellationToken);

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
                => _inner.ChatAsync(messages, systemPrompt, tools, cancellationToken);
        }
    }
}
