using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SimilarityIndexTests
    {
        private SimilarityIndex _index;
        private List<SolutionRecord> _records;

        [SetUp]
        public void SetUp()
        {
            _index = new SimilarityIndex();
            _records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "1",
                    Name = "Open site and read table",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { Action = "open", Target = "browser page", ActivityType = "OpenBrowser", Keywords = new List<string>{ "browser", "page" } },
                        new TaskDescriptor { Action = "read", Target = "table", ActivityType = "ReadTable", Keywords = new List<string>{ "table", "data" } }
                    }
                },
                new SolutionRecord
                {
                    Id = "2",
                    Name = "Fill in the form",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { Action = "fill", Target = "form", ActivityType = "FillField", Keywords = new List<string>{ "form", "field" } }
                    }
                }
            };
        }

        [Test]
        public void FindSimilar_ReturnsMatchingRecord()
        {
            var query = new TaskDescriptor
            {
                Action = "read",
                Target = "table",
                ActivityType = "ReadTable",
                Keywords = new List<string> { "table", "data" }
            };

            var result = _index.FindSimilar(query, _records, 3);

            Assert.That(result.Count, Is.GreaterThan(0));
            Assert.That(result[0].Solution.Id, Is.EqualTo("1"));
            Assert.That(result[0].Score, Is.GreaterThan(0));
            Assert.That(result[0].Reasons, Is.Not.Empty);
        }

        [Test]
        public void FindSimilar_RespectsTopK()
        {
            var query = new TaskDescriptor { Action = "run", Target = "something", ActivityType = "Custom" };

            var result = _index.FindSimilar(query, _records, 1);

            Assert.That(result.Count, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void FindSimilar_NoRecords_ReturnsEmpty()
        {
            var query = new TaskDescriptor { Action = "open" };
            var result = _index.FindSimilar(query, new List<SolutionRecord>(), 3);
            Assert.That(result, Is.Empty);
        }
    }
}
