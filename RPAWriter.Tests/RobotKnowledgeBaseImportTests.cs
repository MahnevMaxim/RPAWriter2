using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        }
    }
}
