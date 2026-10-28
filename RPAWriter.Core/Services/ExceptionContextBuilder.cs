using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Deterministically assembles the prompt sent to the agent for an exception captured
    /// from the Visual Studio debugger. It is a pure function of its input: the whole
    /// context is built in a single pass and returned as one string, with no UI or chat
    /// side effects, so nothing reaches the chat until it has fully completed.
    /// </summary>
    public static class ExceptionContextBuilder
    {
        /// <summary>Instruction prepended to every debugger-exception message.</summary>
        public const string AnalysisPrompt =
            "Отладчик Visual Studio остановился на исключении. " +
            "Проанализируй его, определи наиболее вероятную причину, " +
            "найди место в коде, где оно возникает, и предложи конкретное исправление. " +
            "Отвечай по-русски.";

        /// <summary>Builds the prompt from a captured <see cref="DebuggerExceptionSnapshot"/>.</summary>
        public static string Build(DebuggerExceptionSnapshot snapshot)
        {
            if (snapshot == null)
                return Build(string.Empty);

            return Build(snapshot.ExceptionDump, snapshot.TypeName, snapshot.BreakReason, snapshot.Location);
        }

        /// <summary>
        /// Builds the prompt from the raw exception dump plus optional context strings.
        /// <paramref name="exceptionDump"/> is expected to already include the whole chain of
        /// nested inner exceptions (the standard <c>Exception.ToString()</c> output).
        /// </summary>
        public static string Build(string exceptionDump, string typeName = "", string breakReason = "", string location = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine(AnalysisPrompt);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(breakReason))
                sb.AppendLine("Причина остановки отладчика: " + breakReason);
            if (!string.IsNullOrWhiteSpace(typeName))
                sb.AppendLine("Тип исключения: " + typeName);
            if (!string.IsNullOrWhiteSpace(location))
                sb.AppendLine("Место остановки: " + location);

            sb.AppendLine();
            sb.AppendLine("Полный дамп исключения (включая все вложенные InnerException):");
            sb.AppendLine("```");
            sb.AppendLine(string.IsNullOrEmpty(exceptionDump) ? "(no exception dump available)" : exceptionDump);
            sb.AppendLine("```");

            return sb.ToString().TrimEnd();
        }
    }
}
