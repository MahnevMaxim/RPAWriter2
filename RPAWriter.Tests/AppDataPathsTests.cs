using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class AppDataPathsTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetVersionScope();
        }

        // ---------- InitializeScope ----------

        [Test]
        public void InitializeScope_ParsesMajorVersion()
        {
            AppDataPaths.InitializeScope("17.13.2");
            Assert.That(AppDataPaths.VersionScope, Is.EqualTo("17"));
        }

        [Test]
        public void InitializeScope_AcceptsVersionWithoutSuffix()
        {
            AppDataPaths.InitializeScope("18");
            Assert.That(AppDataPaths.VersionScope, Is.EqualTo("18"));
        }

        [Test]
        public void InitializeScope_InvalidInput_SetsEmpty()
        {
            AppDataPaths.InitializeScope("abc");
            Assert.That(AppDataPaths.VersionScope, Is.Empty);
        }

        [Test]
        public void InitializeScope_ZeroMajor_SetsEmpty()
        {
            AppDataPaths.InitializeScope("0.1");
            Assert.That(AppDataPaths.VersionScope, Is.Empty);
        }

        [Test]
        public void InitializeScope_NullOrEmpty_SetsEmpty()
        {
            AppDataPaths.InitializeScope(null);
            Assert.That(AppDataPaths.VersionScope, Is.Empty);

            AppDataPaths.InitializeScope("   ");
            Assert.That(AppDataPaths.VersionScope, Is.Empty);
        }

        [Test]
        public void InitializeScope_IsIdempotent()
        {
            AppDataPaths.InitializeScope("17.0.0");
            AppDataPaths.InitializeScope("18.0.0");

            Assert.That(AppDataPaths.VersionScope, Is.EqualTo("17"));
        }

        // ---------- path building ----------

        [Test]
        public void ScopedRoot_WithoutScope_EqualsRoot()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2");

            Assert.That(AppDataPaths.Root, Is.EqualTo(root));
            Assert.That(AppDataPaths.ScopedRoot, Is.EqualTo(root));
        }

        [Test]
        public void ScopedRoot_WithScope_UsesVersionFolder()
        {
            AppDataPaths.InitializeScope("17.13.2");

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2", "17");

            Assert.That(AppDataPaths.ScopedRoot, Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_BuildsPathUnderScopedRoot()
        {
            AppDataPaths.InitializeScope("17.0");

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2", "17", "a", "b");

            Assert.That(AppDataPaths.Resolve("a", "b"), Is.EqualTo(expected));
        }

        [Test]
        public void LocalScopedRoot_UsesLocalAppData()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RPAWriter2");

            Assert.That(AppDataPaths.LocalRoot, Is.EqualTo(expected));
            Assert.That(AppDataPaths.LocalScopedRoot, Is.EqualTo(expected));
        }

        [Test]
        public void TempScopedRoot_UsesTempPath()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "RPAWriter2");
            Assert.That(AppDataPaths.TempScopedRoot, Is.EqualTo(tempRoot));

            AppDataPaths.InitializeScope("18.0");
            Assert.That(AppDataPaths.TempScopedRoot, Is.EqualTo(Path.Combine(tempRoot, "18")));
        }

        // ---------- migration ----------

        [Test]
        public void MigrateLegacyData_EmptyScope_ReturnsWithoutThrowing()
        {
            Assert.DoesNotThrow(() => AppDataPaths.MigrateLegacyData());
            Assert.That(AppDataPaths.VersionScope, Is.Empty);
        }

        // ---------- helpers ----------

        private static void ResetVersionScope()
        {
            var field = typeof(AppDataPaths).GetField(
                "<VersionScope>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, "");
        }
    }
}
