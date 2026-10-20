using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RPAWriter2.Controls
{
    /// <summary>One segment of a <see cref="SegmentedToggle"/> control.</summary>
    public sealed class SegmentedToggleItem : INotifyPropertyChanged
    {
        private string _label = "";
        private object? _value;
        private Brush _accent = Brushes.Transparent;
        private bool _showSeparator = true;

        /// <summary>Text displayed inside the segment.</summary>
        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; OnPropertyChanged(); } }
        }

        /// <summary>Value selected through <see cref="SegmentedToggle.SelectedValue"/>.</summary>
        public object? Value
        {
            get => _value;
            set { if (!Equals(_value, value)) { _value = value; OnPropertyChanged(); } }
        }

        /// <summary>Background brush of the segment while it is selected.</summary>
        public Brush Accent
        {
            get => _accent;
            set { if (!Equals(_accent, value)) { _accent = value; OnPropertyChanged(); } }
        }

        /// <summary>Whether the vertical separator after this segment is visible.</summary>
        public bool ShowSeparator
        {
            get => _showSeparator;
            set { if (_showSeparator != value) { _showSeparator = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
