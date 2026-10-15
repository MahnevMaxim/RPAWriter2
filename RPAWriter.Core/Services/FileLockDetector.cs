using System;
using System.IO;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Detects whether a file is locked by another process (for example open in Word/Excel).
    /// </summary>
    public static class FileLockDetector
    {
        private const int ErrorSharingViolation = unchecked((int)0x80070020);
        private const int ErrorLockViolation = unchecked((int)0x80070021);

        /// <summary>Returns true when the file is currently held by another process.</summary>
        public static bool IsFileLocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                // FileShare.None fails whenever any other handle is open, which is exactly
                // the "file is occupied by another process" condition we want to detect.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }

                return false;
            }
            catch (IOException ex)
            {
                return IsLockViolation(ex);
            }
            catch (UnauthorizedAccessException)
            {
                return false; // a permission problem, not a lock
            }
        }

        /// <summary>Returns true when the exception (or any inner exception) is a file lock/sharing violation.</summary>
        public static bool IsFileLocked(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is IOException && IsLockViolation(e))
                    return true;
            }

            return false;
        }

        private static bool IsLockViolation(Exception ex)
        {
            if (ex.HResult == ErrorSharingViolation || ex.HResult == ErrorLockViolation)
                return true;

            var message = ex.Message ?? string.Empty;
            return message.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("sharing violation", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
