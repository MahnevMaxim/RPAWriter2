using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
        private readonly IReadOnlyDictionary<string, string> _mwfDescriptions;
        private string _mwfFullName;
        private bool _isPickerOpen;

        public PixMwfMappingRow(
            int index,
            string pixFullName,
            string pixDisplayName,
            string pixDescription,
            string mwfFullName,
            PixMwfMappingStore store,
            IReadOnlyDictionary<string, string> mwfDescriptions)
        {
            Index = index;
            PixFullName = pixFullName ?? string.Empty;
            PixDisplayName = pixDisplayName ?? string.Empty;
            PixDescription = pixDescription ?? string.Empty;
            _mwfFullName = string.IsNullOrWhiteSpace(mwfFullName) ? null : mwfFullName.Trim();
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _mwfDescriptions = mwfDescriptions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(MwfDescription));
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
                return _mwfDescriptions.TryGetValue(MwfFullName, out var description) ? description : string.Empty;
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

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
