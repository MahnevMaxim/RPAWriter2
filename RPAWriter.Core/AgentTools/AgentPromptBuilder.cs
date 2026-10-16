using System.Text;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Builds the agent system prompt for a mode from the base prompt + tool list.</summary>
    public static class AgentPromptBuilder
    {
        public static string Build(AgentMode mode, AgentToolRegistry registry)
        {
            var basePrompt = mode == AgentMode.Act
                ? PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-act-system.txt", PromptDefaults.AgentActSystemPrompt)
                : PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-plan-system.txt", PromptDefaults.AgentPlanSystemPrompt);

            var sb = new StringBuilder();
            foreach (var t in registry.ForMode(mode))
            {
                sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
                sb.Append(" Parameters: ").AppendLine(t.ParametersSchema);
            }

            var toolList = sb.ToString();
            if (string.IsNullOrWhiteSpace(toolList))
                toolList = "(none)";

            return basePrompt.Replace("{tools}", toolList);
        }
    }
}
