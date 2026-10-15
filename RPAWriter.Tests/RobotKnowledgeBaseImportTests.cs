using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
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
            Assert.That(specMd, Does.Contain("Generated:"));
            Assert.That(specMd, Does.Contain("UTC"));

            var tasksMd = File.ReadAllText(Path.Combine(_kb, "RPA-0001-tasks.md"));
            Assert.That(tasksMd, Does.Contain("Solution file(s)"));
            Assert.That(tasksMd, Does.Contain("Generated:"));

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
    }
}
