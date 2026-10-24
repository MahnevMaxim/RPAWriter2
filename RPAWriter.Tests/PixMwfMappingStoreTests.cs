using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
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
        public void SaveAs_WritesToChosenPath_AndKeepsDefaultPath()
        {
            var defaultPath = NewTempPath();
            var asPath = NewTempPath();
            try
            {
                var store = new PixMwfMappingStore(defaultPath);
                store.SetMapping("BR.Core.Base.Assign", "System.Activities.Statements.Assign");
                store.SaveAs(asPath);

                Assert.That(File.Exists(asPath), Is.True);
                Assert.That(File.Exists(defaultPath), Is.False, "SaveAs must not touch the store's default path.");

                var reloaded = new PixMwfMappingStore(asPath);
                Assert.That(reloaded.GetMapping("BR.Core.Base.Assign"), Is.EqualTo("System.Activities.Statements.Assign"));
                Assert.That(store.FilePath, Is.EqualTo(defaultPath));
            }
            finally
            {
                if (File.Exists(defaultPath)) File.Delete(defaultPath);
                if (File.Exists(asPath)) File.Delete(asPath);
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

        [Test]
        public void SaveAndLoad_ArgumentMappings_RoundTrips()
        {
            var path = NewTempPath();
            try
            {
                var store = new PixMwfMappingStore(path);
                store.SetMapping("BR.Core.Base.Assign", "System.Activities.Statements.Assign");
                store.SetArgumentMapping("BR.Core.Base.Assign", "Value", "Value", ConversionKind.ToString);
                store.SetArgumentMapping("BR.Core.Base.Assign", "To", "To", ConversionKind.Custom, "({0}).Trim()");
                store.Save();

                var reloaded = new PixMwfMappingStore(path);
                var list = reloaded.GetArgumentMappings("BR.Core.Base.Assign");

                Assert.That(list.Count, Is.EqualTo(2));

                var value = list.FirstOrDefault(m => m.PixArgument == "Value");
                Assert.That(value, Is.Not.Null);
                Assert.That(value.MwfArgument, Is.EqualTo("Value"));
                Assert.That(value.Conversion, Is.EqualTo(ConversionKind.ToString));

                var to = list.FirstOrDefault(m => m.PixArgument == "To");
                Assert.That(to, Is.Not.Null);
                Assert.That(to.Conversion, Is.EqualTo(ConversionKind.Custom));
                Assert.That(to.ConversionExpression, Is.EqualTo("({0}).Trim()"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SetArgumentMapping_EmptyMwfArgument_RemovesMapping()
        {
            var store = new PixMwfMappingStore(NewTempPath());
            store.SetArgumentMapping("BR.Core.Base.Assign", "Value", "Value");
            Assert.That(store.GetArgumentMappings("BR.Core.Base.Assign").Count, Is.EqualTo(1));

            store.SetArgumentMapping("BR.Core.Base.Assign", "Value", "");
            Assert.That(store.GetArgumentMappings("BR.Core.Base.Assign").Count, Is.EqualTo(0));
        }
    }
}
