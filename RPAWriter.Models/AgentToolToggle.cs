using System.Collections.ObjectModel;
using RPAWriter.Mvvm;

namespace RPAWriter.Models
{
    /// <summary>
    /// One toggleable RPA-domain agent tool shown on the Settings → RPA tab.
    /// The name matches the real tool name registered by the agent tool factory.
    /// </summary>
    public sealed class AgentToolToggleItem : BaseModel
    {
        public AgentToolToggleItem(string name, string group, string description, bool mutates, bool isEnabled)
        {
            Name = name;
            Group = group;
            Description = description;
            Mutates = mutates;
            _isEnabled = isEnabled;
        }

        public string Name { get; }
        public string Group { get; }
        public string Description { get; }
        public bool Mutates { get; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }

    /// <summary>A named group of <see cref="AgentToolToggleItem"/> entries (RPA / Spec / KB / RPA convert).</summary>
    public sealed class AgentToolToggleGroup
    {
        public AgentToolToggleGroup(string name)
        {
            Name = name;
            Items = new ObservableCollection<AgentToolToggleItem>();
        }

        public string Name { get; }
        public ObservableCollection<AgentToolToggleItem> Items { get; }
    }
}
