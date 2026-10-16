namespace RPAWriter.Core.Services
{
    /// <summary>
    /// In-memory cache of all prompt templates loaded from the deployed "Prompts"
    /// directory. Reloaded from disk by the provider's "refresh prompts" action.
    /// </summary>
    public sealed class PromptSet
    {
        public string SystemPrompt { get; set; }
        public string ChatSystemPrompt { get; set; }
        public string AnalyzeSpec { get; set; }
        public string GenerateRobot { get; set; }
        public string DecomposeTasks { get; set; }
        public string ReconstructSpec { get; set; }
        public string MapTaskToSolution { get; set; }
        public string AgentPlanSystemPrompt { get; set; }
        public string AgentActSystemPrompt { get; set; }

        /// <summary>Loads every prompt template from <paramref name="promptsPath"/> with built-in fallbacks.</summary>
        public static PromptSet Load(string promptsPath)
        {
            return new PromptSet
            {
                SystemPrompt = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.System, "agent-system.txt", PromptDefaults.SystemPrompt),
                ChatSystemPrompt = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.System, "chat-system.txt", PromptDefaults.ChatSystemPrompt),
                AnalyzeSpec = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "analyze-spec.txt", PromptDefaults.AnalyzeSpec),
                GenerateRobot = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "generate-robot.txt", PromptDefaults.GenerateRobot),
                DecomposeTasks = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "decompose-tasks.txt", PromptDefaults.DecomposeTasks),
                ReconstructSpec = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "reconstruct-spec.txt", PromptDefaults.ReconstructSpec),
                MapTaskToSolution = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "map-task-to-solution.txt", PromptDefaults.MapTaskToSolution),
                AgentPlanSystemPrompt = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.System, "agent-plan-system.txt", PromptDefaults.AgentPlanSystemPrompt),
                AgentActSystemPrompt = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.System, "agent-act-system.txt", PromptDefaults.AgentActSystemPrompt)
            };
        }
    }
}
