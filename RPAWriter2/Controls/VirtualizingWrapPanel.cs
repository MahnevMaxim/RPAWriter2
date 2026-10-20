using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// A <see cref="VirtualizingPanel"/> that lays its children out in a wrapping
    /// grid (like <see cref="WrapPanel"/>) while realizing only the items that are
    /// currently visible. The Controls -> SVG gallery binds thousands of icons, so a
    /// plain <see cref="WrapPanel"/> would materialize every one of them and block the
    /// UI thread; this panel keeps that cost proportional to the visible area instead.
    /// </summary>
    /// <remarks>
    /// The panel scrolls vertically in pixel space and requires
    /// <c>ScrollViewer.CanContentScroll="True"</c> on the hosting <see cref="ScrollViewer"/>
    /// (which <see cref="ListBox"/> already defaults to). Each child is arranged at a fixed
    /// <see cref="ChildSize"/> so the scroll extent can be computed without realizing items.
    /// </remarks>
    public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        /// <summary>Fixed layout slot of every realized child; must match the card dimensions declared in XAML.</summary>
        public static readonly DependencyProperty ChildSizeProperty =
            DependencyProperty.Register(
                nameof(ChildSize),
                typeof(Size),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(
                    new Size(112, 108),
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        /// <summary>Gets or sets the fixed layout slot (width, height) of each realized child.</summary>
        public Size ChildSize
        {
            get => (Size)GetValue(ChildSizeProperty);
            set => SetValue(ChildSizeProperty, value);
        }

        // The scroll offset is applied via a render transform so realized children do not
        // have to be re-arranged on every scroll tick.
        private readonly TranslateTransform _trans = new TranslateTransform();
        private ScrollViewer? _owner;
        private bool _canHScroll;
        private bool _canVScroll;
        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset;
        private int _columns = 1;

        /// <summary>Initializes a new instance of the <see cref="VirtualizingWrapPanel"/> class.</summary>
        public VirtualizingWrapPanel()
        {
            RenderTransform = _trans;
        }

        /// <inheritdoc />
        protected override Size MeasureOverride(Size availableSize)
        {
            UpdateScrollInfo(availableSize);

            if (ChildSize.Width <= 0 || ChildSize.Height <= 0)
                return availableSize;

            GetVisibleRange(out int firstVisibleItemIndex, out int lastVisibleItemIndex);

            UIElementCollection children = InternalChildren;
            IItemContainerGenerator generator = ItemContainerGenerator;

            // Map the first visible data index to a generator position, then realize only
            // the range [firstVisibleItemIndex, lastVisibleItemIndex].
            GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstVisibleItemIndex);
            int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstVisibleItemIndex; itemIndex <= lastVisibleItemIndex; itemIndex++, childIndex++)
                {
                    if (!(generator.GenerateNext(out bool newlyRealized) is UIElement child))
                        continue;

                    if (newlyRealized)
                    {
                        if (childIndex >= children.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);

                        generator.PrepareItemContainer(child);
                    }
                    else if (!ReferenceEquals(child, children[childIndex]))
                    {
                        throw new InvalidOperationException("VirtualizingWrapPanel received an unexpected container.");
                    }

                    child.Measure(ChildSize);
                }
            }

            CleanUpItems(firstVisibleItemIndex, lastVisibleItemIndex);

            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        /// <inheritdoc />
        protected override Size ArrangeOverride(Size finalSize)
        {
            UpdateScrollInfo(finalSize);

            IItemContainerGenerator generator = ItemContainerGenerator;

            for (int i = 0; i < Children.Count; i++)
            {
                UIElement child = Children[i];
                int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));

                int row = itemIndex / _columns;
                int column = itemIndex % _columns;

                child.Arrange(new Rect(
                    column * ChildSize.Width,
                    row * ChildSize.Height,
                    ChildSize.Width,
                    ChildSize.Height));
            }

            return finalSize;
        }

        private void UpdateScrollInfo(Size availableSize)
        {
            ItemsControl itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;

            double viewportWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
                ? _viewport.Width
                : availableSize.Width;

            _columns = ChildSize.Width > 0
                ? Math.Max(1, (int)Math.Floor(viewportWidth / ChildSize.Width))
                : 1;

            var extent = new Size(
                viewportWidth,
                ChildSize.Height > 0
                    ? Math.Ceiling(itemCount / (double)_columns) * ChildSize.Height
                    : 0);

            if (extent != _extent)
            {
                _extent = extent;

                // If the collection shrank (e.g. the search filter removed items),
                // pull the scroll offset back into the new, smaller extent so the
                // gallery does not render as an empty viewport.
                double maxVertical = Math.Max(0, _extent.Height - ViewportHeight);
                if (_offset.Y > maxVertical)
                {
                    _offset.Y = maxVertical;
                    _trans.Y = -_offset.Y;
                }

                _owner?.InvalidateScrollInfo();
            }

            if (availableSize != _viewport
                && !double.IsInfinity(availableSize.Width)
                && !double.IsInfinity(availableSize.Height))
            {
                _viewport = availableSize;
                _owner?.InvalidateScrollInfo();
            }
        }

        private void GetVisibleRange(out int firstVisibleItemIndex, out int lastVisibleItemIndex)
        {
            ItemsControl itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;

            if (itemCount == 0 || ChildSize.Height <= 0)
            {
                firstVisibleItemIndex = 0;
                lastVisibleItemIndex = -1;
                return;
            }

            int firstRow = (int)Math.Floor(_offset.Y / ChildSize.Height);
            int lastRow = (int)Math.Ceiling((_offset.Y + ViewportHeight) / ChildSize.Height);

            firstVisibleItemIndex = Math.Max(0, firstRow * _columns);
            lastVisibleItemIndex = Math.Min(itemCount - 1, lastRow * _columns - 1);
        }

        private void CleanUpItems(int minDesiredGenerated, int maxDesiredGenerated)
        {
            UIElementCollection children = InternalChildren;
            IItemContainerGenerator generator = ItemContainerGenerator;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var childGeneratorPos = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(childGeneratorPos);
                if (itemIndex < minDesiredGenerated || itemIndex > maxDesiredGenerated)
                {
                    generator.Remove(childGeneratorPos, 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
        }
        #region IScrollInfo

        /// <inheritdoc />
        public ScrollViewer? ScrollOwner
        {
            get => _owner;
            set => _owner = value;
        }

        /// <inheritdoc />
        public bool CanHorizontallyScroll
        {
            get => _canHScroll;
            set => _canHScroll = value;
        }

        /// <inheritdoc />
        public bool CanVerticallyScroll
        {
            get => _canVScroll;
            set => _canVScroll = value;
        }

        /// <inheritdoc />
        public double ExtentWidth => _extent.Width;

        /// <inheritdoc />
        public double ExtentHeight => _extent.Height;

        /// <inheritdoc />
        public double ViewportWidth => _viewport.Width;

        /// <inheritdoc />
        public double ViewportHeight => _viewport.Height;

        /// <inheritdoc />
        public double HorizontalOffset => _offset.X;

        /// <inheritdoc />
        public double VerticalOffset => _offset.Y;

        /// <inheritdoc />
        public void LineUp() => SetVerticalOffset(VerticalOffset - ChildSize.Height);

        /// <inheritdoc />
        public void LineDown() => SetVerticalOffset(VerticalOffset + ChildSize.Height);

        /// <inheritdoc />
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ChildSize.Width);

        /// <inheritdoc />
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + ChildSize.Width);

        /// <inheritdoc />
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

        /// <inheritdoc />
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

        /// <inheritdoc />
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

        /// <inheritdoc />
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

        /// <inheritdoc />
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 3 * ChildSize.Height);

        /// <inheritdoc />
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 3 * ChildSize.Height);

        /// <inheritdoc />
        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 3 * ChildSize.Width);

        /// <inheritdoc />
        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 3 * ChildSize.Width);

        /// <inheritdoc />
        public void SetHorizontalOffset(double offset)
        {
            offset = ClampOffset(offset, ViewportWidth, ExtentWidth);
            if (offset == _offset.X)
                return;

            _offset.X = offset;
            _owner?.InvalidateScrollInfo();
            _trans.X = -offset;
            InvalidateMeasure();
        }

        /// <inheritdoc />
        public void SetVerticalOffset(double offset)
        {
            offset = ClampOffset(offset, ViewportHeight, ExtentHeight);
            if (offset == _offset.Y)
                return;

            _offset.Y = offset;
            _owner?.InvalidateScrollInfo();
            _trans.Y = -offset;
            InvalidateMeasure();
        }

        /// <inheritdoc />
        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (ReferenceEquals(InternalChildren[i], visual))
                {
                    int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                    int row = itemIndex / _columns;
                    double top = row * ChildSize.Height;
                    double bottom = top + ChildSize.Height;

                    if (top < VerticalOffset)
                        SetVerticalOffset(top);
                    else if (bottom > VerticalOffset + ViewportHeight)
                        SetVerticalOffset(bottom - ViewportHeight);
                    break;
                }
            }

            return rectangle;
        }

        private static double ClampOffset(double offset, double viewport, double extent)
        {
            if (offset < 0 || viewport >= extent)
                return 0;
            if (offset + viewport >= extent)
                return Math.Max(0, extent - viewport);
            return offset;
        }

        #endregion
    }
}
