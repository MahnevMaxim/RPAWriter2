using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MwfActivityLibraryImporterTests
    {
        [Test]
        public void Import_ScansBuiltInSystemActivitiesDll()
        {
            var frameworkDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET", "Framework64", "v4.0.30319");
            var systemActivities = Path.Combine(frameworkDir, "System.Activities.dll");
            if (!File.Exists(systemActivities))
                Assert.Ignore("System.Activities.dll not available on this machine.");

            var src = Path.Combine(Path.GetTempPath(), "RPAWriter2_MwfSrc_" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(Path.GetTempPath(), "RPAWriter2_MwfTarget_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(src);
            try
            {
                // The importer still requires at least one Activities.*.dll in the source folder;
                // copying the framework assembly under such a name satisfies that while keeping the
                // test focused on verifying that System.Activities.dll itself is also scanned.
                File.Copy(systemActivities, Path.Combine(src, "Activities.Framework.dll"));

                var result = new MwfActivityLibraryImporter().Import(src, target);

                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.FailedAssemblies, Is.Empty);
                Assert.That(result.DllsScanned, Is.EqualTo(2));
                Assert.That(result.ActivitiesImported, Is.GreaterThan(0));
                Assert.That(File.Exists(Path.Combine(target, "System.Activities.Statements.Sequence.json")), Is.True);
            }
            finally
            {
                if (Directory.Exists(src)) Directory.Delete(src, true);
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }

        [Test]
        public void Import_MissingActivitiesDirectory_ReportsError()
        {
            var result = new MwfActivityLibraryImporter().Import(
                Path.Combine(Path.GetTempPath(), "RPAWriter2_MissingMwf_" + Guid.NewGuid().ToString("N")),
                Path.Combine(Path.GetTempPath(), "RPAWriter2_MwfTarget_" + Guid.NewGuid().ToString("N")));

            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.ActivitiesImported, Is.EqualTo(0));
        }

        [Test]
        public void Load_And_GetKnownActivityNames_WorkFromDescriptorDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RPAWriter2Mwf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(
                    Path.Combine(tempDir, "Activities.Custom.SendEmail.json"),
                    "{\n" +
                    "  \"activityFullName\": \"Activities.Custom.SendEmail\",\n" +
                    "  \"defaultDisplayName\": \"Send Email\",\n" +
                    "  \"description\": \"Отправляет письмо.\",\n" +
                    "  \"baseType\": \"System.Activities.AsyncCodeActivity\",\n" +
                    "  \"assemblyName\": \"Activities.Custom.dll\",\n" +
                    "  \"parameters\": [ { \"name\": \"To\", \"type\": \"System.String\" } ]\n" +
                    "}\n");

                var library = new MwfActivityLibrary(tempDir);
                Assert.That(library.GetKnownActivityNames().Contains("Activities.Custom.SendEmail"), Is.True);

                var descriptor = library.Load("Activities.Custom.SendEmail");
                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.DefaultDisplayName, Is.EqualTo("Send Email"));
                Assert.That(descriptor.Description, Is.EqualTo("Отправляет письмо."));
                Assert.That(descriptor.Parameters.Count, Is.EqualTo(1));
                Assert.That(descriptor.Parameters[0].Name, Is.EqualTo("To"));
                Assert.That(descriptor.Parameters[0].Type, Is.EqualTo("System.String"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
