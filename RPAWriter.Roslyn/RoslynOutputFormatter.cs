using System.Collections.Generic;
using System.Text;

namespace RPAWriter.Roslyn
{
    /// <summary>
    /// Renders Roslyn analysis DTOs into the compact text handed back to the model.
    /// The formatter is intentionally dumb and deterministic so it can be unit-tested
    /// without a workspace.
    /// </summary>
    public static class RoslynOutputFormatter
    {
        public static string FormatOutline(CodeOutlineEntry root, string filePath)
        {
            var sb = new StringBuilder();
            sb.Append("Outline: ").AppendLine(string.IsNullOrWhiteSpace(filePath) ? root?.Name : filePath);

            if (root != null)
            {
                foreach (var child in root.Children)
                    AppendOutlineEntry(child, sb, 1);
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendOutlineEntry(CodeOutlineEntry entry, StringBuilder sb, int depth)
        {
            if (entry == null)
                return;

            var indent = new string(' ', (depth - 1) * 2);
            sb.Append(indent).Append(entry.Signature ?? entry.Name ?? string.Empty);
            if (entry.StartLine > 0)
            {
                sb.Append("  (lines ").Append(entry.StartLine);
                if (entry.EndLine > entry.StartLine)
                    sb.Append('-').Append(entry.EndLine);
                sb.Append(')');
            }
            sb.AppendLine();

            foreach (var child in entry.Children)
                AppendOutlineEntry(child, sb, depth + 1);
        }

        public static string FormatUsages(SymbolUsageResult result)
        {
            var sb = new StringBuilder();
            sb.Append("Symbol: ").AppendLine(result.SymbolDisplay ?? string.Empty);

            var usages = result.Usages ?? new List<SymbolUsage>();
            foreach (var usage in usages)
            {
                sb.Append("  ")
                  .Append(usage.FilePath ?? "<file>")
                  .Append(':').Append(usage.Line).Append(':').Append(usage.Column)
                  .Append("  ").AppendLine(usage.CodeLine ?? string.Empty);
            }

            sb.Append('(').Append(usages.Count).Append(" references");
            if (result.Truncated)
                sb.Append(", truncated at ").Append(result.MaxResults);
            sb.AppendLine(")");

            return sb.ToString().TrimEnd();
        }

        public static string FormatInheritance(InheritanceInfo info)
        {
            var sb = new StringBuilder();
            sb.Append("Type: ").Append(info.Symbol ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(info.Kind))
                sb.Append(" (").Append(info.Kind).Append(')');
            sb.AppendLine();

            if (info.BaseTypes != null && info.BaseTypes.Count > 0)
                sb.AppendLine("  Base: " + string.Join(" -> ", info.BaseTypes));

            if (info.Interfaces != null && info.Interfaces.Count > 0)
                sb.AppendLine("  Interfaces: " + string.Join(", ", info.Interfaces));

            if (info.DerivedTypes != null && info.DerivedTypes.Count > 0)
                sb.AppendLine("  Derived (" + info.DerivedTypes.Count + "): " + string.Join(", ", info.DerivedTypes));

            if (info.Implementations != null && info.Implementations.Count > 0)
                sb.AppendLine("  Implementations (" + info.Implementations.Count + "): " + string.Join(", ", info.Implementations));

            return sb.ToString().TrimEnd();
        }

        public static string FormatDiagnostics(string filePath, IReadOnlyList<CodeDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(filePath ?? string.Empty).AppendLine("]");

            var items = diagnostics ?? new List<CodeDiagnostic>();
            foreach (var diagnostic in items)
            {
                sb.Append('[').Append(diagnostic.Id ?? string.Empty).Append("] ")
                  .Append(diagnostic.Severity ?? string.Empty).Append(": ")
                  .Append(diagnostic.Message ?? string.Empty)
                  .Append(" (line ").Append(diagnostic.Line);
                if (diagnostic.Column > 0)
                    sb.Append(", col ").Append(diagnostic.Column);
                sb.AppendLine(")");
            }

            sb.Append('(').Append(items.Count).AppendLine(" diagnostics)");
            return sb.ToString().TrimEnd();
        }
    }
}
