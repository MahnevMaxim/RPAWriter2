using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class FileKnowledgeBaseTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "rpawriter_kb_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); }
            catch { /* best effort cleanup */ }
        }

        // ---------- constructor ----------

        [Test]
        public void Ctor_NullOrWhitespaceDirectory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new FileKnowledgeBase(null));
            Assert.Throws<ArgumentNullException>(() => new FileKnowledgeBase("   "));
        }

        [Test]
        public void Ctor_CreatesDirectory()
        {
            new FileKnowledgeBase(_dir);

            Assert.That(Directory.Exists(_dir), Is.True);
        }

        // ---------- AddAsync ----------

        [Test]
        public async Task AddAsync_AssignsIdWhenEmpty()
        {
            var kb = new FileKnowledgeBase(_dir);
            var record = Record();

            await kb.AddAsync(record);

            Assert.That(record.Id, Is.Not.Empty);
            Assert.That(record.Id.Length, Is.EqualTo(32)); // Guid "N" format
        }

        [Test]
        public async Task AddAsync_KeepsProvidedId()
        {
            var kb = new FileKnowledgeBase(_dir);
            var record = Record("abc123");

            await kb.AddAsync(record);

            Assert.That(record.Id, Is.EqualTo("abc123"));
        }

        [Test]
        public async Task AddAsync_DeduplicatesById()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(Record("same-id", "First"));
            await kb.AddAsync(Record("same-id", "Second"));

            var all = await kb.GetAllAsync();

            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].Name, Is.EqualTo("Second"));
        }

        [Test]
        public void AddAsync_Cancelled_Throws()
        {
            var kb = new FileKnowledgeBase(_dir);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() => kb.AddAsync(Record(), cts.Token));
            }
        }

        // ---------- GetAllAsync ----------

        [Test]
        public async Task GetAllAsync_EmptyKb_ReturnsEmpty()
        {
            var kb = new FileKnowledgeBase(_dir);

            Assert.That(await kb.GetAllAsync(), Is.Empty);
        }

        [Test]
        public async Task GetAllAsync_ReturnsAllAdded()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(Record("1"));
            await kb.AddAsync(Record("2"));

            var all = await kb.GetAllAsync();

            Assert.That(all.Count, Is.EqualTo(2));
        }

        // ---------- RemoveWhereAsync ----------

        [Test]
        public async Task RemoveWhereAsync_RemovesMatchingAndReturnsCount()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(Record("1", "Keep"));
            await kb.AddAsync(Record("2", "Remove"));

            var removed = await kb.RemoveWhereAsync(r => r.Name == "Remove");

            Assert.That(removed, Is.EqualTo(1));
            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].Name, Is.EqualTo("Keep"));
        }

        [Test]
        public void RemoveWhereAsync_NullPredicate_Throws()
        {
            var kb = new FileKnowledgeBase(_dir);

            Assert.Throws<ArgumentNullException>(() => kb.RemoveWhereAsync(null));
        }

        // ---------- GetSolutionAsync ----------

        [Test]
        public async Task GetSolutionAsync_ReturnsByIdCaseInsensitive()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(Record("ABC", "Found"));

            var found = await kb.GetSolutionAsync("abc");

            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("Found"));
        }

        [Test]
        public async Task GetSolutionAsync_Missing_ReturnsNull()
        {
            var kb = new FileKnowledgeBase(_dir);

            Assert.That(await kb.GetSolutionAsync("nope"), Is.Null);
        }

        // ---------- FindSimilarTasksAsync ----------

        [Test]
        public async Task FindSimilarTasksAsync_EmptyKb_ReturnsEmpty()
        {
            var kb = new FileKnowledgeBase(_dir);

            var matches = await kb.FindSimilarTasksAsync(
                new TaskDescriptor { Action = "OpenBrowser" }, 5);

            Assert.That(matches, Is.Empty);
        }

        [Test]
        public async Task FindSimilarTasksAsync_ReturnsMatchingRecord()
        {
            var kb = new FileKnowledgeBase(_dir);
            var task = new TaskDescriptor
            {
                Action = "OpenBrowser",
                ActivityType = "UiBrowser",
                Description = "opens the browser window"
            };
            await kb.AddAsync(Record("1", "Browser solution", task));

            var matches = await kb.FindSimilarTasksAsync(
                new TaskDescriptor
                {
                    Action = "OpenBrowser",
                    ActivityType = "UiBrowser",
                    Description = "opens the browser window"
                }, 5);

            Assert.That(matches.Count, Is.GreaterThan(0));
            Assert.That(matches[0].Solution.Name, Is.EqualTo("Browser solution"));
        }

        // ---------- helpers ----------

        private static SolutionRecord Record(string id = null, string name = null, TaskDescriptor task = null)
        {
            return new SolutionRecord
            {
                Id = id ?? "",
                Name = name ?? "Test solution",
                Tasks = new List<TaskDescriptor>
                {
                    task ?? new TaskDescriptor { Action = "OpenBrowser", Description = "opens the browser" }
                }
            };
        }
    }
}
