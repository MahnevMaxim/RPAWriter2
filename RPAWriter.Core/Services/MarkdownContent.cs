using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Markdown builder helpers (tables, task list).</summary>
    public static class MarkdownContent
    {
        /// <summary>Escapes a Markdown table cell value.</summary>
        public static string EscapeCell(string value)
        {
            if (value == null) return "";
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "\\|");
        }

        /// <summary>Builds a Markdown table; the first row is treated as the header.</summary>
        public static string Table(IEnumerable<IReadOnlyList<string>> rows)
        {
            var list = rows?.ToList() ?? new List<IReadOnlyList<string>>();
            if (list.Count == 0) return "";
            int columnCount = list.Max(r => r?.Count ?? 0);
            if (columnCount == 0) return "";

            var sb = new StringBuilder();
            for (int r = 0; r < list.Count; r++)
            {
                var row = list[r];
                sb.Append("| ");
                for (int c = 0; c < columnCount; c++)
                {
                    var cell = row != null && c < row.Count ? row[c] : "";
                    sb.Append(EscapeCell(cell));
                    if (c < columnCount - 1) sb.Append(" | ");
                }
                sb.AppendLine(" |");
                if (r == 0)
                {
                    sb.Append("|");
                    for (int c = 0; c < columnCount; c++) sb.Append(" --- |");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Builds the tasks.md content from a spec's task list.</summary>
        public static string TasksToMarkdown(SpecModel spec)
        {
            var name = spec?.Name ?? "Spec";
            var sb = new StringBuilder();
            sb.AppendLine("# Tasks — " + name);
            sb.AppendLine();

            var rows = new List<IReadOnlyList<string>>
            {
                new List<string> { "#", "Action", "Target", "Activity type", "Keywords" }
            };

            var tasks = spec?.Tasks ?? new List<TaskDescriptor>();
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                rows.Add(new List<string>
                {
                    (i + 1).ToString(),
                    t?.Action ?? "",
                    t?.Target ?? "",
                    t?.ActivityType ?? "",
                    string.Join(", ", t?.Keywords ?? new List<string>())
                });
            }

            sb.Append(Table(rows));
            return sb.ToString();
        }

        /// <summary>Formats an absolute timestamp for human-readable headers (ISO-like UTC).</summary>
        public static string FormatTimestamp(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
        }

        /// <summary>Builds a robot spec.md from a reconstructed specification.</summary>
        public static string SpecToMarkdown(SpecModel spec, DateTime generatedUtc)
        {
            var name = string.IsNullOrWhiteSpace(spec?.Name) ? "Robot" : spec.Name;
            var sb = new StringBuilder();
            sb.AppendLine("# " + name + " — Technical specification");
            sb.AppendLine();
            sb.AppendLine("Generated: " + FormatTimestamp(generatedUtc));
            sb.AppendLine("Source robot: " + name);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(spec?.Description))
            {
                sb.AppendLine("## Description");
                sb.AppendLine();
                sb.AppendLine(spec.Description);
                sb.AppendLine();
            }

            AppendList(sb, "## Steps", spec?.Steps);
            AppendList(sb, "## Inputs", spec?.Inputs);
            AppendList(sb, "## Outputs", spec?.Outputs);

            return sb.ToString();
        }

        /// <summary>Builds a robot tasks.md with the solution file(s) for each task.</summary>
        public static string RobotTasksToMarkdown(string robotName, IReadOnlyList<RobotTaskEntry> tasks, DateTime generatedUtc)
        {
            var name = string.IsNullOrWhiteSpace(robotName) ? "Robot" : robotName;
            var sb = new StringBuilder();
            sb.AppendLine("# Tasks — " + name);
            sb.AppendLine();
            sb.AppendLine("Generated: " + FormatTimestamp(generatedUtc));
            sb.AppendLine("Source robot: " + name);
            sb.AppendLine();

            var rows = new List<IReadOnlyList<string>>
            {
                new List<string> { "#", "Task", "Description", "Activity type", "Solution file(s)" }
            };

            var list = tasks ?? new List<RobotTaskEntry>();
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var task = entry?.Task;
                rows.Add(new List<string>
                {
                    (i + 1).ToString(),
                    (task?.Action ?? "") + (string.IsNullOrWhiteSpace(task?.Target) ? "" : " → " + task.Target),
                    task?.Description ?? "",
                    task?.ActivityType ?? "",
                    string.Join(", ", entry?.SolutionPaths ?? new List<string>())
                });
            }

            sb.Append(Table(rows));
            return sb.ToString();
        }

        private static void AppendList(StringBuilder sb, string heading, List<string> items)
        {
            var list = items ?? new List<string>();
            if (list.Count == 0) return;
            sb.AppendLine(heading);
            sb.AppendLine();
            foreach (var item in list)
                sb.AppendLine("- " + (item ?? ""));
            sb.AppendLine();
        }
    }
}
