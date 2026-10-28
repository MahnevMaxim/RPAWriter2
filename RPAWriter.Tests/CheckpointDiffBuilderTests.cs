using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Checkpoints;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class CheckpointDiffBuilderTests
    {
        [Test]
        public void Build_EmptyDiffs_ReturnsEmptyList()
        {
            var files = CheckpointDiffBuilder.Build(new List<CheckpointDiff>());

            Assert.That(files, Is.Empty);
        }

        [Test]
        public void Build_SingleAddedLine_ProducesInsertedRow()
        {
            var diffs = new List<CheckpointDiff>
            {
                new CheckpointDiff { RelativePath = "a.txt", Before = "one", After = "one\ntwo" }
            };

            var files = CheckpointDiffBuilder.Build(diffs);

            Assert.That(files.Count, Is.EqualTo(1));
            var file = files[0];
            Assert.That(file.RelativePath, Is.EqualTo("a.txt"));
            Assert.That(file.AddedCount, Is.EqualTo(1));
            Assert.That(file.RemovedCount, Is.EqualTo(0));
            Assert.That(file.Rows, Has.Count.EqualTo(2));
            Assert.That(file.Rows[0].Kind, Is.EqualTo(DiffRowKind.Unchanged));
            Assert.That(file.Rows[1].Kind, Is.EqualTo(DiffRowKind.Inserted));
            Assert.That(file.Rows[1].RightText, Is.EqualTo("two"));
        }

        [Test]
        public void Build_SingleRemovedLine_ProducesDeletedRow()
        {
            var diffs = new List<CheckpointDiff>
            {
                new CheckpointDiff { RelativePath = "b.txt", Before = "one\ntwo", After = "one" }
            };

            var files = CheckpointDiffBuilder.Build(diffs);

            var file = files[0];
            Assert.That(file.AddedCount, Is.EqualTo(0));
            Assert.That(file.RemovedCount, Is.EqualTo(1));
            Assert.That(file.Rows, Has.Count.EqualTo(2));
            Assert.That(file.Rows[1].Kind, Is.EqualTo(DiffRowKind.Deleted));
            Assert.That(file.Rows[1].LeftText, Is.EqualTo("two"));
        }

        [Test]
        public void Build_ModifiedLine_CountsAsAddAndRemove()
        {
            var diffs = new List<CheckpointDiff>
            {
                new CheckpointDiff { RelativePath = "c.txt", Before = "old", After = "new" }
            };

            var files = CheckpointDiffBuilder.Build(diffs);

            var file = files[0];
            Assert.That(file.AddedCount, Is.EqualTo(1));
            Assert.That(file.RemovedCount, Is.EqualTo(1));
            Assert.That(file.Rows[0].Kind, Is.EqualTo(DiffRowKind.Modified));
            Assert.That(file.Rows[0].LeftText, Is.EqualTo("old"));
            Assert.That(file.Rows[0].RightText, Is.EqualTo("new"));
        }
    }
}
