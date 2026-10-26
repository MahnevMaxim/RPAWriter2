using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Manual + AI mapping of PIX activities to Workflow Foundation (MWF) activities.
    /// The "AI Map" button sends the activities to the AI provider, which assigns
    /// mappings through the native <c>map_pix_to_mwf</c> tool.
    /// </summary>
    public partial class PixMwfMappingControl : UserControl
    {
        private const int MaxAiIterations = 60;

        private const string MappingSystemPrompt =
            "You map PIX RPA activities to Microsoft Workflow Foundation (MWF) activities. " +
            "Your only tool is map_pix_to_mwf. " +
            "Process the PIX activities strictly in the order listed in the user message and call map_pix_to_mwf once per activity. " +
            "Choose the closest semantic match among the available MWF activities. " +
            "If no MWF activity fits an activity, clear it by passing an empty mwf_activity. " +
            "After all assignments, answer with a short summary. Do not invent activities that are not listed.";

        private const string ArgumentMappingSystemPrompt =
            "You map arguments of already-mapped PIX RPA activities to arguments of their mapped Microsoft Workflow Foundation (MWF) activities. " +
            "Your only tool is map_pix_mwf_arguments. " +
            "For each PIX activity listed in the user message, process its arguments in the listed order and call map_pix_mwf_arguments once per argument. " +
            "Choose the closest semantic match among the available MWF arguments (match by name, then by type and direction). " +
            "Set conversion to None when the source and target types match, ToString when the target is a string, DateTimeFormat when parsing a date/time (expression = format string), " +
            "Parse when converting with ChangeType (expression = target type name), and Custom when a custom C# expression template is needed (expression uses {0} for the source value). " +
            "If no MWF argument fits an argument, unmap it by passing an empty mwf_argument. " +
            "After all assignments, answer with a short summary. Do not invent arguments that are not listed.";

        private readonly PixMwfMappingStore _store;
        private readonly ObservableCollection<PixMwfMappingRow> _rows = new ObservableCollection<PixMwfMappingRow>();
        private ICollectionView _view;
        private ListCollectionView _listView;
        private string _filterText = string.Empty;
        private string _mwfFilterText = string.Empty;
        private HashSet<string> _mappedMwfNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ISet<string> _onlyActivityNames;
        private CancellationTokenSource _aiCts;

        public PixMwfMappingControl() : this(PixMwfMappingStore.Shared)
        {
        }

        public PixMwfMappingControl(PixMwfMappingStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            InitializeComponent();

            DataContext = this;

            _view = CollectionViewSource.GetDefaultView(_rows);
            _listView = _view as ListCollectionView;
            if (_listView != null)
            {
                _listView.IsLiveSorting = true;
                _listView.LiveSortingProperties.Add(nameof(PixMwfMappingRow.IsMapped));
                _listView.LiveSortingProperties.Add(nameof(PixMwfMappingRow.Index));
                _listView.SortDescriptions.Add(new SortDescription(nameof(PixMwfMappingRow.IsMapped), ListSortDirection.Descending));
                _listView.SortDescriptions.Add(new SortDescription(nameof(PixMwfMappingRow.Index), ListSortDirection.Ascending));
                _listView.Filter = FilterRow;
            }

            MapList.ItemsSource = _view;

            // Filter the MWF activity drop-down without changing the underlying collection,
            // so AI mapping and validation still see every available activity.
            CollectionViewSource.GetDefaultView(MwfActivities).Filter = FilterMwfActivity;

            this.Unloaded += OnUnloaded;
        }

        /// <summary>Raised when the user clicks Close; the host hides the control.</summary>
        public event EventHandler CloseRequested;

        /// <summary>The mapping document shared with the converter.</summary>
        public PixMwfMappingStore Store => _store;

        /// <summary>AI provider used by the "AI Map" button.</summary>
        public IAiProvider AiProvider { get; set; }

        /// <summary>Optional sink for AI mapping progress messages (the host points it at the converter output log).</summary>
        public Action<string> Log { get; set; }

        /// <summary>All MWF activities available for assignment (bound by the table's ComboBox).</summary>
        public ObservableCollection<WfActivityDescriptor> MwfActivities { get; } = new ObservableCollection<WfActivityDescriptor>();

        /// <summary>Loads (or reloads) the PIX activities, MWF activities and the saved mapping.</summary>
        public void LoadData()
        {
            LoadMwfActivities();
            LoadPixRows();
            RefreshMwfActivitiesView();
            _listView?.Refresh();

            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetStatus(_rows.Count + " PIX activities, " + MwfActivities.Count + " MWF activities. Mapped: " +
                      _rows.Count(r => r.IsMapped) + ".");
        }

        /// <summary>
        /// Shows only the activities found in a scanned PIX robot folder. Activities that
        /// are not yet part of the table are appended first (with a minimal descriptor) so
        /// they can be mapped, then the table is filtered to the given names.
        /// </summary>
        public void SetFolderActivitiesFilter(IEnumerable<string> activityFullNames)
        {
            var names = new HashSet<string>(
                activityFullNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            EnsureRowsExist(names);

            _onlyActivityNames = names;
            _listView?.Refresh();

            SetStatus("Filtered to " + names.Count + " PIX activities found in the selected folder." +
                      (names.Count == 0 ? " Clear the filter to show all activities." : string.Empty));
        }

        /// <summary>Clears the folder-based filter (the text filter box stays user-controlled).</summary>
        public void ClearActivityFilter()
        {
            _onlyActivityNames = null;
            _listView?.Refresh();
            SetStatus("Activity filter cleared.");
        }

        private void EnsureRowsExist(ISet<string> names)
        {
            if (names.Count == 0)
                return;

            var existing = new HashSet<string>(_rows.Select(r => r.PixFullName), StringComparer.OrdinalIgnoreCase);
            var toAdd = names
                .Where(n => !string.IsNullOrWhiteSpace(n) && !existing.Contains(n) &&
                            !PixActivityLibrary.MappingExcludedActivities.Contains(n))
                .ToList();
            if (toAdd.Count == 0)
                return;

            var mwfDescriptors = MwfActivities.ToDictionary(
                d => d.ActivityFullName,
                d => d,
                StringComparer.OrdinalIgnoreCase);

            var index = _rows.Count;
            foreach (var name in toAdd.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                _rows.Add(new PixMwfMappingRow(
                    index++,
                    name,
                    name,
                    string.Empty,
                    new List<PixActivityParameter>(),
                    _store.GetMapping(name),
                    _store,
                    mwfDescriptors,
                    RefreshMwfActivitiesView));
            }

            // Newly added rows may already be mapped (from the store); keep their MWF
            // activities visible in the drop-down while a filter is active.
            RefreshMwfActivitiesView();
        }
        private void LoadMwfActivities()
        {
            MwfActivities.Clear();
            var library = new MwfActivityLibrary();
            foreach (var name in library.GetKnownActivityNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var descriptor = library.Load(name);
                if (descriptor != null)
                    MwfActivities.Add(descriptor);
            }
        }

        private void LoadPixRows()
        {
            var mwfDescriptors = MwfActivities.ToDictionary(
                d => d.ActivityFullName,
                d => d,
                StringComparer.OrdinalIgnoreCase);

            _rows.Clear();
            var library = new PixActivityLibrary();
            var descriptors = library.GetKnownActivityNames()
                .Where(name => !PixActivityLibrary.MappingExcludedActivities.Contains(name))
                .Select(library.Load)
                .Where(d => d != null)
                .OrderBy(StructuralPriority)
                .ThenBy(d => d.ActivityFullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var index = 0;
            foreach (var descriptor in descriptors)
            {
                _rows.Add(new PixMwfMappingRow(
                    index++,
                    descriptor.ActivityFullName,
                    descriptor.DefaultDisplayName ?? descriptor.ActivityFullName,
                    descriptor.Description ?? string.Empty,
                    descriptor.Parameters,
                    _store.GetMapping(descriptor.ActivityFullName),
                    _store,
                    mwfDescriptors,
                    RefreshMwfActivitiesView));
            }
        }

        /// <summary>Control-flow/structural activities are mapped first, statements after them.</summary>
        private static int StructuralPriority(PixActivityDescriptor descriptor)
        {
            switch (descriptor?.Role)
            {
                case "container":
                case "if":
                case "then":
                case "elseIf":
                case "else":
                case "tryCatch":
                case "try":
                case "catch":
                case "finally":
                    return 0;
                case "statement":
                    return 1;
                default:
                    return 2;
            }
        }
        private void OnSave(object sender, RoutedEventArgs e)
        {
            _store.Save();
            SetStatus("Mapping saved to " + _store.FilePath + " (" + _store.Count + " mappings).");
        }

        private void OnSaveAs(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save PIX → MWF mapping",
                Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                FileName = "pix-mwf-map.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog() != true)
                return;

            _store.SaveAs(dialog.FileName);
            SetStatus("Mapping saved to " + dialog.FileName + " (" + _store.Count + " mappings).");
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            _store.Load();
            LoadData();
        }

        private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
        {
            _filterText = FilterBox?.Text ?? string.Empty;
            _listView?.Refresh();
        }

        private void OnClearFilter(object sender, RoutedEventArgs e)
        {
            if (FilterBox != null)
                FilterBox.Text = string.Empty;
            _filterText = string.Empty;
            ClearActivityFilter();
        }

        private bool FilterRow(object item)
        {
            var row = item as PixMwfMappingRow;
            if (row == null)
                return false;

            if (_onlyActivityNames != null && _onlyActivityNames.Count > 0 &&
                !_onlyActivityNames.Contains(row.PixFullName))
                return false;

            if (string.IsNullOrWhiteSpace(_filterText))
                return true;

            return row.PixFullName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.PixDisplayName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Rows currently visible in the table (folder filter + text filter).</summary>
        private List<PixMwfMappingRow> GetFilteredRows()
        {
            return _rows.Where(FilterRow).ToList();
        }

        private void OnMwfFilterTextChanged(object sender, TextChangedEventArgs e)
        {
            _mwfFilterText = MwfFilterBox?.Text ?? string.Empty;
            RefreshMwfActivitiesView();
        }

        private void OnClearMwfFilter(object sender, RoutedEventArgs e)
        {
            if (MwfFilterBox != null)
                MwfFilterBox.Text = string.Empty;
            _mwfFilterText = string.Empty;
            RefreshMwfActivitiesView();
        }

        private bool FilterMwfActivity(object item)
        {
            var activity = item as WfActivityDescriptor;
            if (activity == null)
                return false;

            if (string.IsNullOrWhiteSpace(_mwfFilterText))
                return true;

            // Keep already-assigned MWF activities visible in the drop-down, so filtering
            // the list never clears an existing PIX → MWF mapping.
            if (_mappedMwfNames.Contains(activity.ActivityFullName))
                return true;

            return ContainsIgnoreCase(activity.ActivityFullName, _mwfFilterText) ||
                   ContainsIgnoreCase(activity.DefaultDisplayName, _mwfFilterText) ||
                   ContainsIgnoreCase(activity.Description, _mwfFilterText);
        }

        private void RefreshMwfActivitiesView()
        {
            RebuildMappedMwfNames();
            CollectionViewSource.GetDefaultView(MwfActivities).Refresh();
        }

        private void RebuildMappedMwfNames()
        {
            _mappedMwfNames = new HashSet<string>(
                _rows.Where(r => r.IsMapped).Select(r => r.MwfFullName),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string fragment)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            CancelAi();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            CancelAi();
            SetStatus("Cancelled.");
        }

        private void OnRowAction(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is PixMwfMappingRow row))
                return;

            if (row.IsMapped)
            {
                ApplyMapping(row.PixFullName, null);
                SetStatus("Unmapped " + row.PixFullName + ".");
            }
            else
            {
                row.IsPickerOpen = true;
            }
        }

        private void OnToggleDetails(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is PixMwfMappingRow row))
                return;

            row.IsDetailsOpen = !row.IsDetailsOpen;

            var container = MapList.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow;
            if (container != null)
                container.DetailsVisibility = row.IsDetailsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnToggleArgumentMapping(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is PixMwfMappingRow row))
                return;

            if (!row.IsMapped)
                return;

            row.IsArgumentMappingOpen = !row.IsArgumentMappingOpen;
        }

        private void OnAutoMatchArguments(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is PixMwfMappingRow row))
                return;

            row.AutoMatchArguments();
        }

        private void OnTogglePixDescription(object sender, MouseButtonEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is PixMwfMappingRow row) || !row.HasPixDescriptionOverflow)
                return;

            row.IsPixDescriptionExpanded = !row.IsPixDescriptionExpanded;
            if (MapList.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow container)
                container.InvalidateMeasure();
            e.Handled = true;
        }

        private void OnMapListLoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is PixMwfMappingRow row)
                e.Row.DetailsVisibility = row.IsDetailsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private string ApplyMapping(string pixFullName, string mwfFullName)
        {
            if (string.IsNullOrWhiteSpace(pixFullName))
                return "Error: pix_activity is required.";

            var row = _rows.FirstOrDefault(r => string.Equals(r.PixFullName, pixFullName, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                return "Unknown PIX activity: " + pixFullName;

            if (!string.IsNullOrWhiteSpace(mwfFullName) &&
                MwfActivities.All(m => !string.Equals(m.ActivityFullName, mwfFullName, StringComparison.OrdinalIgnoreCase)))
                return "Unknown MWF activity: " + mwfFullName;

            row.MwfFullName = string.IsNullOrWhiteSpace(mwfFullName) ? null : mwfFullName;
            return "Mapped " + row.PixFullName + " → " + (row.MwfFullName ?? "(none)");
        }
        private async void OnAiMap(object sender, RoutedEventArgs e)
        {
            var ai = AiProvider;
            if (ai == null)
            {
                SetStatus("AI mapping requires a configured AI provider (OpenAI-compatible).");
                return;
            }

            var rows = GetFilteredRows();
            if (rows.Count == 0)
            {
                SetStatus("No PIX activities to map (current filter shows none).");
                return;
            }

            if (MwfActivities.Count == 0)
            {
                SetStatus("No MWF activities available — import them first with 'Get All MWF Activities'.");
                return;
            }

            SetBusy(true);
            _aiCts = new CancellationTokenSource();
            Log?.Invoke("AI mapping started for " + rows.Count + " activities.");
            SetStatus("AI mapping in progress — see Converter output.");
            try
            {
                var tool = BuildMappingTool();
                var tools = new List<ToolDefinition> { ToolSchemaConverter.ToToolDefinition(tool) };
                var messages = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = BuildMappingPrompt(rows) }
                };

                for (var i = 0; i < MaxAiIterations; i++)
                {
                    var response = await ai.ChatAsync(messages, MappingSystemPrompt, tools, _aiCts.Token);
                    var calls = (response?.ToolCalls ?? new List<NativeToolCall>()).ToList();
                    if (calls.Count == 0)
                    {
                        var summary = string.IsNullOrWhiteSpace(response?.Answer) ? string.Empty : " " + response.Answer.Trim();
                        Log?.Invoke("AI mapping finished." + summary);
                        SetStatus("AI mapping finished.");
                        break;
                    }

                    messages.Add(new ChatMessageData { Role = "assistant", Content = response?.Answer ?? string.Empty, ToolCalls = calls });

                    var applied = 0;
                    foreach (var call in calls)
                    {
                        var result = await tool.ExecuteAsync(call.Arguments ?? new JObject(), _aiCts.Token);
                        messages.Add(new ChatMessageData { Role = "tool", ToolCallId = call.Id, Content = result });
                        if (!result.StartsWith("Error", StringComparison.OrdinalIgnoreCase) &&
                            !result.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                            applied++;
                    }

                    Log?.Invoke("AI mapping: applied " + applied + " assignment(s) in step " + (i + 1) + ".");
                }
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("AI mapping cancelled.");
                SetStatus("AI mapping cancelled.");
            }
            catch (Exception ex)
            {
                Log?.Invoke("AI mapping failed: " + ex.Message);
                SetStatus("AI mapping failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                _aiCts?.Dispose();
                _aiCts = null;
            }
        }
        private async void OnAiMapArguments(object sender, RoutedEventArgs e)
        {
            var ai = AiProvider;
            if (ai == null)
            {
                SetStatus("AI argument mapping requires a configured AI provider (OpenAI-compatible).");
                return;
            }

            var mappedRows = GetFilteredRows().Where(r => r.IsMapped).ToList();
            if (mappedRows.Count == 0)
            {
                SetStatus("No mapped PIX activities to map arguments for — assign MWF activities first.");
                return;
            }

            if (mappedRows.All(r => r.PixArguments.Count == 0))
            {
                SetStatus("Mapped activities have no arguments to map.");
                return;
            }

            SetBusy(true);
            _aiCts = new CancellationTokenSource();
            Log?.Invoke("AI argument mapping started for " + mappedRows.Count + " mapped activities.");
            SetStatus("AI argument mapping in progress — see Converter output.");
            try
            {
                var tool = BuildArgumentMappingTool();
                var tools = new List<ToolDefinition> { ToolSchemaConverter.ToToolDefinition(tool) };
                var messages = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = BuildArgumentMappingPrompt(mappedRows) }
                };

                for (var i = 0; i < MaxAiIterations; i++)
                {
                    var response = await ai.ChatAsync(messages, ArgumentMappingSystemPrompt, tools, _aiCts.Token);
                    var calls = (response?.ToolCalls ?? new List<NativeToolCall>()).ToList();
                    if (calls.Count == 0)
                    {
                        var summary = string.IsNullOrWhiteSpace(response?.Answer) ? string.Empty : " " + response.Answer.Trim();
                        Log?.Invoke("AI argument mapping finished." + summary);
                        SetStatus("AI argument mapping finished.");
                        break;
                    }

                    messages.Add(new ChatMessageData { Role = "assistant", Content = response?.Answer ?? string.Empty, ToolCalls = calls });

                    var applied = 0;
                    foreach (var call in calls)
                    {
                        var result = await tool.ExecuteAsync(call.Arguments ?? new JObject(), _aiCts.Token);
                        messages.Add(new ChatMessageData { Role = "tool", ToolCallId = call.Id, Content = result });
                        if (!result.StartsWith("Error", StringComparison.OrdinalIgnoreCase) &&
                            !result.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                            applied++;
                    }

                    Log?.Invoke("AI argument mapping: applied " + applied + " assignment(s) in step " + (i + 1) + ".");
                }
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("AI argument mapping cancelled.");
                SetStatus("AI argument mapping cancelled.");
            }
            catch (Exception ex)
            {
                Log?.Invoke("AI argument mapping failed: " + ex.Message);
                SetStatus("AI argument mapping failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                _aiCts?.Dispose();
                _aiCts = null;
            }
        }

        private AgentTool BuildArgumentMappingTool()
        {
            return new AgentTool(
                "map_pix_mwf_arguments",
                "Map one PIX activity argument to one MWF (Workflow Foundation) activity argument in the mapping table. " +
                "pix_activity and pix_argument are required. " +
                "mwf_argument is the exact MWF argument name; pass an empty string to unmap. " +
                "conversion is one of None, ToString, Parse, DateTimeFormat, Custom. " +
                "conversion_expression is the format string or a custom C# expression template where {0} is the source value.",
                "{\"pix_activity\":\"string\",\"pix_argument\":\"string\",\"mwf_argument?\":\"string\",\"conversion?\":\"string\",\"conversion_expression?\":\"string\"}",
                mutates: true,
                (args, ct) =>
                {
                    var pixActivity = args?["pix_activity"]?.Value<string>() ?? string.Empty;
                    var pixArgument = args?["pix_argument"]?.Value<string>() ?? string.Empty;
                    var mwfArgument = args?["mwf_argument"]?.Value<string>() ?? string.Empty;
                    var conversion = args?["conversion"]?.Value<string>() ?? "None";
                    var expression = args?["conversion_expression"]?.Value<string>() ?? string.Empty;
                    return Task.FromResult(ApplyArgumentMapping(pixActivity, pixArgument, mwfArgument, conversion, expression));
                });
        }

        private string ApplyArgumentMapping(string pixActivity, string pixArgument, string mwfArgument, string conversionText, string expression)
        {
            if (string.IsNullOrWhiteSpace(pixActivity))
                return "Error: pix_activity is required.";
            if (string.IsNullOrWhiteSpace(pixArgument))
                return "Error: pix_argument is required.";

            var row = _rows.FirstOrDefault(r => string.Equals(r.PixFullName, pixActivity, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                return "Unknown PIX activity: " + pixActivity;

            if (!row.IsMapped)
                return "PIX activity is not mapped to an MWF activity: " + pixActivity;

            if (!string.IsNullOrWhiteSpace(mwfArgument) &&
                row.MwfArguments.All(a => !string.Equals(a.Name, mwfArgument, StringComparison.OrdinalIgnoreCase)))
                return "Unknown MWF argument: " + mwfArgument;

            if (!TryParseConversion(conversionText, out var conversion))
                return "Error: unknown conversion \"" + conversionText + "\". Use None, ToString, Parse, DateTimeFormat or Custom.";

            row.ApplyArgumentMapping(
                pixArgument,
                string.IsNullOrWhiteSpace(mwfArgument) ? null : mwfArgument,
                conversion,
                string.IsNullOrWhiteSpace(expression) ? null : expression);

            return string.IsNullOrWhiteSpace(mwfArgument)
                ? "Unmapped argument " + pixArgument + " of " + pixActivity + "."
                : "Mapped " + pixActivity + "." + pixArgument + " → " + mwfArgument + " (" + conversion + ").";
        }

        private static bool TryParseConversion(string text, out ConversionKind conversion)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "none": conversion = ConversionKind.None; return true;
                case "tostring": conversion = ConversionKind.ToString; return true;
                case "parse": conversion = ConversionKind.Parse; return true;
                case "datetimeformat":
                case "datetime": conversion = ConversionKind.DateTimeFormat; return true;
                case "custom": conversion = ConversionKind.Custom; return true;
                default: conversion = ConversionKind.None; return false;
            }
        }

        private AgentTool BuildMappingTool()
        {
            return new AgentTool(
                "map_pix_to_mwf",
                "Assign one PIX activity to one MWF (Workflow Foundation) activity in the mapping table. " +
                "pix_activity is the exact PIX activity full name. " +
                "mwf_activity is the exact MWF activity full name; pass an empty string to unmap the PIX activity.",
                "{\"pix_activity\":\"string\",\"mwf_activity\":\"string\"}",
                mutates: true,
                (args, ct) =>
                {
                    var pix = args?["pix_activity"]?.Value<string>() ?? string.Empty;
                    var mwf = args?["mwf_activity"]?.Value<string>() ?? string.Empty;
                    return Task.FromResult(ApplyMapping(pix, mwf));
                });
        }

        private string BuildMappingPrompt(IEnumerable<PixMwfMappingRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Map the following PIX activities to MWF activities in the given order:");
            sb.AppendLine();
            sb.AppendLine("PIX activities:");
            var index = 0;
            foreach (var row in rows)
            {
                index++;
                sb.Append(index).Append(". ").Append(row.PixFullName).Append(" — ").Append(row.PixDisplayName);
                if (!string.IsNullOrWhiteSpace(row.PixDescription))
                    sb.Append(" (").Append(row.PixDescription).Append(")");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Available MWF activities:");
            foreach (var activity in MwfActivities)
            {
                sb.Append("- ").Append(activity.ActivityFullName);
                if (!string.IsNullOrWhiteSpace(activity.Description))
                    sb.Append(" — ").Append(activity.Description);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string BuildArgumentMappingPrompt(IEnumerable<PixMwfMappingRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Map the arguments of the following already-mapped PIX activities to their MWF activity arguments:");
            foreach (var row in rows)
            {
                sb.AppendLine();
                sb.AppendLine("PIX activity: " + row.PixFullName + " (" + row.PixDisplayName + ")");
                sb.AppendLine("  MWF activity: " + row.MwfFullName);

                sb.AppendLine("  PIX arguments:");
                foreach (var a in row.PixArguments)
                    sb.AppendLine("    - " + a.Name + " : " + a.Type + " (" + a.Direction + ")" +
                        (string.IsNullOrEmpty(a.Description) ? string.Empty : " — " + a.Description));

                sb.AppendLine("  Available MWF arguments:");
                foreach (var a in row.MwfArguments)
                    sb.AppendLine("    - " + a.Name + " : " + a.Type + " (" + a.Direction + ")" +
                        (string.IsNullOrEmpty(a.Description) ? string.Empty : " — " + a.Description));
            }

            return sb.ToString();
        }

        private void SetBusy(bool busy)
        {
            AiMapButton.IsEnabled = !busy;
            AiMapArgumentsButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy;
            SaveAsButton.IsEnabled = !busy;
            ReloadButton.IsEnabled = !busy;
            ClearFilterButton.IsEnabled = !busy;
            FilterBox.IsEnabled = !busy;
            ClearMwfFilterButton.IsEnabled = !busy;
            MwfFilterBox.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            MapList.IsEnabled = !busy;
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message ?? string.Empty;
        }

        private void CancelAi()
        {
            try { _aiCts?.Cancel(); } catch { /* already disposed — ignore */ }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CancelAi();
        }
    }
}
