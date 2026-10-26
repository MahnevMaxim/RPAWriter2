using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SystemActivitiesStoreTests
    {
        [Test]
        public void SaveDiscovery_FirstDiscovery_SelectsAll()
        {
            var path = TempPath();
            try
            {
                var store = new SystemActivitiesStore(path);
                store.SaveDiscovery(new[]
                {
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Sequence" },
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Assign" }
                });

                var names = store.GetSelectedNames();
                Assert.That(names.Count, Is.EqualTo(2));
                Assert.That(names.Contains("System.Activities.Statements.Sequence"), Is.True);
                Assert.That(names.Contains("System.Activities.Statements.Assign"), Is.True);
            }
            finally
            {
                Delete(path);
            }
        }

        [Test]
        public void SaveFilter_PersistsSelectionAcrossInstances()
        {
            var path = TempPath();
            try
            {
                var store = new SystemActivitiesStore(path);
                store.SaveDiscovery(new[]
                {
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Sequence" },
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Assign" }
                });
                store.SaveFilter(new[] { "System.Activities.Statements.Sequence" });

                var reloaded = new SystemActivitiesStore(path);
                Assert.That(reloaded.GetSelectedNames().Count, Is.EqualTo(1));
                Assert.That(reloaded.GetSelectedNames().Contains("System.Activities.Statements.Sequence"), Is.True);
                Assert.That(reloaded.GetActivities().Count, Is.EqualTo(2));
            }
            finally
            {
                Delete(path);
            }
        }

        [Test]
        public void SaveDiscovery_PreservesSelectionForStillExistingActivities()
        {
            var path = TempPath();
            try
            {
                var store = new SystemActivitiesStore(path);
                store.SaveDiscovery(new[]
                {
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Sequence" },
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Assign" }
                });
                store.SaveFilter(new[] { "System.Activities.Statements.Assign" });

                // Rediscovery drops "Sequence" and adds "Delay"; the still-existing "Assign" selection survives.
                store.SaveDiscovery(new[]
                {
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Assign" },
                    new WfActivityDescriptor { ActivityFullName = "System.Activities.Statements.Delay" }
                });

                var names = store.GetSelectedNames();
                Assert.That(names.Count, Is.EqualTo(1));
                Assert.That(names.Contains("System.Activities.Statements.Assign"), Is.True);
            }
            finally
            {
                Delete(path);
            }
        }

        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "RPAWriter2_SystemActivities_" + Guid.NewGuid().ToString("N") + ".json");

        private static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
