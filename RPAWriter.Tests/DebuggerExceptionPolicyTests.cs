using System;
using NUnit.Framework;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// The debugger-exception watcher's decision rules must be deterministic and fail closed:
    /// auto-send fires only for unhandled exceptions and never for first-chance exceptions.
    /// </summary>
    [TestFixture]
    public class DebuggerExceptionPolicyTests
    {
        [Test]
        public void ShouldCapture_CapturesOnlyExceptionReasons()
        {
            Assert.That(DebuggerExceptionPolicy.ShouldCapture(DebuggerBreakReason.ExceptionThrown), Is.True);
            Assert.That(DebuggerExceptionPolicy.ShouldCapture(DebuggerBreakReason.ExceptionNotHandled), Is.True);
            Assert.That(DebuggerExceptionPolicy.ShouldCapture(DebuggerBreakReason.None), Is.False);
        }

        [Test]
        public void ShouldAutoSend_RequiresUnhandledAndEnabled()
        {
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.ExceptionNotHandled, true), Is.True);
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.ExceptionNotHandled, false), Is.False);
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.ExceptionThrown, true), Is.False);
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.ExceptionThrown, false), Is.False);
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.None, true), Is.False);
        }

        [Test]
        public void ShouldAutoSend_FirstChanceExceptionNeverAutoSends()
        {
            // Regression: a caught exception must never flood the chat, even when auto-capture is on.
            Assert.That(DebuggerExceptionPolicy.ShouldAutoSend(DebuggerBreakReason.ExceptionThrown, true), Is.False);
        }

        [Test]
        public void ReadAutoCaptureEnabledSafely_ReturnsReaderValue()
        {
            Assert.That(DebuggerExceptionPolicy.ReadAutoCaptureEnabledSafely(() => true), Is.True);
            Assert.That(DebuggerExceptionPolicy.ReadAutoCaptureEnabledSafely(() => false), Is.False);
        }

        [Test]
        public void ReadAutoCaptureEnabledSafely_FailsClosedOnException()
        {
            var result = DebuggerExceptionPolicy.ReadAutoCaptureEnabledSafely(
                () => throw new InvalidOperationException("settings read failed"));

            Assert.That(result, Is.False);
        }
    }
}
