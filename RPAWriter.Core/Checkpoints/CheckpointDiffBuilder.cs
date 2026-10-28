using System.Collections.Generic;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Kind of a single diff row, driving how the diff viewer highlights the line.</summary>
    public enum DiffRowKind
    {
        Unchanged,
        Inserted,
        Deleted,
        Modified
    }

    /// <summary>One aligned diff row: left (before) and right (after) line with their line numbers.</summary>
    public sealed class DiffRowModel
    {
        public DiffRowKind Kind { get; set; }

        public int? LeftNumber { get; set; }

        public int? RightNumber { get; set; }

        public string LeftText { get; set; } = "";

        public string RightText { get; set; } = "";
    }

    /// <summary>Diff of one file, ready to be shown by the diff viewer.</summary>
    public sealed class DiffFileModel
    {
        public string RelativePath { get; set; } = "";

        public IReadOnlyList<DiffRowModel> Rows { get; set; } = new List<DiffRowModel>();

        public int AddedCount { get; set; }

        public int RemovedCount { get; set; }

        /// <summary>Compact "+N -M" summary shown next to the file name.</summary>
        public string ChangeSummary => "+" + AddedCount + " -" + RemovedCount;
    }

    /// <summary>
    /// Converts raw checkpoint diffs (<see cref="CheckpointDiff"/>) into side-by-side row models
    /// using the DiffPlex diff engine. Keeps the WPF layer free of DiffPlex types and unit-testable.
    /// </summary>
    public static class CheckpointDiffBuilder
    {
        public static IReadOnlyList<DiffFileModel> Build(IReadOnlyList<CheckpointDiff> diffs)
        {
            if (diffs == null || diffs.Count == 0)
                return new List<DiffFileModel>();

            var files = new List<DiffFileModel>(diffs.Count);
            foreach (var diff in diffs)
            {
                var rows = BuildRows(diff.Before ?? "", diff.After ?? "");

                int added = 0;
                int removed = 0;
                foreach (var row in rows)
                {
                    switch (row.Kind)
                    {
                        case DiffRowKind.Inserted:
                            added++;
                            break;
                        case DiffRowKind.Deleted:
                            removed++;
                            break;
                        case DiffRowKind.Modified:
                            added++;
                            removed++;
                            break;
                    }
                }

                files.Add(new DiffFileModel
                {
                    RelativePath = diff.RelativePath ?? "",
                    Rows = rows,
                    AddedCount = added,
                    RemovedCount = removed,
                });
            }

            return files;
        }

        private static IReadOnlyList<DiffRowModel> BuildRows(string before, string after)
        {
            // Whitespace is not ignored so whitespace-only edits remain visible.
            var model = new SideBySideDiffBuilder().BuildDiffModel(before, after, ignoreWhitespace: false);

            var oldLines = model.OldText.Lines;
            var newLines = model.NewText.Lines;

            var rows = new List<DiffRowModel>(oldLines.Count);
            for (int i = 0; i < oldLines.Count; i++)
            {
                var oldPiece = oldLines[i];
                var newPiece = newLines[i];

                rows.Add(new DiffRowModel
                {
                    Kind = MapKind(oldPiece.Type, newPiece.Type),
                    LeftNumber = oldPiece.Position,
                    RightNumber = newPiece.Position,
                    LeftText = oldPiece.Text ?? "",
                    RightText = newPiece.Text ?? "",
                });
            }

            return rows;
        }

        private static DiffRowKind MapKind(ChangeType oldType, ChangeType newType)
        {
            // DiffPlex pads the shorter side with imaginary lines so both panes line up 1:1.
            if (oldType == ChangeType.Imaginary) return DiffRowKind.Inserted;
            if (newType == ChangeType.Imaginary) return DiffRowKind.Deleted;
            if (oldType == ChangeType.Modified || newType == ChangeType.Modified) return DiffRowKind.Modified;
            return DiffRowKind.Unchanged;
        }
    }
}
