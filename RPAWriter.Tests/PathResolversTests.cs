using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PathResolversTests
    {
        // ---------- folder-name constants ----------

        [Test]
        public void ActivityLibraryPaths_ExposesExpectedFolderNames()
        {
            Assert.That(ActivityLibraryPaths.ActivityLibraryFolderName, Is.EqualTo("ActivityLibrary"));
            Assert.That(ActivityLibraryPaths.WfSnippetsFolderName, Is.EqualTo("wf"));
            Assert.That(ActivityLibraryPaths.PixActivitiesFolderName, Is.EqualTo("pix"));
            Assert.That(ActivityLibraryPaths.MwfActivitiesFolderName, Is.EqualTo("mwf"));
        }

        [Test]
        public void PromptPaths_ExposesExpectedFolderNames()
        {
            Assert.That(PromptPaths.PromptsFolderName, Is.EqualTo("Prompts"));
            Assert.That(PromptPaths.RulesFolderName, Is.EqualTo("Rules"));
        }

        [Test]
        public void HelpDocumentation_ExposesExpectedFolderName()
        {
            Assert.That(HelpDocumentation.DocsFolderName, Is.EqualTo("Docs"));
        }

        // ---------- resolved path shape ----------

        [Test]
        public void ResolveWfSnippetsDirectory_EndsWithActivityLibraryWf()
        {
            Assert.That(
                ActivityLibraryPaths.ResolveWfSnippetsDirectory(),
                Does.EndWith(Path.Combine("ActivityLibrary", "wf")));
        }

        [Test]
        public void ResolvePixActivitiesDirectory_EndsWithActivityLibraryPix()
        {
            Assert.That(
                ActivityLibraryPaths.ResolvePixActivitiesDirectory(),
                Does.EndWith(Path.Combine("ActivityLibrary", "pix")));
        }

        [Test]
        public void ResolveMwfActivitiesDirectory_EndsWithActivityLibraryMwf()
        {
            Assert.That(
                ActivityLibraryPaths.ResolveMwfActivitiesDirectory(),
                Does.EndWith(Path.Combine("ActivityLibrary", "mwf")));
        }

        [Test]
        public void ResolveRulesDirectory_EndsWithPromptsRules()
        {
            Assert.That(
                PromptPaths.ResolveRulesDirectory(),
                Does.EndWith(Path.Combine("Prompts", "Rules")));
        }

        // ---------- smoke tests (non-empty, no throw) ----------

        [Test]
        public void ResolveActivityLibraryDirectory_ReturnsNonEmptyPath()
        {
            Assert.That(ActivityLibraryPaths.ResolveActivityLibraryDirectory(), Is.Not.Empty);
        }

        [Test]
        public void ResolvePromptsDirectory_ReturnsNonEmptyPath()
        {
            Assert.That(PromptPaths.ResolvePromptsDirectory(), Is.Not.Empty);
        }

        [Test]
        public void ResolveDocsDirectory_ReturnsNonEmptyPath()
        {
            Assert.That(HelpDocumentation.ResolveDocsDirectory(), Is.Not.Empty);
        }

        [Test]
        public void HelpDocumentationReadAll_ReturnsStringWithoutThrowing()
        {
            // Empty when no docs directory is deployed next to the test assembly, content otherwise.
            Assert.That(HelpDocumentation.ReadAll(), Is.Not.Null);
        }
    }
}
