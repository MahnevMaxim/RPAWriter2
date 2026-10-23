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
using System.Windows.Data;
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

        private readonly PixMwfMappingStore _store = new PixMwfMappingStore();
        private readonly ObservableCollection<PixMwfMappingRow> _rows = new ObservableCollection<PixMwfMappingRow>();
        private ICollectionView _view;
        private CancellationTokenSource _aiCts;

        public PixMwfMappingControl()
        {
            InitializeComponent();

            DataContext = this;

            _view = CollectionViewSource.GetDefaultView(_rows);
            if (_view is ListCollectionView listView)
            {
                listView.IsLiveSorting = true;
                listView.LiveSortingProperties.Add(nameof(PixMwfMappingRow.IsMapped));
                listView.LiveSortingProperties.Add(nameof(PixMwfMappingRow.Index));
                listView.SortDescriptions.Add(new SortDescription(nameof(PixMwfMappingRow.IsMapped), ListSortDirection.Descending));
                listView.SortDescriptions.Add(new SortDescription(nameof(PixMwfMappingRow.Index), ListSortDirection.Ascending));
            }

            MapList.ItemsSource = _view;
            this.Unloaded += OnUnloaded;
        }

        /// <summary>Raised when the user clicks Close; the host hides the control.</summary>
        public event EventHandler CloseRequested;

        /// <summary>The mapping document shared with the converter.</summary>
        public PixMwfMappingStore Store => _store;

        /// <summary>AI provider used by the "AI Map" button.</summary>
        public IAiProvider AiProvider { get; set; }

        /// <summary>All MWF activities available for assignment (bound by the table's ComboBox).</summary>
        public ObservableCollection<WfActivityDescriptor> MwfActivities { get; } = new ObservableCollection<WfActivityDescriptor>();

        /// <summary>Loads (or reloads) the PIX activities, MWF activities and the saved mapping.</summary>
        public void LoadData()
        {
            LoadMwfActivities();
            LoadPixRows();

            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetStatus(_rows.Count + " PIX activities, " + MwfActivities.Count + " MWF activities. Mapped: " +
                      _rows.Count(r => r.IsMapped) + ".");
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
            var descriptions = MwfActivities.ToDictionary(
                d => d.ActivityFullName,
                d => d.Description ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

            _rows.Clear();
            var library = new PixActivityLibrary();
            var descriptors = library.GetKnownActivityNames()
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
                    _store.GetMapping(descriptor.ActivityFullName),
                    _store,
                    descriptions));
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

        private void OnReload(object sender, RoutedEventArgs e)
        {
            _store.Load();
            LoadData();
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

            if (_rows.Count == 0)
            {
                SetStatus("No PIX activities to map.");
                return;
            }

            if (MwfActivities.Count == 0)
            {
                SetStatus("No MWF activities available — import them first with 'Get All MWF Activities'.");
                return;
            }

            SetBusy(true);
            _aiCts = new CancellationTokenSource();
            try
            {
                var tool = BuildMappingTool();
                var tools = new List<ToolDefinition> { ToolSchemaConverter.ToToolDefinition(tool) };
                var messages = new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = BuildMappingPrompt() }
                };

                for (var i = 0; i < MaxAiIterations; i++)
                {
                    var response = await ai.ChatAsync(messages, MappingSystemPrompt, tools, _aiCts.Token);
                    var calls = (response?.ToolCalls ?? new List<NativeToolCall>()).ToList();
                    if (calls.Count == 0)
                    {
                        var summary = string.IsNullOrWhiteSpace(response?.Answer) ? string.Empty : " " + response.Answer.Trim();
                        SetStatus("AI mapping finished." + summary);
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

                    SetStatus("AI mapping: applied " + applied + " assignment(s) in step " + (i + 1) + ".");
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("AI mapping cancelled.");
            }
            catch (Exception ex)
            {
                SetStatus("AI mapping failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                _aiCts?.Dispose();
                _aiCts = null;
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

        private string BuildMappingPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Map the following PIX activities to MWF activities in the given order:");
            sb.AppendLine();
            sb.AppendLine("PIX activities:");
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                sb.Append(i + 1).Append(". ").Append(row.PixFullName).Append(" — ").Append(row.PixDisplayName);
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

        private void SetBusy(bool busy)
        {
            AiMapButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy;
            ReloadButton.IsEnabled = !busy;
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
