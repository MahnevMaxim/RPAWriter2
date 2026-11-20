using System;

namespace Agent.Rpa.Services
{
    /// <summary>
    /// Reason for the debugger break that triggered the exception watcher. Kept independent of the
    /// EnvDTE <c>dbgEventReason</c> enum so the decision rules stay unit-testable without Visual Studio.
    /// </summary>
    public enum DebuggerBreakReason
    {
        /// <summary>Not an exception break (breakpoint, step, pause, etc.).</summary>
        None = 0,

        /// <summary>A first-chance (caught) exception was thrown.</summary>
        ExceptionThrown = 1,

        /// <summary>An unhandled exception escaped to the process boundary.</summary>
        ExceptionNotHandled = 2
    }

    /// <summary>
    /// Pure decision rules for <c>DebuggerExceptionWatcher</c>. They are the single source of truth
    /// for which breaks are captured and which are auto-sent, and are fully unit-testable.
    /// </summary>
    public static class DebuggerExceptionPolicy
    {
        /// <summary>Captures only exceptions, ignoring ordinary breakpoints, steps and pauses.</summary>
        public static bool ShouldCapture(DebuggerBreakReason reason)
            => reason == DebuggerBreakReason.ExceptionThrown ||
               reason == DebuggerBreakReason.ExceptionNotHandled;

        /// <summary>
        /// Auto-sends only unhandled exceptions. First-chance (caught) exceptions are captured for the
        /// manual "send exception" command but must never auto-send — doing so would flood the chat.
        /// </summary>
        public static bool ShouldAutoSend(DebuggerBreakReason reason, bool autoCaptureEnabled)
            => reason == DebuggerBreakReason.ExceptionNotHandled && autoCaptureEnabled;

        /// <summary>
        /// Reads the auto-capture flag and fails closed: a settings read error disables auto-send
        /// instead of enabling it.
        /// </summary>
        public static bool ReadAutoCaptureEnabledSafely(Func<bool> reader)
        {
            try
            {
                return reader();
            }
            catch
            {
                return false;
            }
        }
    }
}
