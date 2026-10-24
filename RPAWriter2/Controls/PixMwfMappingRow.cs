using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// One row of the PIX → MWF mapping table. The row keeps the store in sync as the
    /// user (or the AI tool) changes the selected MWF activity, so <see cref="PixMwfMappingStore.Save"/>
    /// persists the current state.
    /// </summary>
    public sealed class PixMwfMappingRow : INotifyPropertyChanged
    {
        private readonly PixMwfMappingStore _store;
        private readonly IReadOnlyDictionary<string, WfActivityDescriptor> _mwfDescriptors;
        private readonly Action? _mwfChoicesChanged;
        private readonly ObservableCollection<ArgumentMappingRowVm> _argumentMappings = new ObservableCollection<ArgumentMappingRowVm>();
        private string _mwfFullName;
        private bool _isPickerOpen;
        private bool _isDetailsOpen;
        private bool _isArgumentMappingOpen;

        public PixMwfMappingRow(
            int index,
            string pixFullName,
            string pixDisplayName,
            string pixDescription,
            IReadOnlyList<PixActivityParameter> pixParameters,
            string mwfFullName,
            PixMwfMappingStore store,
            IReadOnlyDictionary<string, WfActivityDescriptor> mwfDescriptors,
            Action? mwfChoicesChanged = null)
        {
            Index = index;
            PixFullName = pixFullName ?? string.Empty;
            PixDisplayName = pixDisplayName ?? string.Empty;
            PixDescription = pixDescription ?? string.Empty;
            PixArguments = (pixParameters ?? new List<PixActivityParameter>())
                .Select(ActivityArgumentInfo.FromPix)
                .ToList();
            _mwfFullName = string.IsNullOrWhiteSpace(mwfFullName) ? null : mwfFullName.Trim();
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _mwfDescriptors = mwfDescriptors ?? new Dictionary<string, WfActivityDescriptor>(StringComparer.OrdinalIgnoreCase);
            _mwfChoicesChanged = mwfChoicesChanged;
            RebuildArgumentMappings();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Original table order, used to keep unmapped rows stable when they are pushed to the bottom.</summary>
        public int Index { get; }

        /// <summary>Column 1 — PIX activity full name.</summary>
        public string PixFullName { get; }

        /// <summary>Column 2 — PIX activity display name.</summary>
        public string PixDisplayName { get; }

        /// <summary>PIX activity description (used in the AI mapping prompt, not displayed in the table).</summary>
        public string PixDescription { get; }

        /// <summary>Arguments of the PIX activity: name, .NET type and data-flow direction.</summary>
        public IReadOnlyList<ActivityArgumentInfo> PixArguments { get; }

        /// <summary>Arguments of the currently selected MWF activity: name, .NET type and data-flow direction.</summary>
        public IReadOnlyList<ActivityArgumentInfo> MwfArguments
        {
            get
            {
                if (string.IsNullOrEmpty(MwfFullName) || !_mwfDescriptors.TryGetValue(MwfFullName, out var descriptor))
                    return Array.Empty<ActivityArgumentInfo>();
                return (descriptor.Parameters ?? new List<WfActivityParameter>())
                    .Select(ActivityArgumentInfo.FromMwf)
                    .ToList();
            }
        }

        /// <summary>Column 3 — selected MWF activity full name, or null when unmapped.</summary>
        public string MwfFullName
        {
            get => _mwfFullName;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (string.Equals(_mwfFullName, normalized, StringComparison.Ordinal))
                    return;

                _mwfFullName = normalized;
                _store.SetMapping(PixFullName, normalized);
                if (normalized == null)
                    _store.ClearArgumentMappings(PixFullName);

                // Refresh the MWF activity choices before the ComboBox binding reads the new
                // value, so an assigned activity stays visible even when it does not match the
                // current filter text (the filter keeps already-assigned activities visible).
                _mwfChoicesChanged?.Invoke();

                RebuildArgumentMappings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(MwfDescription));
                OnPropertyChanged(nameof(MwfArguments));
                OnPropertyChanged(nameof(MwfArgumentsText));
                OnPropertyChanged(nameof(IsMapped));
                OnPropertyChanged(nameof(ActionLabel));
            }
        }

        /// <summary>Column 4 — description of the selected MWF activity.</summary>
        public string MwfDescription
        {
            get
            {
                if (string.IsNullOrEmpty(MwfFullName))
                    return string.Empty;
                return _mwfDescriptors.TryGetValue(MwfFullName, out var descriptor)
                    ? descriptor.Description ?? string.Empty
                    : string.Empty;
            }
        }

        /// <summary>True when this row has a MWF activity assigned.</summary>
        public bool IsMapped => !string.IsNullOrWhiteSpace(MwfFullName);

        /// <summary>Label of the row action button: "Unmap" for mapped rows, "Assign" for unmapped rows.</summary>
        public string ActionLabel => IsMapped ? "Unmap" : "Assign";

        /// <summary>Drives the ComboBox drop-down so the "Assign" button opens the picker.</summary>
        public bool IsPickerOpen
        {
            get => _isPickerOpen;
            set
            {
                if (_isPickerOpen != value)
                {
                    _isPickerOpen = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>True while the row's argument details panel is expanded (toggled by the row's Details button).</summary>
        public bool IsDetailsOpen
        {
            get => _isDetailsOpen;
            set
            {
                if (_isDetailsOpen != value)
                {
                    _isDetailsOpen = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DetailsLabel));
                }
            }
        }

        /// <summary>Label of the row-details toggle button.</summary>
        public string DetailsLabel => IsDetailsOpen ? "Hide" : "Args";

        /// <summary>Multi-line rendering of the PIX activity arguments, or a placeholder when empty.</summary>
        public string PixArgumentsText =>
            PixArguments.Count == 0
                ? "No arguments."
                : string.Join(Environment.NewLine, PixArguments.Select(a => a.Display));

        /// <summary>Multi-line rendering of the MWF activity arguments, or a placeholder when empty.</summary>
        public string MwfArgumentsText =>
            MwfArguments.Count == 0
                ? "No arguments."
                : string.Join(Environment.NewLine, MwfArguments.Select(a => a.Display));

        /// <summary>Argument-level mapping rows shown when the row details is in mapping mode.</summary>
        public ObservableCollection<ArgumentMappingRowVm> ArgumentMappings => _argumentMappings;

        /// <summary>True while the row details shows the argument mapping editor instead of the plain argument lists.</summary>
        public bool IsArgumentMappingOpen
        {
            get => _isArgumentMappingOpen;
            set
            {
                if (_isArgumentMappingOpen != value)
                {
                    _isArgumentMappingOpen = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ArgumentMappingToggleLabel));
                    OnPropertyChanged(nameof(ArgumentsListVisibility));
                    OnPropertyChanged(nameof(ArgumentMappingVisibility));
                }
            }
        }

        /// <summary>Label of the argument-mapping toggle button.</summary>
        public string ArgumentMappingToggleLabel => IsArgumentMappingOpen ? "Back to list" : "Map arguments";

        /// <summary>Visibility of the plain argument list panel.</summary>
        public Visibility ArgumentsListVisibility => IsArgumentMappingOpen ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Visibility of the argument mapping editor panel.</summary>
        public Visibility ArgumentMappingVisibility => IsArgumentMappingOpen ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Auto-matches unmapped PIX arguments to MWF arguments by name, then by type.</summary>
        public void AutoMatchArguments()
        {
            foreach (var vm in _argumentMappings)
            {
                if (!string.IsNullOrEmpty(vm.MwfArgument))
                    continue;

                var match = vm.MwfArguments.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.Name) && string.Equals(a.Name, vm.PixArgument, StringComparison.OrdinalIgnoreCase))
                    ?? vm.MwfArguments.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.Name) && string.Equals(a.Type, vm.PixType, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    vm.MwfArgument = match.Name;
            }
        }

        private void RebuildArgumentMappings()
        {
            _argumentMappings.Clear();
            var stored = _store.GetArgumentMappings(PixFullName);
            var mwfArguments = MwfArguments;

            foreach (var pix in PixArguments)
            {
                var existing = stored.FirstOrDefault(m =>
                    string.Equals(m.PixArgument, pix.Name, StringComparison.OrdinalIgnoreCase));

                ArgumentMappingRowVm? vm = null;
                vm = new ArgumentMappingRowVm(pix, mwfArguments, existing, () => PersistArgumentMapping(vm!));
                _argumentMappings.Add(vm);
            }
        }

        private void PersistArgumentMapping(ArgumentMappingRowVm vm)
        {
            _store.SetArgumentMapping(
                PixFullName,
                vm.PixArgument,
                string.IsNullOrWhiteSpace(vm.MwfArgument) ? null : vm.MwfArgument,
                vm.Conversion,
                vm.ConversionExpression);
        }

        /// <summary>Applies (or removes) an argument mapping from the AI tool and refreshes the editor rows.</summary>
        public void ApplyArgumentMapping(string pixArgument, string mwfArgument, ConversionKind conversion, string conversionExpression)
        {
            _store.SetArgumentMapping(PixFullName, pixArgument, mwfArgument, conversion, conversionExpression);
            RebuildArgumentMappings();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Display model for a single activity argument: name, .NET type, direction and description.</summary>
    public sealed class ActivityArgumentInfo
    {
        public ActivityArgumentInfo(string name, string type, string direction, string? description = null, string? displayName = null)
        {
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Direction = direction ?? string.Empty;
            Description = description ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Display = string.IsNullOrEmpty(Name)
                ? (string.IsNullOrEmpty(DisplayName) ? "(none)" : DisplayName)
                : Name + " : " + Type + (string.IsNullOrEmpty(Direction) ? string.Empty : "  (" + Direction + ")");
        }

        /// <summary>Argument name.</summary>
        public string Name { get; }

        /// <summary>Argument .NET type.</summary>
        public string Type { get; }

        /// <summary>Data-flow direction: In / Out / InOut.</summary>
        public string Direction { get; }

        /// <summary>Argument description (used by the mapping UI and AI prompts).</summary>
        public string Description { get; }

        /// <summary>Human-readable argument name (used by the mapping UI).</summary>
        public string DisplayName { get; }

        /// <summary>Single-line "name : type (direction)" rendering for the popup.</summary>
        public string Display { get; }

        /// <summary>Maps a PIX parameter target (Expression/Variable/Argument) to a data-flow direction.</summary>
        public static ActivityArgumentInfo FromPix(PixActivityParameter parameter)
        {
            if (parameter == null)
                return new ActivityArgumentInfo(string.Empty, string.Empty, string.Empty);

            var direction = parameter.Target switch
            {
                "Expression" => "In",
                "Variable" => "Out",
                "Argument" => "InOut",
                _ => parameter.Target ?? string.Empty
            };

            return new ActivityArgumentInfo(parameter.Pix, parameter.Type, direction, parameter.Description, parameter.DisplayName);
        }

        /// <summary>Maps a WF property type (InArgument/OutArgument/InOutArgument) to a data-flow direction.</summary>
        public static ActivityArgumentInfo FromMwf(WfActivityParameter parameter)
        {
            if (parameter == null)
                return new ActivityArgumentInfo(string.Empty, string.Empty, string.Empty);

            var type = parameter.Type ?? string.Empty;
            var innerType = type;
            var direction = parameter.Direction ?? string.Empty;

            if (string.IsNullOrEmpty(direction))
            {
                if (ExtractWrappedType(type, "InOutArgument", out var t)) { direction = "InOut"; innerType = t; }
                else if (ExtractWrappedType(type, "OutArgument", out t)) { direction = "Out"; innerType = t; }
                else if (ExtractWrappedType(type, "InArgument", out t)) { direction = "In"; innerType = t; }
            }

            return new ActivityArgumentInfo(parameter.Name, innerType, direction, parameter.Description, parameter.DisplayName);
        }

        private static bool ExtractWrappedType(string type, string wrapperName, out string inner)
        {
            inner = type;
            var idx = type.IndexOf(wrapperName + "<", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            var open = type.IndexOf('<', idx);
            var close = type.LastIndexOf('>');
            if (open < 0 || close <= open)
                return false;

            inner = type.Substring(open + 1, close - open - 1).Trim();
            return true;
        }
    }

    /// <summary>A single choice in the conversion-kind drop-down.</summary>
    public sealed class ConversionOption
    {
        public static readonly IReadOnlyList<ConversionOption> All = new[]
        {
            new ConversionOption(ConversionKind.None, "None"),
            new ConversionOption(ConversionKind.ToString, "ToString"),
            new ConversionOption(ConversionKind.Parse, "Parse"),
            new ConversionOption(ConversionKind.DateTimeFormat, "DateTime"),
            new ConversionOption(ConversionKind.Custom, "Custom")
        };

        public ConversionOption(ConversionKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public ConversionKind Kind { get; }
        public string Label { get; }
    }

    /// <summary>
    /// View model for one PIX argument in the argument-mapping editor: the source PIX
    /// argument plus the selected MWF argument and the optional conversion. Changes are
    /// pushed back to the owning <see cref="PixMwfMappingRow"/> store.
    /// </summary>
    public sealed class ArgumentMappingRowVm : INotifyPropertyChanged
    {
        private readonly Action _onChanged;
        private string _mwfArgument;
        private ConversionKind _conversion;
        private string? _conversionExpression;

        public ArgumentMappingRowVm(
            ActivityArgumentInfo pixArgument,
            IReadOnlyList<ActivityArgumentInfo> mwfArguments,
            PixMwfArgumentMapping existing,
            Action onChanged)
        {
            PixArgument = pixArgument?.Name ?? string.Empty;
            PixType = pixArgument?.Type ?? string.Empty;
            PixDisplay = pixArgument?.Display ?? string.Empty;
            PixDescription = pixArgument?.Description ?? string.Empty;

            var options = new List<ActivityArgumentInfo>
            {
                new ActivityArgumentInfo(string.Empty, string.Empty, string.Empty, null, "(none)")
            };
            options.AddRange(mwfArguments ?? new List<ActivityArgumentInfo>());
            MwfArguments = options;

            _mwfArgument = existing?.MwfArgument ?? string.Empty;
            _conversion = existing?.Conversion ?? ConversionKind.None;
            _conversionExpression = existing?.ConversionExpression;
            _onChanged = onChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Source PIX argument name.</summary>
        public string PixArgument { get; }

        /// <summary>Source PIX argument .NET type (used by auto-match).</summary>
        public string PixType { get; }

        /// <summary>Single-line source argument rendering.</summary>
        public string PixDisplay { get; }

        /// <summary>Source argument description (tooltip).</summary>
        public string PixDescription { get; }

        /// <summary>Selectable MWF arguments (including a "(none)" entry).</summary>
        public IReadOnlyList<ActivityArgumentInfo> MwfArguments { get; }

        /// <summary>Conversion choices shown in the drop-down.</summary>
        public IReadOnlyList<ConversionOption> ConversionOptions => ConversionOption.All;

        /// <summary>Selected MWF argument name (empty = unmapped).</summary>
        public string MwfArgument
        {
            get => _mwfArgument;
            set
            {
                if (string.Equals(_mwfArgument, value ?? string.Empty, StringComparison.Ordinal))
                    return;
                _mwfArgument = value ?? string.Empty;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        /// <summary>Selected conversion kind.</summary>
        public ConversionKind Conversion
        {
            get => _conversion;
            set
            {
                if (_conversion == value)
                    return;
                _conversion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExpressionEnabled));
                _onChanged?.Invoke();
            }
        }

        /// <summary>Format string or custom C# expression template (<c>{0}</c> = source value).</summary>
        public string? ConversionExpression
        {
            get => _conversionExpression;
            set
            {
                if (string.Equals(_conversionExpression, value, StringComparison.Ordinal))
                    return;
                _conversionExpression = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }

        /// <summary>True when the expression/format text box is relevant for the selected conversion.</summary>
        public bool IsExpressionEnabled =>
            _conversion == ConversionKind.Parse ||
            _conversion == ConversionKind.DateTimeFormat ||
            _conversion == ConversionKind.Custom;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}