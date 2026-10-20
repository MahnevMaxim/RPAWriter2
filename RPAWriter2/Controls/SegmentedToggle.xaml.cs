using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Reusable segmented toggle that looks like a single rounded button divided into
    /// vertical segments. The active segment is filled with its accent color, inactive
    /// segments stay transparent, and one-pixel separators are drawn between segments.
    /// </summary>
    public partial class SegmentedToggle : UserControl
    {
        public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
            nameof(SelectedValue), typeof(object), typeof(SegmentedToggle),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public SegmentedToggle()
        {
            InitializeComponent();
            Items.CollectionChanged += OnItemsChanged;
        }

        /// <summary>Segments displayed by the toggle.</summary>
        public ObservableCollection<SegmentedToggleItem> Items { get; } = new ObservableCollection<SegmentedToggleItem>();

        /// <summary>Value of the selected segment (matches <see cref="SegmentedToggleItem.Value"/>).</summary>
        public object? SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        /// <summary>Raised after the user (or code) selects a segment.</summary>
        public event RoutedEventHandler? ValueChanged;

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            for (var i = 0; i < Items.Count; i++)
                Items[i].ShowSeparator = i < Items.Count - 1;
            EnsureInitialSelection();
        }

        private void EnsureInitialSelection()
        {
            if (Segments == null || Items.Count == 0)
                return;
            if (Segments.SelectedItem != null)
                return;
            if (SelectedValue != null)
                Segments.SelectedValue = SelectedValue;
            if (Segments.SelectedItem == null)
                Segments.SelectedIndex = 0;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Segments.SelectedItem == null)
                return;

            // Keep the DP in sync even if the two-way binding has not propagated yet.
            if (!Equals(SelectedValue, Segments.SelectedValue))
                SelectedValue = Segments.SelectedValue;

            ValueChanged?.Invoke(this, new RoutedEventArgs());
        }
    }
}
