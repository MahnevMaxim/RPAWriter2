using System;
using System.IO;
using Newtonsoft.Json;

namespace RPAWriter2.Services
{
    /// <summary>Per-session UI state persisted between Visual Studio sessions.</summary>
    public class UiStateData
    {
        /// <summary>Chat tab view: "Conversation" or "History".</summary>
        public string ChatViewMode { get; set; } = "Conversation";

        /// <summary>Id of the active conversation, or empty when none.</summary>
        public string LastConversationId { get; set; } = "";

        /// <summary>Agent mode: "Plan" or "Act".</summary>
        public string AgentMode { get; set; } = "Plan";

        /// <summary>Header of the active main tab (e.g. "Chat", "Settings").</summary>
        public string ActiveTab { get; set; } = "Chat";
    }

    /// <summary>Loads/saves the UI state JSON at %AppData%\RPAWriter2\ui_state.json.</summary>
    public static class UiStateStore
    {
        public static string StateFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2", "ui_state.json");

        public static UiStateData Load()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    var json = File.ReadAllText(StateFile);
                    var state = JsonConvert.DeserializeObject<UiStateData>(json) ?? new UiStateData();
                    EnsureDefaults(state);
                    return state;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            var defaults = new UiStateData();
            EnsureDefaults(defaults);
            return defaults;
        }

        public static void Save(UiStateData state)
        {
            if (state == null) return;
            try
            {
                var directory = Path.GetDirectoryName(StateFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }

        private static void EnsureDefaults(UiStateData state)
        {
            if (state == null) return;
            if (!string.Equals(state.ChatViewMode, "History", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state.ChatViewMode, "Conversation", StringComparison.OrdinalIgnoreCase))
                state.ChatViewMode = "Conversation";
            if (state.LastConversationId == null)
                state.LastConversationId = "";
            if (!string.Equals(state.AgentMode, "Act", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state.AgentMode, "Plan", StringComparison.OrdinalIgnoreCase))
                state.AgentMode = "Plan";
            if (string.IsNullOrWhiteSpace(state.ActiveTab))
                state.ActiveTab = "Chat";
        }
    }
}
