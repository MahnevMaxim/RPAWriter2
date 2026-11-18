using System;
using System.IO;
using Newtonsoft.Json;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2.Services
{
    /// <summary>Loads/saves the UI state JSON at %AppData%\RPAWriter2\&lt;version&gt;\ui_state.json.</summary>
    public static class UiStateStore
    {
        public static string StateFile => AppDataPaths.ResolveWorkspaceCurrent("ui_state.json");

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
