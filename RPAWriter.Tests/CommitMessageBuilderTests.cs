using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class CommitMessageBuilderTests
    {
        // ---------- SanitizeSummary / Format*Line ----------

        [Test]
        public void FormatRobotLine_StripsAiAddedTypeAndPrefix()
        {
            Assert.That(
                CommitMessageBuilder.FormatRobotLine("RPA-1111", "(fix)RPA-1111: add validation"),
                Is.EqualTo("RPA-1111: add validation"));
        }

        [Test]
        public void FormatRobotLine_StripsConventionalType()
        {
            Assert.That(
                CommitMessageBuilder.FormatRobotLine("RPA-1111", "fix: add validation"),
                Is.EqualTo("RPA-1111: add validation"));
        }

        [Test]
        public void FormatRobotLine_DoesNotDuplicatePrefix()
        {
            Assert.That(
                CommitMessageBuilder.FormatRobotLine("RPA-1111", "RPA-1111: add validation"),
                Is.EqualTo("RPA-1111: add validation"));
        }

        [Test]
        public void FormatProjectLine_StripsTypePrefix()
        {
            Assert.That(
                CommitMessageBuilder.FormatProjectLine("RPAWriter.Core", "fix: fix parser"),
                Is.EqualTo("RPAWriter.Core: fix parser"));
        }

        [Test]
        public void FormatChoreLine_PrefixesChoreOnce()
        {
            Assert.That(
                CommitMessageBuilder.FormatChoreLine("chore: tidy up"),
                Is.EqualTo("chore: tidy up"));
        }

        [Test]
        public void SanitizeSummary_StripsCodeFencesAndQuotes()
        {
            Assert.That(
                CommitMessageBuilder.SanitizeSummary("```\nadd validation\n```"),
                Is.EqualTo("add validation"));
            Assert.That(
                CommitMessageBuilder.SanitizeSummary("`RPA-1111: add validation`"),
                Is.EqualTo("add validation"));
            Assert.That(
                CommitMessageBuilder.SanitizeSummary("\"RPA-1111: add validation\""),
                Is.EqualTo("add validation"));
        }

        [Test]
        public void SanitizeSummary_LeavesPlainSummaryIntact()
        {
            Assert.That(CommitMessageBuilder.SanitizeSummary("add validation"), Is.EqualTo("add validation"));
        }

        // ---------- GroupChangedFiles ----------

        [Test]
        public void GroupChangedFiles_GroupsRobotSolutionAsOne()
        {
            var root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "RPA-1111", "RPA-1111", "Activities"));
                File.WriteAllText(Path.Combine(root, "RPA-1111", "RPA-1111.sln"), "");
                File.WriteAllText(Path.Combine(root, "RPA-1111", "RPA-1111", "RPA-1111.csproj"), "");
                File.WriteAllText(Path.Combine(root, "RPA-1111", "RPA-1111", "Activities", "Activities.csproj"), "");

                var plan = CommitMessageBuilder.GroupChangedFiles(root, new[]
                {
                    "RPA-1111/RPA-1111/Program.cs",
                    "RPA-1111/RPA-1111/Activities/Activity.cs",
                });

                Assert.That(plan.Robots.Count, Is.EqualTo(1));
                Assert.That(plan.Robots[0].Name, Is.EqualTo("RPA-1111"));
                Assert.That(plan.Robots[0].Files.Count, Is.EqualTo(2));
                Assert.That(plan.Projects.Count, Is.EqualTo(0));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void GroupChangedFiles_GroupsMultipleRobotsSeparately()
        {
            var root = CreateTempRoot();
            try
            {
                CreateRobot(root, "RPA-1111");
                CreateRobot(root, "RPA-2222");

                var plan = CommitMessageBuilder.GroupChangedFiles(root, new[]
                {
                    "RPA-1111/RPA-1111/A.cs",
                    "RPA-2222/RPA-2222/B.cs",
                });

                Assert.That(plan.Robots.Select(r => r.Name), Is.EquivalentTo(new[] { "RPA-1111", "RPA-2222" }));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void GroupChangedFiles_GroupsOrdinaryProjectsByName()
        {
            var root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "RPAWriter.Core", "Services"));
                Directory.CreateDirectory(Path.Combine(root, "RPAWriter2"));
                File.WriteAllText(Path.Combine(root, "RPAWriter.Core", "RPAWriter.Core.csproj"), "");
                File.WriteAllText(Path.Combine(root, "RPAWriter2", "RPAWriter2.csproj"), "");

                var plan = CommitMessageBuilder.GroupChangedFiles(root, new[]
                {
                    "RPAWriter.Core/Services/A.cs",
                    "RPAWriter2/B.cs",
                });

                Assert.That(plan.Projects.Select(p => p.Name), Is.EquivalentTo(new[] { "RPAWriter.Core", "RPAWriter2" }));
                Assert.That(plan.Robots.Count, Is.EqualTo(0));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void GroupChangedFiles_PutsRootFilesIntoOther()
        {
            var root = CreateTempRoot();
            try
            {
                var plan = CommitMessageBuilder.GroupChangedFiles(root, new[] { ".gitignore", "memory-bank/x.md" });

                Assert.That(plan.OtherFiles.Count, Is.EqualTo(2));
                Assert.That(plan.Robots.Count, Is.EqualTo(0));
                Assert.That(plan.Projects.Count, Is.EqualTo(0));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void GroupChangedFiles_HandlesRenameArrow()
        {
            var root = CreateTempRoot();
            try
            {
                CreateRobot(root, "RPA-1111");

                var plan = CommitMessageBuilder.GroupChangedFiles(root, new[]
                {
                    "RPA-1111/RPA-1111/Old.cs -> RPA-1111/RPA-1111/New.cs",
                });

                Assert.That(plan.Robots.Count, Is.EqualTo(1));
                Assert.That(plan.Robots[0].Files, Is.EquivalentTo(new[] { "RPA-1111/RPA-1111/New.cs" }));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void IsRobotFolderName_RequiresRpaPrefixAndSuffix()
        {
            Assert.That(CommitMessageBuilder.IsRobotFolderName("RPA-1111"), Is.True);
            Assert.That(CommitMessageBuilder.IsRobotFolderName("rpa-1"), Is.True);
            Assert.That(CommitMessageBuilder.IsRobotFolderName("RPA-"), Is.False);
            Assert.That(CommitMessageBuilder.IsRobotFolderName("RPAWriter.Core"), Is.False);
        }

        // ---------- helpers ----------

        private static void CreateRobot(string root, string name)
        {
            Directory.CreateDirectory(Path.Combine(root, name, name));
            File.WriteAllText(Path.Combine(root, name, name + ".sln"), "");
            File.WriteAllText(Path.Combine(root, name, name, name + ".csproj"), "");
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "rpawriter_commit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteTempRoot(string root)
        {
            try { Directory.Delete(root, true); }
            catch { /* best effort cleanup */ }
        }
    }
}
