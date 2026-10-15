using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class FileLockDetectorTests
    {
        private sealed class LockedIOException : IOException
        {
            public LockedIOException() : base("locked") { HResult = unchecked((int)0x80070020); }
        }

        [Test]
        public void IsFileLocked_ReturnsTrueWhenFileIsHeldByAnotherProcess()
        {
            var path = Path.Combine(Path.GetTempPath(), "rpawriter_lock_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "data");
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.That(FileLockDetector.IsFileLocked(path), Is.True);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void IsFileLocked_ReturnsFalseWhenFileIsFree()
        {
            var path = Path.Combine(Path.GetTempPath(), "rpawriter_lock_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "data");
            try
            {
                Assert.That(FileLockDetector.IsFileLocked(path), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void IsFileLocked_ReturnsFalseForMissingFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "rpawriter_missing_" + Guid.NewGuid().ToString("N") + ".txt");
            Assert.That(FileLockDetector.IsFileLocked(path), Is.False);
        }

        [Test]
        public void IsFileLockedException_DetectsSharingViolationHResult()
        {
            Assert.That(FileLockDetector.IsFileLocked(new LockedIOException()), Is.True);
        }

        [Test]
        public void IsFileLockedException_DetectsLockMessage()
        {
            var ex = new IOException("The process cannot access the file 'spec.docx' because it is being used by another process.");
            Assert.That(FileLockDetector.IsFileLocked(ex), Is.True);
        }

        [Test]
        public void IsFileLockedException_DetectsNestedLockMessage()
        {
            var inner = new IOException("The process cannot access the file because it is being used by another process.");
            Assert.That(FileLockDetector.IsFileLocked(new Exception("outer", inner)), Is.True);
        }

        [Test]
        public void IsFileLockedException_ReturnsFalseForUnrelatedExceptions()
        {
            Assert.That(FileLockDetector.IsFileLocked(new IOException("I/O error")), Is.False);
            Assert.That(FileLockDetector.IsFileLocked(new InvalidOperationException("boom")), Is.False);
        }
    }
}
