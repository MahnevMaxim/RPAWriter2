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
