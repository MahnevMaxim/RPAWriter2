using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class WorkspaceIdentityTests
    {
        [Test]
        public void ComputeId_EmptyOrWhitespace_ReturnsEmpty()
        {
            Assert.That(WorkspaceIdentity.ComputeId(null), Is.Empty);
            Assert.That(WorkspaceIdentity.ComputeId(""), Is.Empty);
            Assert.That(WorkspaceIdentity.ComputeId("   "), Is.Empty);
        }

        [Test]
        public void ComputeId_IsDeterministic()
        {
            var first = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot");
            var second = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot");

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void ComputeId_IsLowercaseHexSha1()
        {
            var id = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot");

            Assert.That(id, Is.Not.Empty);
            Assert.That(id, Does.Match("^[0-9a-f]{40}$"));
        }

        [Test]
        public void ComputeId_IgnoresTrailingSeparatorAndCasing()
        {
            var withSeparator = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot\");
            var withoutSeparator = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot");
            var lower = WorkspaceIdentity.ComputeId(@"c:\solutions\robot");

            Assert.That(withSeparator, Is.EqualTo(withoutSeparator));
            Assert.That(lower, Is.EqualTo(withoutSeparator));
        }

        [Test]
        public void ComputeId_IgnoresSlashStyle()
        {
            var backslashes = WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot");
            var forwardSlashes = WorkspaceIdentity.ComputeId("C:/Solutions/Robot");

            Assert.That(forwardSlashes, Is.EqualTo(backslashes));
        }

        [Test]
        public void ComputeId_DistinctPaths_DistinctIds()
        {
            var first = WorkspaceIdentity.ComputeId(@"C:\Solutions\RobotA");
            var second = WorkspaceIdentity.ComputeId(@"C:\Solutions\RobotB");

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void WorkspaceInfo_DefaultsAreEmpty()
        {
            var info = new WorkspaceInfo();

            Assert.That(info.Id, Is.Empty);
            Assert.That(info.Path, Is.Empty);
            Assert.That(info.Name, Is.Empty);
        }
    }
}
