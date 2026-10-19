using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class LogAccessServiceTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "rpawriter_logs_" + Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public async Task LocalLogAccessService_NullPaths_ReturnsEmpty()
        {
            var result = await new LocalLogAccessService().ReadLocalLogsAsync(null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task LocalLogAccessService_SkipsMissingFiles()
        {
            Directory.CreateDirectory(_dir);

            var result = await new LocalLogAccessService().ReadLocalLogsAsync(new[] { Path.Combine(_dir, "missing.log") });

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task LocalLogAccessService_ReadsExistingFiles()
        {
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, "robot.log");
            File.WriteAllLines(file, new[] { "line1", "line2" });

            var result = await new LocalLogAccessService().ReadLocalLogsAsync(new[] { file });

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo("line1"));
            Assert.That(result[1], Is.EqualTo("line2"));
        }

        [Test]
        public void LocalLogAccessService_Cancelled_Throws()
        {
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, "robot.log");
            File.WriteAllLines(file, new[] { "line1" });
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await new LocalLogAccessService().ReadLocalLogsAsync(new[] { file }, cts.Token),
                Throws.Exception.TypeOf<OperationCanceledException>());
        }

        [Test]
        public async Task RemoteLogService_AnalyzeAsync_DelegatesToLogAnalyzer()
        {
            var result = await new RemoteLogService().AnalyzeAsync(new[] { "2026-10-13 10:00:00 [Robot] ERROR boom" });

            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(result.Summary, Does.Contain("errors: 1"));
        }

        [Test]
        public void RemoteLogService_DownloadAsync_NotSupported()
        {
            Assert.That(
                async () => await new RemoteLogService().DownloadAsync(new RobotDeploymentConfig()),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
