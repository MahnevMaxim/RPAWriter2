using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RPAWriter.Core.Models
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

    /// <summary>
    /// Per-session UI state persisted between Visual Studio sessions. Data only: the store
    /// (see <c>IUiStateStore</c>) decides where the file lives and how it is serialized.
    /// </summary>
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
}
