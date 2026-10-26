using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RPAWriter.Core.Models;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Modal dialog that lists the discovered built-in <c>System.Activities</c> activities with
    /// checkboxes so the user can pick which ones should be imported into the MWF library.
    /// </summary>
    public partial class SystemActivitiesSelectionWindow : Window
    {
        private readonly ObservableCollection<SystemActivityItemVm> _items = new ObservableCollection<SystemActivityItemVm>();
        private readonly ICollectionView _view;

        public SystemActivitiesSelectionWindow(IReadOnlyList<WfActivityDescriptor> activities, ISet<string> initiallySelected)
        {
            InitializeComponent();

            var selected = initiallySelected ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var activity in (activities ?? new List<WfActivityDescriptor>())
                         .OrderBy(a => a.ActivityFullName, StringComparer.OrdinalIgnoreCase))
            {
                var item = new SystemActivityItemVm(activity, selected.Contains(activity.ActivityFullName));
                item.PropertyChanged += OnItemPropertyChanged;
                _items.Add(item);
            }

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = FilterItem;
            ActivityList.ItemsSource = _view;

            UpdateCountHint();
        }

        /// <summary>Full names of the activities selected when the user clicks OK.</summary>
        public IReadOnlyList<string> SelectedFullNames =>
            _items.Where(i => i.IsSelected).Select(i => i.FullName).ToList();

        private bool FilterItem(object item)
        {
            if (!(item is SystemActivityItemVm vm))
                return false;

            var text = SearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return true;

            return vm.FullName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || vm.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _view.Refresh();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            SetAll(true);
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            SetAll(false);
        }

        private void SetAll(bool selected)
        {
            foreach (var item in _items)
                item.IsSelected = selected;

            UpdateCountHint();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(SystemActivityItemVm.IsSelected), StringComparison.Ordinal))
                UpdateCountHint();
        }

        private void UpdateCountHint()
        {
            var selected = _items.Count(i => i.IsSelected);
            CountHint.Text = selected + " of " + _items.Count + " selected";
        }
    }

    /// <summary>Checkbox row backing the selection list.</summary>
    public sealed class SystemActivityItemVm : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SystemActivityItemVm(WfActivityDescriptor descriptor, bool isSelected)
        {
            FullName = descriptor?.ActivityFullName ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(descriptor?.DefaultDisplayName)
                ? FullName
                : descriptor.DefaultDisplayName;
            _isSelected = isSelected;
        }

        /// <summary>Full CLR type name of the activity.</summary>
        public string FullName { get; }

        /// <summary>Human-readable display name.</summary>
        public string DisplayName { get; }

        /// <summary>Whether the activity is selected for import.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
