using System;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Converts <see cref="AgentMode"/> to and from the string the UI shows in the mode toggle
    /// ("Plan", "Act", "Help").
    /// <para>
    /// Both directions live in Core because they are used by three call sites that must agree:
    /// the XAML converter behind the toggle binding, the persisted <see cref="UiStateData.AgentMode"/>
    /// and the "Mode: Act" transcript entry. Keeping the rules here makes them testable without
    /// a view (<c>AgentModeNamesTests</c>) and gives the converter a single fallback: an unknown
    /// or not-yet-initialized value is Plan, never an exception.
    /// </para>
    /// </summary>
    public static class AgentModeNames
    {
        /// <summary>Toggle value of <see cref="AgentMode.Plan"/>.</summary>
        public const string Plan = "Plan";

        /// <summary>Toggle value of <see cref="AgentMode.Act"/>.</summary>
        public const string Act = "Act";

        /// <summary>Toggle value of <see cref="AgentMode.Help"/>.</summary>
        public const string Help = "Help";

        /// <summary>Display name of the mode; <see cref="AgentMode.Plan"/> for unknown values.</summary>
        public static string ToValue(AgentMode mode)
        {
            switch (mode)
            {
                case AgentMode.Act:
                    return Act;
                case AgentMode.Help:
                    return Help;
                default:
                    return Plan;
            }
        }

        /// <summary>
        /// Parses a toggle value (case-insensitive). Unknown, empty or <c>null</c> input is
        /// <see cref="AgentMode.Plan"/>; the caller never has to guard the binding.
        /// </summary>
        public static AgentMode Parse(object value)
        {
            var text = value as string ?? value?.ToString();

            if (string.Equals(text, Act, StringComparison.OrdinalIgnoreCase))
                return AgentMode.Act;
            if (string.Equals(text, Help, StringComparison.OrdinalIgnoreCase))
                return AgentMode.Help;
            return AgentMode.Plan;
        }
    }
}
