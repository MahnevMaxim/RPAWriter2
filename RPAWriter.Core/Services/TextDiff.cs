using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPAWriter.Core.Services
{
    public enum DiffLineKind { Equal, Add, Remove }

    /// <summary>A single diff line.</summary>
    public class DiffLine
    {
        public DiffLineKind Kind { get; set; }
        public int OldNumber { get; set; }
        public int NewNumber { get; set; }
        public string Text { get; set; }
    }

    /// <summary>Result of comparing two texts.</summary>
    public class TextDiffResult
    {
        public List<DiffLine> Lines { get; set; } = new List<DiffLine>();
        public bool HasChanges => Lines.Any(l => l.Kind != DiffLineKind.Equal);
        public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Add);
        public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Remove);
        public string Summary { get; set; }
    }

    /// <summary>Line-level diff for detecting discrepancies between md files and a fresh parse.</summary>
    public static class TextDiff
    {
        public static TextDiffResult Diff(string oldText, string newText)
        {
            var oldLines = SplitLines(oldText);
            var newLines = SplitLines(newText);
            var result = Compute(oldLines, newLines);
            result.Summary = BuildSummary(result);
            return result;
        }

        private static TextDiffResult Compute(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if ((long)a.Count * b.Count > 8_000_000L)
                return ComputeByMultiset(a, b);

            int n = a.Count, m = b.Count;
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    dp[i, j] = a[i] == b[j]
                        ? dp[i + 1, j + 1] + 1
                        : Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            var result = new TextDiffResult();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y])
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Equal, OldNumber = x + 1, NewNumber = y + 1, Text = a[x] });
                    x++; y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Remove, OldNumber = x + 1, Text = a[x] });
                    x++;
                }
                else
                {
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Add, NewNumber = y + 1, Text = b[y] });
                    y++;
                }
            }
            while (x < n)
            {
                result.Lines.Add(new DiffLine { Kind = DiffLineKind.Remove, OldNumber = x + 1, Text = a[x] });
                x++;
            }
            while (y < m)
            {
                result.Lines.Add(new DiffLine { Kind = DiffLineKind.Add, NewNumber = y + 1, Text = b[y] });
                y++;
            }
            return result;
        }

        private static TextDiffResult ComputeByMultiset(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            var result = new TextDiffResult();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in a)
                counts[line] = counts.TryGetValue(line, out var c) ? c + 1 : 1;

            foreach (var line in b)
            {
                if (counts.TryGetValue(line, out var c) && c > 0)
                    counts[line] = c - 1;
                else
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Add, Text = line });
            }

            foreach (var kv in counts)
                for (int i = 0; i < kv.Value; i++)
                    result.Lines.Add(new DiffLine { Kind = DiffLineKind.Remove, Text = kv.Key });

            return result;
        }

        private static string BuildSummary(TextDiffResult result)
        {
            var sb = new StringBuilder();
            if (!result.HasChanges)
            {
                sb.AppendLine("No discrepancies found — file is identical.");
                return sb.ToString();
            }

            sb.AppendLine("Discrepancies: " + (result.AddedCount + result.RemovedCount) +
                          " (added " + result.AddedCount + ", removed " + result.RemovedCount + ").");

            const int maxPreview = 40;
            int shown = 0;
            foreach (var line in result.Lines)
            {
                if (line.Kind == DiffLineKind.Equal) continue;
                if (shown >= maxPreview)
                {
                    sb.AppendLine("… (remaining lines omitted)");
                    break;
                }
                sb.AppendLine((line.Kind == DiffLineKind.Add ? "+ " : "- ") + (line.Text ?? ""));
                shown++;
            }
            return sb.ToString();
        }

        private static List<string> SplitLines(string text)
        {
            if (text == null) return new List<string>();
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        }
    }
}
