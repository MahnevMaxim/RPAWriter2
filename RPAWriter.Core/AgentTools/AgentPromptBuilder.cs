using System.Text;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Builds the agent system prompt for a mode from the base prompt + tool list.</summary>
    public static class AgentPromptBuilder
    {
        /// <summary>Builds the JSON-over-chat system prompt (backward-compatible default).</summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry)
        {
            return Build(mode, registry, ToolCallingMode.JsonChat, AgentPermissions.All);
        }

        /// <summary>
        /// Builds the agent system prompt for a mode and tool-calling protocol.
        /// Native mode uses a prompt without the JSON tool protocol (tools are provided
        /// as native function definitions); JSON mode injects the tool list into {tools}.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode)
        {
            return Build(mode, registry, toolCallingMode, AgentPermissions.All);
        }

        /// <summary>Builds the prompt for a mode, protocol and allowed permission set.</summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed)
        {
            if (toolCallingMode == ToolCallingMode.Native)
            {
                return mode == AgentMode.Act
                    ? PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-act-system-native.txt", PromptDefaults.AgentActSystemPromptNative)
                    : PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-plan-system-native.txt", PromptDefaults.AgentPlanSystemPromptNative);
            }

            var basePrompt = PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-json-base.txt", PromptDefaults.AgentJsonBaseSystemPrompt);
            var modeInstructions = mode == AgentMode.Act
                ? PromptDefaults.AgentActModeInstructions
                : PromptDefaults.AgentPlanModeInstructions;

            var sb = new StringBuilder();
            foreach (var t in registry.ForMode(mode, allowed))
            {
                sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
                sb.Append(" Parameters: ").AppendLine(t.ParametersSchema);
            }

            var toolList = sb.ToString();
            if (string.IsNullOrWhiteSpace(toolList))
                toolList = "(none)";

            return basePrompt.Replace("{tools}", toolList).Replace("{mode_instructions}", modeInstructions);
        }

        /// <summary>
        /// Builds the prompt for a mode, protocol, permission set and optional user addressing.
        /// When <paramref name="addressByName"/> is true and <paramref name="userName"/> is not
        /// blank, a short instruction to address the user by name is appended to the prompt.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string userName, bool addressByName)
        {
            var prompt = Build(mode, registry, toolCallingMode, allowed);
            var name = (userName ?? string.Empty).Trim();
            if (!addressByName || name.Length == 0)
                return prompt;

            return prompt + "\n\nAddress the user by their name when replying: \"" + name + "\".";
        }
    }
}
