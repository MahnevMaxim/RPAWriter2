using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Arranges children into vertical columns that are anchored to the bottom and
    /// filled from the bottom up. When a column runs out of vertical space, the
    /// overflow is taken from the top of the column and placed in a new column to
    /// the right, aligned with the bottom of the previous column.
    /// </summary>
    /// <remarks>
    /// This is the mirror of <see cref="WrapPanel"/> with a vertical orientation:
    /// instead of the bottom child overflowing to the top of the next column, the
    /// top child overflows to the bottom of the next column.
    /// </remarks>
    public sealed class TopOverflowWrapPanel : Panel
    {
        public static readonly DependencyProperty VerticalSpacingProperty =
            DependencyProperty.Register(
                nameof(VerticalSpacing),
                typeof(double),
                typeof(TopOverflowWrapPanel),
                new FrameworkPropertyMetadata(
                    0d,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
                    null,
                    CoerceSpacing));

        public static readonly DependencyProperty ColumnSpacingProperty =
            DependencyProperty.Register(
                nameof(ColumnSpacing),
                typeof(double),
                typeof(TopOverflowWrapPanel),
                new FrameworkPropertyMetadata(
                    0d,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
                    null,
                    CoerceSpacing));

        /// <summary>Gets or sets the vertical gap between children within a column.</summary>
        public double VerticalSpacing
        {
            get => (double)GetValue(VerticalSpacingProperty);
            set => SetValue(VerticalSpacingProperty, value);
        }

        /// <summary>Gets or sets the horizontal gap between columns.</summary>
        public double ColumnSpacing
        {
            get => (double)GetValue(ColumnSpacingProperty);
            set => SetValue(ColumnSpacingProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0)
                return default;

            MeasureChildren(availableSize);
            var columns = BuildColumns(NormalizeHeight(availableSize.Height));
            return ComputeDesiredSize(columns);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0)
                return finalSize;

            var columns = BuildColumns(NormalizeHeight(finalSize.Height));

            double x = 0;
            foreach (var column in columns)
            {
                // Columns are anchored to the bottom of the panel.
                double y = finalSize.Height;
                foreach (int index in column)
                {
                    UIElement child = Children[index];
                    child.Arrange(new Rect(x, y - child.DesiredSize.Height, child.DesiredSize.Width, child.DesiredSize.Height));
                    y -= child.DesiredSize.Height + VerticalSpacing;
                }

                x += GetColumnWidth(column) + ColumnSpacing;
            }

            return finalSize;
        }

        private void MeasureChildren(Size availableSize)
        {
            var constraint = new Size(availableSize.Width, double.PositiveInfinity);
            foreach (UIElement child in Children)
                child.Measure(constraint);
        }

        private List<List<int>> BuildColumns(double availableHeight)
        {
            var columns = new List<List<int>>();
            var current = new List<int>();
            columns.Add(current);

            double usedHeight = 0;
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                double height = Children[i].DesiredSize.Height;
                double additional = current.Count == 0 ? height : VerticalSpacing + height;

                if (current.Count > 0 && usedHeight + additional > availableHeight)
                {
                    current = new List<int>();
                    columns.Add(current);
                    usedHeight = 0;
                    additional = height;
                }

                current.Add(i);
                usedHeight += additional;
            }

            return columns;
        }

        private Size ComputeDesiredSize(List<List<int>> columns)
        {
            double totalWidth = 0;
            double totalHeight = 0;

            for (int i = 0; i < columns.Count; i++)
            {
                double columnHeight = 0;
                double columnWidth = 0;

                for (int j = 0; j < columns[i].Count; j++)
                {
                    UIElement child = Children[columns[i][j]];
                    columnHeight += child.DesiredSize.Height;
                    if (j > 0)
                        columnHeight += VerticalSpacing;

                    if (child.DesiredSize.Width > columnWidth)
                        columnWidth = child.DesiredSize.Width;
                }

                if (columnHeight > totalHeight)
                    totalHeight = columnHeight;

                totalWidth += columnWidth;
                if (i < columns.Count - 1)
                    totalWidth += ColumnSpacing;
            }

            return new Size(totalWidth, totalHeight);
        }

        private double GetColumnWidth(List<int> column)
        {
            double width = 0;
            foreach (int index in column)
            {
                if (Children[index].DesiredSize.Width > width)
                    width = Children[index].DesiredSize.Width;
            }

            return width;
        }

        private static double NormalizeHeight(double height)
        {
            return double.IsNaN(height) || double.IsInfinity(height) || height <= 0
                ? double.PositiveInfinity
                : height;
        }

        private static object CoerceSpacing(DependencyObject d, object baseValue)
        {
            double value = baseValue is double boxed ? boxed : 0d;
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 ? value : 0d;
        }
    }
}

