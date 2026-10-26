using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RPAWriter.Core.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Modal window that lists the deployed Workflow Foundation activity snippets and shows the
    /// XAML template of the selected one.
    /// </summary>
    public partial class WfSnippetsWindow : Window
    {
        private readonly IReadOnlyList<WfSnippet> _snippets;

        public WfSnippetsWindow()
        {
            InitializeComponent();

            _snippets = new WfSnippetLibrary().GetAll();
            SnippetList.ItemsSource = _snippets;
            SnippetList.DisplayMemberPath = nameof(WfSnippet.Name);

            if (_snippets.Count > 0)
                SnippetList.SelectedIndex = 0;
            else
                SnippetContent.Text = "(No WF snippets found.)";
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SnippetList.SelectedItem is WfSnippet snippet)
                SnippetContent.Text = snippet.Content ?? string.Empty;
        }
    }
}
