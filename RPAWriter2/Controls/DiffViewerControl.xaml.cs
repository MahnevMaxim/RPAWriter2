using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RPAWriter.Core.Checkpoints;

namespace RPAWriter2.Controls
{
    /// <summary>A single line in the inline (unified) diff view.</summary>
    public sealed class InlineDiffRow
    {
        public string Prefix { get; set; } = "";

        public string Text { get; set; } = "";

        public DiffRowKind Kind { get; set; }
    }

    /// <summary>
    /// Graphical diff viewer: a changed-file list plus a side-by-side/inline diff pane.
    /// Filled via <see cref="SetDiff"/>; keeps no agent/domain logic.
    /// </summary>
    public partial class DiffViewerControl : UserControl
    {
        private readonly List<DiffFileModel> _files = new List<DiffFileModel>();

        public DiffViewerControl()
        {
            InitializeComponent();
        }

        /// <summary>Shows the given files (or the empty state when none changed).</summary>
        public void SetDiff(IReadOnlyList<DiffFileModel> files, string title)
        {
            _files.Clear();
            if (files != null)
                _files.AddRange(files);

            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Diff" : title;

            int totalAdded = _files.Sum(f => f.AddedCount);
            int totalRemoved = _files.Sum(f => f.RemovedCount);
            SummaryText.Text = _files.Count == 0
                ? ""
                : _files.Count + " file(s) · +" + totalAdded + " -" + totalRemoved;

            FilesList.ItemsSource = null;
            FilesList.ItemsSource = _files;

            if (_files.Count == 0)
            {
                ShowEmpty();
                return;
            }

            FilesList.SelectedIndex = 0;
        }

        private void ShowEmpty()
        {
            EmptyText.Visibility = Visibility.Visible;
            SideBySideHeader.Visibility = Visibility.Collapsed;
            SideBySideScroll.Visibility = Visibility.Collapsed;
            InlineScroll.Visibility = Visibility.Collapsed;
        }

        private void OnFileSelected(object sender, SelectionChangedEventArgs e)
        {
            if (FilesList.SelectedItem is not DiffFileModel file)
            {
                ShowEmpty();
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;
            SideBySideList.ItemsSource = file.Rows;
            InlineList.ItemsSource = BuildInlineRows(file.Rows);
            ApplyViewMode();
        }

        private void OnViewModeChanged(object sender, RoutedEventArgs e)
        {
            // The "Checked" event also fires while InitializeComponent builds the
            // first RadioButton; guard against not-yet-initialized named elements.
            if (InlineToggle == null || SideBySideScroll == null || InlineScroll == null)
                return;

            ApplyViewMode();
        }

        private void ApplyViewMode()
        {
            bool inline = InlineToggle.IsChecked == true;
            SideBySideHeader.Visibility = inline ? Visibility.Collapsed : Visibility.Visible;
            SideBySideScroll.Visibility = inline ? Visibility.Collapsed : Visibility.Visible;
            InlineScroll.Visibility = inline ? Visibility.Visible : Visibility.Collapsed;
        }

        private static IReadOnlyList<InlineDiffRow> BuildInlineRows(IReadOnlyList<DiffRowModel> rows)
        {
            var list = new List<InlineDiffRow>();
            if (rows == null)
                return list;

            foreach (var row in rows)
            {
                switch (row.Kind)
                {
                    case DiffRowKind.Deleted:
                        list.Add(new InlineDiffRow { Prefix = "-", Text = row.LeftText, Kind = DiffRowKind.Deleted });
                        break;
                    case DiffRowKind.Inserted:
                        list.Add(new InlineDiffRow { Prefix = "+", Text = row.RightText, Kind = DiffRowKind.Inserted });
                        break;
                    case DiffRowKind.Modified:
                        list.Add(new InlineDiffRow { Prefix = "-", Text = row.LeftText, Kind = DiffRowKind.Deleted });
                        list.Add(new InlineDiffRow { Prefix = "+", Text = row.RightText, Kind = DiffRowKind.Inserted });
                        break;
                    default:
                        list.Add(new InlineDiffRow { Prefix = " ", Text = row.LeftText ?? row.RightText, Kind = DiffRowKind.Unchanged });
                        break;
                }
            }

            return list;
        }
    }
}
