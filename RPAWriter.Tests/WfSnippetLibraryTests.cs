using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class WfSnippetLibraryTests
    {
        private static string FindActivityLibrary()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "RPAWriter2", "ActivityLibrary");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("ActivityLibrary not found above " + System.AppContext.BaseDirectory);
        }

        [Test]
        public void GetAll_ReturnsDeployedSnippetsWithContent()
        {
            var root = FindActivityLibrary();
            var snippets = new WfSnippetLibrary(Path.Combine(root, "wf")).GetAll();

            Assert.That(snippets.Count, Is.GreaterThanOrEqualTo(1));

            var assign = snippets.FirstOrDefault(s => s.Name == "Assign");
            Assert.That(assign, Is.Not.Null);
            Assert.That(assign.Content, Does.Contain("Assign"));
        }
    }
}
