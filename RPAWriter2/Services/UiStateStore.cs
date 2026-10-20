using System;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>Chat tab view: the active conversation or the history list.</summary>
    public enum ChatViewMode
    {
        /// <summary>The active conversation is shown.</summary>
        Conversation,

        /// <summary>The list of saved conversations is shown.</summary>
        History
    }

    /// <summary>Main tabs of the RPA Writer window.</summary>
    public enum MainTab
    {
        /// <summary>Agent chat.</summary>
        Chat,

        /// <summary>Specification import/parsing.</summary>
        Spec,

        /// <summary>Knowledge base.</summary>
        [EnumMember(Value = "Knowledge base")]
        KnowledgeBase,

        /// <summary>Extension settings.</summary>
        Settings,

        /// <summary>Package gallery.</summary>
        Packages,

        /// <summary>Hidden controls gallery (debug).</summary>
        Controls
    }

    /// <summary>Per-session UI state persisted between Visual Studio sessions.</summary>
    public class UiStateData
    {
        /// <summary>Chat tab view: Conversation or History.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ChatViewMode ChatViewMode { get; set; }

        /// <summary>Id of the active conversation, or empty when none.</summary>
        public string LastConversationId { get; set; } = "";

        /// <summary>Agent mode: Plan or Act.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public AgentMode AgentMode { get; set; }

        /// <summary>Active main tab.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public MainTab ActiveTab { get; set; }
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
