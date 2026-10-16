using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class KnowledgeAndTemplateTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public async Task FileKnowledgeBase_AddAndFindSimilar_RoundTrip()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(new SolutionRecord
            {
                Name = "Open site",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor { Action = "open", Target = "site", ActivityType = "OpenBrowser", Keywords = new List<string>{ "browser" } }
                },
                XamlFragment = "<InvokeMethod MethodName=\"OpenBrowser\" />"
            });

            var query = new TaskDescriptor { Action = "open", Target = "site", ActivityType = "OpenBrowser" };
            var matches = await kb.FindSimilarTasksAsync(query, 5);

            Assert.That(matches.Count, Is.EqualTo(1));
            Assert.That(matches[0].Solution.Name, Is.EqualTo("Open site"));

            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task FileKnowledgeBase_RemoveWhere_RemovesMatchingRecords()
        {
            var kb = new FileKnowledgeBase(_dir);
            await kb.AddAsync(new SolutionRecord { Name = "A", SourceRobot = "RPA-0001" });
            await kb.AddAsync(new SolutionRecord { Name = "B", SourceRobot = "RPA-0002" });
            await kb.AddAsync(new SolutionRecord { Name = "C" });

            var removed = await kb.RemoveWhereAsync(r => !string.IsNullOrWhiteSpace(r.SourceRobot));

            Assert.That(removed, Is.EqualTo(2));
            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(1));
            Assert.That(all[0].Name, Is.EqualTo("C"));
        }

        [Test]
        public void SolutionTemplateEngine_ReplacesPlaceholders()
        {
            var template = new SolutionRecord
            {
                XamlFragment = "Open {{url}} with selector {{selector}}",
                CSharpCode = "Open(\"{{url}}\")"
            };

            var result = new SolutionTemplateEngine().Apply(template, new Dictionary<string, string>
            {
                { "url", "http://new.example.com" },
                { "selector", "#btn" }
            });

            Assert.That(result.XamlFragment, Does.Contain("http://new.example.com"));
            Assert.That(result.XamlFragment, Does.Contain("#btn"));
            Assert.That(result.CSharpCode, Does.Contain("http://new.example.com"));
            Assert.That(result.XamlFragment, Does.Not.Contain("{{url}}"));
        }
    }
}
