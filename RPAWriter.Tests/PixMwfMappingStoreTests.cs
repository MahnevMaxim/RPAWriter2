using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PixMwfMappingStoreTests
    {
        private static string NewTempPath() =>
            Path.Combine(Path.GetTempPath(), "RPAWriter2-test-map-" + Guid.NewGuid().ToString("N") + ".json");

        [Test]
        public void SaveAndLoad_RoundTrips()
        {
            var path = NewTempPath();
            try
            {
                var store = new PixMwfMappingStore(path);
                store.SetMapping("BR.Core.Base.Assign", "System.Activities.Statements.Assign");
                store.SetMapping("BR.Core.Base.Sleep", "System.Activities.Statements.Delay");
                store.Save();

                var reloaded = new PixMwfMappingStore(path);
                Assert.That(reloaded.GetMapping("BR.Core.Base.Assign"), Is.EqualTo("System.Activities.Statements.Assign"));
                Assert.That(reloaded.GetMapping("BR.Core.Base.Sleep"), Is.EqualTo("System.Activities.Statements.Delay"));
                Assert.That(reloaded.Count, Is.EqualTo(2));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SetMapping_EmptyValue_RemovesMapping()
        {
            var store = new PixMwfMappingStore(NewTempPath());
            store.SetMapping("BR.Core.Base.Assign", "System.Activities.Statements.Assign");
            Assert.That(store.Count, Is.EqualTo(1));

            store.SetMapping("BR.Core.Base.Assign", "");
            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.GetMapping("BR.Core.Base.Assign"), Is.Null);
        }
    }
}
