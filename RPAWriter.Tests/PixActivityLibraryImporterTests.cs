using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PixActivityLibraryImporterTests
    {
        private static string FindPixInstallDirectory()
        {
            var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PIX");
            if (Directory.Exists(candidate))
                return candidate;

            candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PIX");
            return Directory.Exists(candidate) ? candidate : null;
        }

        [Test]
        public void Import_AgainstInstalledPix_WritesFullLibrary()
        {
            var pixDir = FindPixInstallDirectory();
            if (pixDir == null)
            {
                Assert.Ignore("PIX is not installed on this machine.");
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "RPAWriter2PixImport_" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = new PixActivityLibraryImporter().Import(pixDir, tempDir);

                Assert.That(result.DllsScanned, Is.GreaterThan(0));
                Assert.That(result.ActivitiesImported, Is.GreaterThan(0));
                Assert.That(result.FilesWritten, Is.EqualTo(result.ActivitiesImported));
                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.FailedAssemblies, Is.Empty);

                var library = new PixActivityLibrary(tempDir);
                Assert.That(library.GetKnownActivityNames().Count, Is.EqualTo(result.ActivitiesImported));

                // Structural activities keep their canonical converter mapping even when the target is empty.
                var sleep = library.Load("BR.Core.Base.Sleep");
                Assert.That(sleep, Is.Not.Null);
                Assert.That(sleep.WfSnippet, Is.EqualTo("Delay"));
                Assert.That(sleep.Parameters.Any(p => p.Pix == "Count" && p.Target == "Expression"), Is.True);
                Assert.That(sleep.DefaultDisplayName, Is.EqualTo("Ожидание"));
                Assert.That(sleep.Description, Is.EqualTo("Данная активность используется для установки времени бездействия робота, прежде чем он продолжит алгоритм или начнёт выполнять его заново."));
                var sleepCount = sleep.Parameters.First(p => p.Pix == "Count");
                Assert.That(sleepCount.DisplayName, Is.EqualTo("Время ожидания"));
                Assert.That(sleepCount.Description, Is.EqualTo("Длительность ожидания."));

                var ifDescriptor = library.Load("BR.Core.Base.If");
                Assert.That(ifDescriptor, Is.Not.Null);
                Assert.That(ifDescriptor.Role, Is.EqualTo("if"));
                Assert.That(ifDescriptor.Parameters.Any(p => p.Pix == "Condition" && p.Target == "Expression"), Is.True);
                Assert.That(ifDescriptor.DefaultDisplayName, Is.EqualTo("Условный оператор"));
                Assert.That(ifDescriptor.Description, Does.StartWith("Логика данной активности"));

                // A regular activity gets the safe InvokeMethod fallback and a parameter signature.
                var clickElement = library.Load("Activities.Web.ClickElement");
                Assert.That(clickElement, Is.Not.Null);
                Assert.That(clickElement.WfSnippet, Is.EqualTo("InvokeMethod"));
                Assert.That(clickElement.Parameters.Any(p => p.Pix == "Browser" && !string.IsNullOrEmpty(p.Type)), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void Import_MissingPixDirectory_ReportsError()
        {
            var result = new PixActivityLibraryImporter().Import(
                Path.Combine(Path.GetTempPath(), "RPAWriter2_MissingPix_" + Guid.NewGuid().ToString("N")),
                Path.Combine(Path.GetTempPath(), "RPAWriter2_Target_" + Guid.NewGuid().ToString("N")));

            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.ActivitiesImported, Is.EqualTo(0));
        }
    }
}
