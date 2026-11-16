using System.Collections.Generic;
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
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, IReadOnlyList<string> allowedReadPaths = null)
        {
            if (mode == AgentMode.Help)
                return BuildHelpSupportPrompt();

            string prompt;
            if (toolCallingMode == ToolCallingMode.Native)
            {
                prompt = mode == AgentMode.Act
                    ? PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-act-system-native.txt", PromptDefaults.AgentActSystemPromptNative)
                    : PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-plan-system-native.txt", PromptDefaults.AgentPlanSystemPromptNative);

                prompt = prompt.Replace("{tools}", FormatToolList(registry, mode, allowed, includeParameters: false));
            }
            else
            {
                var basePrompt = PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-json-base.txt", PromptDefaults.AgentJsonBaseSystemPrompt);
                var modeInstructions = mode == AgentMode.Act
                    ? PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-act-mode-instructions.txt", PromptDefaults.AgentActModeInstructions)
                    : PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-plan-mode-instructions.txt", PromptDefaults.AgentPlanModeInstructions);

                var toolList = FormatToolList(registry, mode, allowed, includeParameters: true);
                prompt = basePrompt.Replace("{tools}", toolList).Replace("{mode_instructions}", modeInstructions);
            }

            var roslynNavigation = registry.Find("code_outline") != null ? PromptDefaults.RoslynNavigationHint : "";
            return prompt
                .Replace("{allowed_read_paths}", FormatAllowedReadPaths(allowedReadPaths))
                .Replace("{roslyn_navigation}", roslynNavigation);
        }

        /// <summary>
        /// Builds the prompt for a mode, protocol, permission set and optional user addressing.
        /// When <paramref name="addressByName"/> is true and <paramref name="userName"/> is not
        /// blank, a short instruction to address the user by name is appended to the prompt.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string userName, bool addressByName)
        {
            return Build(mode, registry, toolCallingMode, allowed, userName, addressByName, MemoryBankMode.Tool);
        }

        /// <summary>
        /// Builds the prompt for a mode, protocol, permission set, optional user addressing
        /// and an optional memory-bank convention.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string userName, bool addressByName, MemoryBankMode memoryBankMode, IReadOnlyList<string> allowedReadPaths = null)
        {
            var prompt = Build(mode, registry, toolCallingMode, allowed, allowedReadPaths);
            var name = (userName ?? string.Empty).Trim();
            if (addressByName && name.Length > 0)
                prompt = prompt + "\n\nAddress the user by their name when replying: \"" + name + "\".";

            if (memoryBankMode == MemoryBankMode.Convention)
                prompt = prompt + "\n\n" + PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "memory-bank-convention.txt", PromptDefaults.MemoryBankConventionInstructions);

            return prompt;
        }

        /// <summary>Formats the tools available to the model for the given mode and permissions.</summary>
        private static string FormatToolList(AgentToolRegistry registry, AgentMode mode, AgentPermissions allowed, bool includeParameters)
        {
            var sb = new StringBuilder();
            foreach (var t in registry.ForMode(mode, allowed))
            {
                sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
                if (includeParameters)
                    sb.Append(" Parameters: ").AppendLine(t.ParametersSchema);
                else
                    sb.AppendLine();
            }

            var toolList = sb.ToString();
            return string.IsNullOrWhiteSpace(toolList) ? "(none)" : toolList;
        }

        /// <summary>Formats the allowed read paths into a bullet list for the prompt (empty list → "(none)").</summary>
        private static string FormatAllowedReadPaths(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return "(none)";

            var sb = new StringBuilder();
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                sb.Append("- ").AppendLine(path);
            }

            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? "(none)" : result;
        }

        /// <summary>Builds the Help-mode support prompt: the support persona plus the bundled user documentation.</summary>
        private static string BuildHelpSupportPrompt()
        {
            var template = PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "help-support.txt", PromptDefaults.HelpSupportPrompt);
            var docs = HelpDocumentation.ReadAll();
            var body = string.IsNullOrWhiteSpace(docs)
                ? "Документация временно недоступна. Вежливо объясни пользователю, как пользоваться расширением RPA Writer, на основе общих знаний."
                : docs;

            return template.Replace("{docs}", body);
        }
    }
}
