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
    public class RobotSecurityAuditServiceTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        /// <summary>Captures user content sent to the AI and returns a canned Markdown section.</summary>
        private sealed class RecordingAiProvider : IAiProvider
        {
            public int ChatCalls { get; private set; }
            public List<string> UserContents { get; } = new List<string>();
            public List<string> SystemPrompts { get; } = new List<string>();
            public string Answer { get; set; } =
                "### [High] Hardcoded password\n" +
                "- **Location**: `Main.xaml`\n" +
                "- **Evidence**: `password = \"secret\"`\n" +
                "- **Risk**: credential theft\n" +
                "- **Recommendation**: use a secrets store\n\n" +
                "## Summary\n\n" +
                "- Critical: 0, High: 1, Medium: 0, Low: 0, Info: 0\n" +
                "- Overall: low risk\n";

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
            {
                ChatCalls++;
                SystemPrompts.Add(systemPrompt ?? "");
                var content = (messages ?? new List<ChatMessageData>())
                    .LastOrDefault(m => m != null && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content ?? "";
                UserContents.Add(content);
                return Task.FromResult(new ChatResponse { Answer = Answer, Reasoning = "" });
            }

            public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
                => ChatAsync(new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = message } }, "", cancellationToken);

            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
                => ChatAsync(messages, systemPrompt, cancellationToken);

            public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
                => Task.FromResult(new SpecAnalysis { Summary = "stub", Tasks = new List<TaskDescriptor>() });

            public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
                => Task.FromResult(new RobotPlan { Name = "stub" });

            public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<TaskDescriptor>>(new List<TaskDescriptor>());

            public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => Task.FromResult(new SpecModel { Name = robotName });

            public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf)
                => Task.FromResult(new SolutionRecord { Id = Guid.NewGuid().ToString("N"), SolutionPaths = new List<string> { "Main.xaml" } });
        }

        /// <summary>Synchronous progress sink so tests can assert on emitted log messages deterministically.</summary>
        private sealed class ListProgress<T> : IProgress<T>
        {
            public List<T> Items { get; } = new List<T>();
            public void Report(T value) => Items.Add(value);
        }

        [TestCase("RPA-1234.sln", true)]
        [TestCase("RPA-653.sln", true)]
        [TestCase("RPA-1234.slnx", true)]
        [TestCase("RPA-653.slnx", true)]
        [TestCase("RPA-12.sln", true)]
        [TestCase("RPA-1234.SLN", true)]
        [TestCase("rpa-1234.sln", false)]
        [TestCase("Rpa-1234.sln", false)]
        [TestCase("RPA-.sln", false)]
        [TestCase("RPA-abc.sln", false)]
        [TestCase("RPA-1234.slnxx", false)]
        [TestCase("RPA-1234.csproj", false)]
        [TestCase("RPA-1234.sln.tmp", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsRobotSolution_MatchesExpectedPattern(string fileName, bool expected)
        {
            Assert.That(RobotSecurityAuditService.IsRobotSolution(fileName), Is.EqualTo(expected));
        }

        [Test]
        public void FindRobotSolutions_ReturnsOnlySiblingRobotSolutions_Sorted()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "Nested"));
            File.WriteAllText(Path.Combine(_root, "RPA-1234.sln"), "");
            File.WriteAllText(Path.Combine(_root, "RPA-653.sln"), "");
            File.WriteAllText(Path.Combine(_root, "RPA-999.slnx"), "");
            File.WriteAllText(Path.Combine(_root, "RPA-12.sln"), "");
            File.WriteAllText(Path.Combine(_root, "rpa-1.sln"), "");
            File.WriteAllText(Path.Combine(_root, "RPA-abc.sln"), "");
            File.WriteAllText(Path.Combine(_root, "README.md"), "");
            File.WriteAllText(Path.Combine(_root, "Nested", "RPA-77.sln"), "");

            var result = RobotSecurityAuditService.FindRobotSolutions(_root);

            var names = result.Select(Path.GetFileName).ToList();
            Assert.That(names, Is.EqualTo(new[] { "RPA-12.sln", "RPA-1234.sln", "RPA-653.sln", "RPA-999.slnx" }));
        }

        [Test]
        public void FindRobotSolutions_MissingDirectory_ReturnsEmpty()
        {
            Assert.That(RobotSecurityAuditService.FindRobotSolutions(Path.Combine(_root, "missing")), Is.Empty);
        }

        [Test]
        public async Task AuditAsync_WritesReportAndLogsPipeline()
        {
            var solutionDir = Path.Combine(_root, "RPA-1234");
            Directory.CreateDirectory(Path.Combine(solutionDir, "bin"));
            Directory.CreateDirectory(Path.Combine(solutionDir, "obj"));
            Directory.CreateDirectory(Path.Combine(solutionDir, "packages"));
            File.WriteAllText(Path.Combine(solutionDir, "RPA-1234.sln"), "SOLUTION");
            File.WriteAllText(Path.Combine(solutionDir, "Main.xaml"), "<Activity SECRET_MAIN />");
            File.WriteAllText(Path.Combine(solutionDir, "RpaActions.cs"), "public void Go() { }");
            File.WriteAllText(Path.Combine(solutionDir, "bin", "Generated.cs"), "SECRET_BIN");
            File.WriteAllText(Path.Combine(solutionDir, "obj", "Generated.cs"), "SECRET_OBJ");
            File.WriteAllText(Path.Combine(solutionDir, "packages", "X.cs"), "SECRET_PACKAGES");

            var ai = new RecordingAiProvider();
            var progress = new ListProgress<string>();
            var progressValue = new ListProgress<double>();
            var reportPath = Path.Combine(_root, "docs", "security_audit.md");

            var service = new RobotSecurityAuditService(ai);
            var result = await service.AuditAsync(
                new[] { Path.Combine(solutionDir, "RPA-1234.sln") }, reportPath, progress, progressValue);

            Assert.That(result.SolutionsAudited, Is.EqualTo(1));
            Assert.That(result.SolutionsFailed, Is.EqualTo(0));

            var report = File.ReadAllText(reportPath);
            Assert.That(report, Does.Contain("# Security Audit Report"));
            Assert.That(report, Does.Contain("Mode: full static analysis"));
            Assert.That(report, Does.Contain("## RPA-1234.sln"));
            Assert.That(report, Does.Contain("| RPA-1234.sln | 0 | 1 | 0 | 0 | 0 | OK |"));

            var manifest = string.Join("\n", ai.UserContents);
            Assert.That(manifest, Does.Contain("SECRET_MAIN"));
            Assert.That(manifest, Does.Contain("public void Go"));
            Assert.That(manifest, Does.Contain("SOLUTION"));
            Assert.That(manifest, Does.Not.Contain("SECRET_BIN"));
            Assert.That(manifest, Does.Not.Contain("SECRET_OBJ"));
            Assert.That(manifest, Does.Not.Contain("SECRET_PACKAGES"));

            var logText = string.Join("\n", progress.Items);
            Assert.That(logText, Does.Contain("Collecting source files for RPA-1234.sln"));
            Assert.That(logText, Does.Contain("Collected 3 file(s) for RPA-1234.sln"));
            Assert.That(logText, Does.Contain("Security audit completed"));
            Assert.That(progressValue.Items.Last(), Is.EqualTo(100.0));
        }

        [Test]
        public async Task AuditAsync_EmptyAiResponse_MarksFailed()
        {
            var solutionDir = Path.Combine(_root, "RPA-653");
            Directory.CreateDirectory(solutionDir);
            File.WriteAllText(Path.Combine(solutionDir, "RPA-653.sln"), "SOLUTION");
            File.WriteAllText(Path.Combine(solutionDir, "Main.xaml"), "<Activity />");

            var ai = new RecordingAiProvider { Answer = "" };
            var reportPath = Path.Combine(_root, "docs", "security_audit.md");

            var result = await new RobotSecurityAuditService(ai).AuditAsync(
                new[] { Path.Combine(solutionDir, "RPA-653.sln") }, reportPath);

            Assert.That(result.SolutionsFailed, Is.EqualTo(1));
            Assert.That(result.SolutionsAudited, Is.EqualTo(1));

            var report = File.ReadAllText(reportPath);
            Assert.That(report, Does.Contain("| RPA-653.sln |"));
            Assert.That(report, Does.Contain("FAILED"));
            Assert.That(report, Does.Contain("### FAILED"));
        }

        [Test]
        public async Task AuditAsync_MissingSolutionFile_AddsWarning()
        {
            var reportPath = Path.Combine(_root, "docs", "security_audit.md");
            var missing = Path.Combine(_root, "RPA-999.sln");
            var ai = new RecordingAiProvider();

            var result = await new RobotSecurityAuditService(ai).AuditAsync(new[] { missing }, reportPath);

            Assert.That(result.SolutionsAudited, Is.EqualTo(0));
            Assert.That(result.Warnings, Has.Some.Contains("Solution file not found"));
            Assert.That(ai.ChatCalls, Is.EqualTo(0));

            var report = File.ReadAllText(reportPath);
            Assert.That(report, Does.Contain("# Security Audit Report"));
            Assert.That(report, Does.Contain("## Summary"));
        }

        [Test]
        public async Task AuditAsync_ChunksLargeManifest()
        {
            var solutionDir = Path.Combine(_root, "RPA-42");
            Directory.CreateDirectory(solutionDir);
            File.WriteAllText(Path.Combine(solutionDir, "RPA-42.sln"), "SOLUTION");
            var big = new string('a', 20000);
            for (var i = 0; i < 8; i++)
                File.WriteAllText(Path.Combine(solutionDir, "File" + i + ".cs"), big);

            var ai = new RecordingAiProvider();
            var progress = new ListProgress<string>();
            var reportPath = Path.Combine(_root, "docs", "security_audit.md");

            var result = await new RobotSecurityAuditService(ai).AuditAsync(
                new[] { Path.Combine(solutionDir, "RPA-42.sln") }, reportPath, progress);

            Assert.That(result.SolutionsAudited, Is.EqualTo(1));
            Assert.That(result.SolutionsFailed, Is.EqualTo(0));
            Assert.That(ai.ChatCalls, Is.EqualTo(2));

            var logText = string.Join("\n", progress.Items);
            Assert.That(logText, Does.Contain("in 2 chunk(s)"));
            Assert.That(logText, Does.Contain("chunk 2/2"));
        }

        [Test]
        public async Task AuditAsync_CredentialsOnly_UsesFocusedPrompt()
        {
            var solutionDir = Path.Combine(_root, "RPA-777");
            Directory.CreateDirectory(solutionDir);
            File.WriteAllText(Path.Combine(solutionDir, "RPA-777.sln"), "SOLUTION");
            File.WriteAllText(Path.Combine(solutionDir, "Main.xaml"), "<Activity />");

            var ai = new RecordingAiProvider();
            var reportPath = Path.Combine(_root, "docs", "security_audit.md");

            var result = await new RobotSecurityAuditService(ai).AuditAsync(
                new[] { Path.Combine(solutionDir, "RPA-777.sln") }, reportPath, mode: SecurityAuditMode.CredentialsOnly);

            Assert.That(result.SolutionsAudited, Is.EqualTo(1));
            Assert.That(result.SolutionsFailed, Is.EqualTo(0));
            Assert.That(ai.ChatCalls, Is.EqualTo(1));
            Assert.That(ai.SystemPrompts, Has.Count.EqualTo(1));
            Assert.That(ai.SystemPrompts[0], Does.Contain("hardcoded credentials"));
            Assert.That(ai.SystemPrompts[0], Does.Contain("data-flow analysis"));
            Assert.That(ai.SystemPrompts[0], Does.Not.Contain("Injection"));

            var report = File.ReadAllText(reportPath);
            Assert.That(report, Does.Contain("Mode: credentials-focused scan"));
            Assert.That(report, Does.Contain("## RPA-777.sln"));
        }
    }
}
