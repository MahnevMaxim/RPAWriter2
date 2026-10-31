using System.Collections.Generic;

namespace RPAWriter.Core.Models
{
    /// <summary>
    /// One entry of the "minimum level" filter: a display title plus the level it selects.
    /// Pure data (no WPF types), so the filter list can live in the model and be bound in the view.
    /// </summary>
    public sealed class LogLevelOption
    {
        public LogLevelOption(string title, LogLevel? value)
        {
            Title = title;
            Value = value;
        }

        /// <summary>Text shown in the combo box ("All", "Info"...).</summary>
        public string Title { get; }

        /// <summary>Selected level; <c>null</c> for the "All" entry.</summary>
        public LogLevel? Value { get; }

        /// <summary>Builds the default list: "All" followed by every <see cref="LogLevel"/> in declaration order.</summary>
        public static IReadOnlyList<LogLevelOption> CreateDefault()
        {
            var options = new List<LogLevelOption> { new LogLevelOption("All", null) };
            foreach (LogLevel level in System.Enum.GetValues(typeof(LogLevel)))
                options.Add(new LogLevelOption(level.ToString(), level));
            return options;
        }

        /// <summary>A combo box without a <c>DisplayMemberPath</c> falls back to this.</summary>
        public override string ToString() => Title;
    }
}
