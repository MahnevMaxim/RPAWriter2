using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class WorkspaceResolverTests
    {
        [Test]
        public void Resolve_NothingOpen_ReturnsNull()
        {
            Assert.That(WorkspaceResolver.Resolve(false, null, null), Is.Null);
            Assert.That(WorkspaceResolver.Resolve(false, "", "   "), Is.Null);
        }

        [Test]
        public void Resolve_SolutionOpen_UsesSolutionDirectory()
        {
            var info = WorkspaceResolver.Resolve(true, @"C:\Solutions\Robot\Robot.sln", null);

            Assert.That(info, Is.Not.Null);
            Assert.That(info.Path, Is.EqualTo(@"C:\Solutions\Robot"));
            Assert.That(info.Name, Is.EqualTo("Robot"));
            Assert.That(info.Id, Is.EqualTo(WorkspaceIdentity.ComputeId(@"C:\Solutions\Robot")));
        }

        [Test]
        public void Resolve_SolutionWinsOverFolder()
        {
            var info = WorkspaceResolver.Resolve(true, @"C:\Solutions\Robot\Robot.sln", @"C:\SomeFolder");

            Assert.That(info, Is.Not.Null);
            Assert.That(info.Path, Is.EqualTo(@"C:\Solutions\Robot"));
        }

        [Test]
        public void Resolve_FolderOpen_UsesFolder()
        {
            var info = WorkspaceResolver.Resolve(false, null, @"C:\MyFolder");

            Assert.That(info, Is.Not.Null);
            Assert.That(info.Path, Is.EqualTo(@"C:\MyFolder"));
            Assert.That(info.Name, Is.EqualTo("MyFolder"));
            Assert.That(info.Id, Is.EqualTo(WorkspaceIdentity.ComputeId(@"C:\MyFolder")));
        }

        [Test]
        public void Resolve_FolderWithTrailingSeparator_Normalizes()
        {
            var with = WorkspaceResolver.Resolve(false, null, @"C:\MyFolder\");
            var without = WorkspaceResolver.Resolve(false, null, @"C:\MyFolder");

            Assert.That(with.Path, Is.EqualTo(without.Path));
            Assert.That(with.Name, Is.EqualTo("MyFolder"));
            Assert.That(with.Id, Is.EqualTo(without.Id));
        }

        [Test]
        public void Resolve_FolderNameKeepsExtension()
        {
            var info = WorkspaceResolver.Resolve(false, null, @"C:\Repo\My.Folder");

            Assert.That(info, Is.Not.Null);
            Assert.That(info.Name, Is.EqualTo("My.Folder"));
        }
    }
}
