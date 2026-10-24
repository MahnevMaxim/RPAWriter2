using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PixActivityLibraryTests
    {
        private static string NewTempDir() =>
            Path.Combine(Path.GetTempPath(), "RPAWriter2-test-pixlib-" + Guid.NewGuid().ToString("N"));

        [Test]
        public void EnsureDescriptor_MakesActivityKnown_AndLoadable()
        {
            var dir = NewTempDir();
            try
            {
                var library = new PixActivityLibrary(dir);
                Assert.That(library.GetKnownActivityNames(), Is.Empty);

                library.EnsureDescriptor(new PixActivityDescriptor
                {
                    ActivityFullName = "Vendor.Robots.SomeActivity",
                    DefaultDisplayName = "Some activity",
                    Role = "statement"
                });

                Assert.That(library.GetKnownActivityNames(), Does.Contain("Vendor.Robots.SomeActivity"));

                var loaded = library.Load("Vendor.Robots.SomeActivity");
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.ActivityFullName, Is.EqualTo("Vendor.Robots.SomeActivity"));
                Assert.That(loaded.DefaultDisplayName, Is.EqualTo("Some activity"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void EnsureDescriptor_NullOrNameless_IsNoOp()
        {
            var dir = NewTempDir();
            try
            {
                var library = new PixActivityLibrary(dir);
                library.EnsureDescriptor(null);
                library.EnsureDescriptor(new PixActivityDescriptor { ActivityFullName = "  " });
                Assert.That(library.GetKnownActivityNames(), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
