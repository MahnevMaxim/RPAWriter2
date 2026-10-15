using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MarkdownAndDiffTests
    {
        [Test]
        public void MarkdownContent_TasksToMarkdown_BuildsTable()
        {
            var spec = new SpecModel
            {
                Name = "Test",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor
                    {
                        Action = "open",
                        Target = "site",
                        ActivityType = "OpenBrowser",
                        Keywords = new List<string> { "browser" }
                    }
                }
            };

            var md = MarkdownContent.TasksToMarkdown(spec);

            Assert.That(md, Does.Contain("# Tasks — Test"));
            Assert.That(md, Does.Contain("| # | Action | Target | Activity type | Keywords |"));
            Assert.That(md, Does.Contain("| open | site | OpenBrowser | browser |"));
        }

        [Test]
        public void TextDiff_DetectsChanges()
        {
            var oldText = "Line 1\nLine 2\nLine 3";
            var newText = "Line 1\nLine 2 changed\nLine 4";

            var diff = TextDiff.Diff(oldText, newText);

            Assert.That(diff.HasChanges, Is.True);
            Assert.That(diff.AddedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.RemovedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.Summary, Does.Contain("Discrepancies"));
        }

        [Test]
        public void TextDiff_NoChanges()
        {
            var diff = TextDiff.Diff("a\nb\nc", "a\nb\nc");

            Assert.That(diff.HasChanges, Is.False);
            Assert.That(diff.Summary, Does.Contain("No discrepancies found"));
        }
    }
}
