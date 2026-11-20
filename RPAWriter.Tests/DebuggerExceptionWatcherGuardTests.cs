using System.IO;
using NUnit.Framework;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Regression guard for the debugger-exception watcher. The watcher itself lives in the net472
    /// VSIX and cannot be unit-tested directly, so this guards the wiring: the watcher must delegate
    /// its decisions to <see cref="Agent.Rpa.Services.DebuggerExceptionPolicy"/> (covered by
    /// <see cref="DebuggerExceptionPolicyTests"/>).
    /// </summary>
    [TestFixture]
    public class DebuggerExceptionWatcherGuardTests
    {
        [Test]
        public void Watcher_DelegatesDecisionsToPolicy()
        {
            var code = File.ReadAllText(Path.Combine(RepoRoot, "RPAWriter2", "DebuggerExceptionWatcher.cs"));

            Assert.That(code, Does.Contain("DebuggerExceptionPolicy.ShouldCapture"),
                "The capture filter must be delegated to DebuggerExceptionPolicy.");
            Assert.That(code, Does.Contain("DebuggerExceptionPolicy.ShouldAutoSend"),
                "The auto-send decision must be delegated to DebuggerExceptionPolicy.");
            Assert.That(code, Does.Contain("DebuggerExceptionPolicy.ReadAutoCaptureEnabledSafely"),
                "The settings read must fail closed via DebuggerExceptionPolicy.ReadAutoCaptureEnabledSafely.");
        }

        private static string RepoRoot => FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "RPAWriter2")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "RPAWriter.Tests")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the RPAWriter2 repository root.");
        }
    }
}