using System;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Formats an exception and its whole chain of nested inner exceptions into a
    /// readable multi-line block for the chat: each exception is shown as its type and
    /// message, followed by its stack trace wrapped in a fenced (monospace) code block,
    /// then each inner exception in turn. It is a pure function of its input, with no
    /// UI or chat side effects.
    /// </summary>
    public static class ExceptionFormatter
    {
        /// <summary>
        /// Formats <paramref name="exception"/>, including every nested inner exception,
        /// into multi-line text: message, stack trace, then inner message and inner stack trace.
        /// </summary>
        public static string Format(Exception exception)
        {
            if (exception == null)
                return string.Empty;

            var sb = new StringBuilder();
            Append(sb, exception, isInner: false);
            return sb.ToString().TrimEnd();
        }

        private static void Append(StringBuilder sb, Exception exception, bool isInner)
        {
            if (exception == null)
                return;

            if (sb.Length > 0)
                sb.AppendLine();

            var typeName = string.IsNullOrWhiteSpace(exception.GetType().FullName)
                ? exception.GetType().Name
                : exception.GetType().FullName;

            // The exception itself (type + message), or the inner exception when recursing.
            sb.AppendLine((isInner ? "Inner error: " : string.Empty) + typeName + ": " + (exception.Message ?? string.Empty));
            AppendStackTrace(sb, exception, isInner ? "Inner stack trace:" : "Stack trace:");

            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    Append(sb, inner, isInner: true);
            }
            else if (exception.InnerException != null)
            {
                Append(sb, exception.InnerException, isInner: true);
            }
        }

        private static void AppendStackTrace(StringBuilder sb, Exception exception, string label)
        {
            var stackTrace = exception.StackTrace;
            if (string.IsNullOrWhiteSpace(stackTrace))
                return;

            // Wrap the stack trace in a fenced code block so it renders monospaced and
            // keeps its line structure in the chat.
            sb.AppendLine("```");
            sb.AppendLine(label);
            sb.AppendLine(stackTrace.TrimEnd());
            sb.AppendLine("```");
        }
    }
}
