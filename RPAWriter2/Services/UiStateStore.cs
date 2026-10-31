using System;
using System.IO;
using Newtonsoft.Json;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>Loads/saves the UI state JSON at %AppData%\RPAWriter2\&lt;version&gt;\ui_state.json.</summary>
    public static class UiStateStore
    {
        public static string StateFile => AppDataPaths.Resolve("ui_state.json");

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

            if (state.ChatViewMode != ChatViewMode.Conversation && state.ChatViewMode != ChatViewMode.History)
                state.ChatViewMode = ChatViewMode.Conversation;

            if (state.LastConversationId == null)
                state.LastConversationId = "";

            if (state.AgentMode != AgentMode.Plan && state.AgentMode != AgentMode.Act && state.AgentMode != AgentMode.Help)
                state.AgentMode = AgentMode.Plan;

            if (!Enum.IsDefined(typeof(MainTab), state.ActiveTab))
                state.ActiveTab = MainTab.Chat;
        }
    }
}
