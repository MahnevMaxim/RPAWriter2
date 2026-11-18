using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class RemoteLogServiceTests
    {
        private readonly RemoteLogService _service = new RemoteLogService();

        [Test]
        public void DownloadAsync_ThrowsNotSupported()
        {
            Assert.Throws<NotSupportedException>(() => _service.DownloadAsync(new RobotDeploymentConfig()));
        }

        [Test]
        public async Task AnalyzeAsync_Null_ReturnsEmptyAnalysis()
        {
            var analysis = await _service.AnalyzeAsync(null);

            Assert.That(analysis, Is.Not.Null);
            Assert.That(analysis.Entries, Is.Empty);
            Assert.That(analysis.Errors, Is.Empty);
            Assert.That(analysis.Warnings, Is.Empty);
        }

        [Test]
        public async Task AnalyzeAsync_DelegatesToLogParser()
        {
            var lines = new[]
            {
                "2024-01-01 10:00:00 [Scheduler] ERROR failed to start",
                "2024-01-01 10:00:01 [Scheduler] WARN retrying",
                "2024-01-01 10:00:02 [Scheduler] INFO started"
            };

            var analysis = await _service.AnalyzeAsync(lines);

            Assert.That(analysis.Entries.Count, Is.EqualTo(3));
            Assert.That(analysis.Errors.Count, Is.EqualTo(1));
            Assert.That(analysis.Warnings.Count, Is.EqualTo(1));
            Assert.That(analysis.Summary, Does.Contain("errors: 1"));
            Assert.That(analysis.Summary, Does.Contain("warnings: 1"));
        }
    }
}
