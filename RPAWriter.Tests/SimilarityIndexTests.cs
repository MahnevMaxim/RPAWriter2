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

        [Test]
        public void FindSimilar_SapTasks_MatchByTransactionAndKindRegardlessOfText()
        {
            var records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "sap-setup",
                    Name = "SAP VA01 setup",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor
                        {
                            Action = "настроить и запустить",
                            Target = "транзакцию VA01",
                            Description = "Совершенно другой текст описания",
                            ActivityType = "SapSetup",
                            SapTransactionCode = "VA01",
                            SapActionKind = SapActionKind.Setup
                        }
                    }
                }
            };

            var query = new TaskDescriptor
            {
                Action = "подготовить",
                Target = "VA01",
                Description = "Полностью другой текст",
                ActivityType = "SomethingElse",
                SapTransactionCode = "va01",
                SapActionKind = SapActionKind.Setup
            };

            var result = _index.FindSimilar(query, records, 3);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Solution.Id, Is.EqualTo("sap-setup"));
            Assert.That(result[0].Score, Is.EqualTo(1.0));
            Assert.That(result[0].Reasons, Does.Contain("matched SAP transaction: VA01 (setup)"));
        }

        [Test]
        public void FindSimilar_SapTasks_DifferentTransaction_DoesNotMatch()
        {
            var records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "sap-setup",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { SapTransactionCode = "VA01", SapActionKind = SapActionKind.Setup }
                    }
                }
            };

            var query = new TaskDescriptor { SapTransactionCode = "MB51", SapActionKind = SapActionKind.Setup };

            var result = _index.FindSimilar(query, records, 3);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindSimilar_SapTasks_DifferentKind_DoesNotMatch()
        {
            var records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "sap-setup",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { SapTransactionCode = "VA01", SapActionKind = SapActionKind.Setup }
                    }
                }
            };

            var query = new TaskDescriptor { SapTransactionCode = "VA01", SapActionKind = SapActionKind.Work };

            var result = _index.FindSimilar(query, records, 3);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindSimilar_SapTask_DoesNotMatchNonSapTaskByText()
        {
            var records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "non-sap",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { Action = "настроить", Target = "VA01", ActivityType = "SapSetup" }
                    }
                }
            };

            var query = new TaskDescriptor
            {
                Action = "настроить",
                Target = "VA01",
                ActivityType = "SapSetup",
                SapTransactionCode = "VA01",
                SapActionKind = SapActionKind.Setup
            };

            var result = _index.FindSimilar(query, records, 3);

            Assert.That(result, Is.Empty);
        }
    }
}
