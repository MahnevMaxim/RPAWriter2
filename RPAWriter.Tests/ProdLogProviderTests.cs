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
    public class ProdLogProviderTests
    {
        [Test]
        public void Stub_Name_IsStubOffline()
        {
            Assert.That(new StubProdLogProvider().Name, Is.EqualTo("Stub (offline)"));
        }

        [Test]
        public async Task Stub_GetTasksAsync_ReturnsTasksTaggedWithRobot()
        {
            var tasks = await new StubProdLogProvider().GetTasksAsync("RPA-1212", null);

            Assert.That(tasks, Is.Not.Empty);
            Assert.That(tasks.All(t => t.RobotName == "RPA-1212"), Is.True);
            Assert.That(tasks.All(t => !string.IsNullOrEmpty(t.TaskId)), Is.True);
            Assert.That(tasks.Any(t => t.Status == "Completed"), Is.True);
            Assert.That(tasks.Any(t => t.Status == "Failed"), Is.True);
        }

        [Test]
        public async Task Stub_GetLogsAsync_ReturnsStructuredEntries()
        {
            var logs = await new StubProdLogProvider().GetLogsAsync(new ProdTask(), null);

            Assert.That(logs, Is.Not.Empty);
            Assert.That(logs.All(l => l.Timestamp.HasValue), Is.True);
            Assert.That(logs.All(l => !string.IsNullOrEmpty(l.Message)), Is.True);
            Assert.That(logs.All(l => !string.IsNullOrEmpty(l.Source)), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Info), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Warning), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Error), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Fatal), Is.True);
        }

        [Test]
        public async Task Stub_GetLogsAsync_MinLevelError_ReturnsOnlyErrorAndAbove()
        {
            var query = new ProdLogQuery { MinLevel = LogLevel.Error };
            var logs = await new StubProdLogProvider().GetLogsAsync(new ProdTask(), query);

            Assert.That(logs, Is.Not.Empty);
            Assert.That(logs.All(l => l.Level >= LogLevel.Error), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Error), Is.True);
            Assert.That(logs.Any(l => l.Level == LogLevel.Fatal), Is.True);
        }

        [Test]
        public async Task Stub_GetLogsAsync_ReturnsParameters()
        {
            var logs = await new StubProdLogProvider().GetLogsAsync(new ProdTask(), null);

            Assert.That(logs.Any(l => l.Parameters != null && l.Parameters.Count > 0), Is.True);
        }

        [Test]
        public async Task Stub_GetTasksAsync_DateRange_FiltersRuns()
        {
            var now = DateTime.UtcNow;
            var query = new ProdLogQuery
            {
                FromUtc = now.AddHours(-2).AddMinutes(-1),
                ToUtc = now.AddMinutes(-1)
            };

            var tasks = await new StubProdLogProvider().GetTasksAsync("RPA-1212", query);

            Assert.That(tasks, Is.Not.Empty);
            Assert.That(tasks.All(t => t.StartedAtUtc >= query.FromUtc && t.StartedAtUtc <= query.ToUtc), Is.True);
        }

        [Test]
        public void Stub_GetTasksAsync_Cancelled_Throws()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await new StubProdLogProvider().GetTasksAsync("RPA-1212", null, cts.Token);
                });
            }
        }

        [Test]
        public void Stub_GetLogsAsync_Cancelled_Throws()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await new StubProdLogProvider().GetLogsAsync(new ProdTask(), null, cts.Token);
                });
            }
        }

        [Test]
        public void Discover_NullDirectory_ReturnsEmpty()
        {
            Assert.That(LogProviderLoader.Discover(null), Is.Empty);
        }

        [Test]
        public void Discover_WhitespaceDirectory_ReturnsEmpty()
        {
            Assert.That(LogProviderLoader.Discover("   "), Is.Empty);
        }

        [Test]
        public void Discover_MissingDirectory_ReturnsEmpty()
        {
            Assert.That(LogProviderLoader.Discover(@"C:\definitely\does\not\exist"), Is.Empty);
        }

        [Test]
        public void Discover_EmptyDirectory_ReturnsEmpty()
        {
            var dir = CreateTempDir();
            try
            {
                Assert.That(LogProviderLoader.Discover(dir), Is.Empty);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void Discover_FindsProviderAssemblyInDirectory()
        {
            var dir = CreateTempDir();
            try
            {
                var testDll = typeof(ProdLogProviderTests).Assembly.Location;
                File.Copy(testDll, Path.Combine(dir, "RPAWriter.Tests.dll"));

                var providers = LogProviderLoader.Discover(dir);

                Assert.That(providers.Any(p => p is TestProdLogProvider), Is.True);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void Discover_SkipsCoreAssembly()
        {
            var dir = CreateTempDir();
            try
            {
                var coreDll = typeof(IProdLogProvider).Assembly.Location;
                File.Copy(coreDll, Path.Combine(dir, "RPAWriter.Core.dll"));

                // A second copy of RPAWriter.Core must be ignored to avoid a type-identity mismatch.
                Assert.That(LogProviderLoader.Discover(dir), Is.Empty);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    /// <summary>Top-level public provider used to exercise LogProviderLoader discovery in tests.</summary>
    public class TestProdLogProvider : IProdLogProvider
    {
        public string Name => "Test provider";

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ProdTask>>(Array.Empty<ProdTask>());
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IProdLog>>(Array.Empty<IProdLog>());
        }
    }
}
