using System;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2
{
    /// <summary>
    /// Watches the Visual Studio debugger for break-mode entries caused by an exception,
    /// captures the exception object (including the whole nested InnerException chain) through
    /// the debugger's <c>$exception</c> pseudo-variable. The captured exception is exposed to
    /// the context-menu command and, when enabled, is automatically sent to the agent chat.
    /// </summary>
    internal sealed class DebuggerExceptionWatcher
    {
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(2);

        private readonly AsyncPackage _package;
        private DTE2? _dte;
        private DebuggerEvents? _debuggerEvents;
        private string _lastCaptureKey = string.Empty;
        private DateTime _lastCaptureUtc = DateTime.MinValue;

        private DebuggerExceptionWatcher(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>Gets the singleton instance.</summary>
        public static DebuggerExceptionWatcher Instance { get; private set; }

        /// <summary>The most recently captured exception, until it is sent or dismissed.</summary>
        public DebuggerExceptionSnapshot? Pending { get; private set; }

        /// <summary>Initializes the singleton and subscribes to the debugger events.</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            Instance = new DebuggerExceptionWatcher(package);
            Instance._dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;

            // Keep the COM events object in a field so the GC does not collect it.
            Instance._debuggerEvents = Instance._dte?.Events?.DebuggerEvents;
            if (Instance._debuggerEvents != null)
                Instance._debuggerEvents.OnEnterBreakMode += Instance.OnEnterBreakMode;
        }

        /// <summary>Drops the pending exception (after it has been sent or dismissed).</summary>
        public void ClearPending()
        {
            Pending = null;
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            if (reason != dbgEventReason.dbgEventReasonExceptionThrown &&
                reason != dbgEventReason.dbgEventReasonExceptionNotHandled)
                return;

            _package.JoinableTaskFactory.RunAsync(async delegate
            {
                await CaptureAsync(reason);
            });
        }

        private async Task CaptureAsync(dbgEventReason reason)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var debugger = _dte?.Debugger;
            if (debugger == null || debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return;

            // $exception is only available while the debugger is stopped on the exception.
            var dump = ReadExpression(debugger, "$exception.ToString()");
            var type = ReadExpression(debugger, "$exception.GetType().FullName");
            var message = ReadExpression(debugger, "$exception.Message");
            var location = ReadLocation(debugger);

            if (string.IsNullOrWhiteSpace(dump) &&
                string.IsNullOrWhiteSpace(type) &&
                string.IsNullOrWhiteSpace(message))
                return; // Nothing to capture (e.g. native code without $exception).

            // Do not re-capture the same exception within a short window.
            var key = (type ?? string.Empty) + "\u0000" + (message ?? string.Empty) + "\u0000" + (dump ?? string.Empty);
            var now = DateTime.UtcNow;
            if (key == _lastCaptureKey && (now - _lastCaptureUtc) < DedupeWindow)
                return;
            _lastCaptureKey = key;
            _lastCaptureUtc = now;

            var snapshot = new DebuggerExceptionSnapshot
            {
                ExceptionDump = dump ?? string.Empty,
                TypeName = type ?? string.Empty,
                Message = message ?? string.Empty,
                BreakReason = reason.ToString(),
                Location = location ?? string.Empty
            };

            Pending = snapshot;

            if (IsAutoSendEnabled())
            {
                await SendToChatAsync(snapshot);
                Pending = null;
            }
        }

        private static bool IsAutoSendEnabled()
        {
            try
            {
                return SettingsStore.Load().AutoCaptureRuntimeExceptions;
            }
            catch
            {
                return true;
            }
        }

        private async Task SendToChatAsync(DebuggerExceptionSnapshot snapshot)
        {
            var window = await _package.ShowToolWindowAsync(typeof(ToolWindow1), 0, true, _package.DisposalToken) as ToolWindow1;
            var control = window?.Control;
            if (control == null)
                return;

            await control.SubmitErrorToChatAsync(ExceptionContextBuilder.Build(snapshot));
        }

        private static string ReadExpression(Debugger debugger, string expression)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var result = debugger.GetExpression(expression, true, 3000);
                if (result != null && result.IsValidValue && !string.IsNullOrWhiteSpace(result.Value))
                    return result.Value;
            }
            catch
            {
                // Expression evaluation can fail for native/optimized code; ignore.
            }

            return string.Empty;
        }

        private static string ReadLocation(Debugger debugger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var frame = debugger.CurrentStackFrame;
                if (frame == null)
                    return string.Empty;

                var name = frame.FunctionName ?? string.Empty;
                var language = frame.Language ?? string.Empty;
                return string.IsNullOrWhiteSpace(language) ? name : name + " [" + language + "]";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
