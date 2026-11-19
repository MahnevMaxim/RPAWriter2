using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RPAWriter2.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Domain "Packages" tab: filter box, package list and the restore panel. The prefix filter is
    /// pure view state and lives here; refresh/install/update/uninstall/restore stay in the shell
    /// because they need the package service.
    /// </summary>
    public partial class PackagesControl : UserControl
    {
        private ICollectionView? _packagesView;

        public PackagesControl()
        {
            InitializeComponent();
        }

        public ListView PackagesList => PackagesListControl;
        public TextBlock PackagesStatusText => PackagesStatusTextControl;
        public Border RestorePackagesPanel => RestorePackagesPanelControl;

        // ---- Shell-service callbacks (refresh/install/update/uninstall/restore). ----

        public event EventHandler<RoutedEventArgs>? RefreshRequested;
        public event EventHandler<RoutedEventArgs>? InstallRequested;
        public event EventHandler<RoutedEventArgs>? UpdateRequested;
        public event EventHandler<RoutedEventArgs>? UninstallRequested;
        public event EventHandler<RoutedEventArgs>? RestoreRequested;

        private void OnRefreshPackages(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, e);
        private void OnInstallSelectedPackages(object sender, RoutedEventArgs e) => InstallRequested?.Invoke(this, e);
        private void OnUpdateSelectedPackages(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, e);
        private void OnUninstallSelectedPackages(object sender, RoutedEventArgs e) => UninstallRequested?.Invoke(this, e);
        private void OnRestorePackages(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, e);

        /// <summary>Binds the shell-owned package collection and wires the prefix filter.</summary>
        public void BindPackages(ObservableCollection<PackageListItem> packages)
        {
            _packagesView = CollectionViewSource.GetDefaultView(packages);
            _packagesView.Filter = FilterPackages;
            PackagesList.ItemsSource = _packagesView;
        }

        private bool FilterPackages(object item)
        {
            var filter = (PackageFilterBox?.Text ?? "").Trim();
            if (filter.Length == 0)
                return true;
            return item is PackageListItem package &&
                   package.Id.StartsWith(filter, StringComparison.Ordinal);
        }

        private void OnPackageFilterChanged(object sender, TextChangedEventArgs e)
            => _packagesView?.Refresh();
    }
}
