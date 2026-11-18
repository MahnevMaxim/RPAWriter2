using Agent.Core.Services;
using Agent.Rpa.Models;

namespace Agent.Rpa.Services
{
    /// <summary>
    /// In-memory cache of the RPA prompt templates (spec analysis, robot generation,
    /// task decomposition) loaded from the deployed "Prompts" directory.
    /// </summary>
    public sealed class RpaPromptSet
    {
        public string AnalyzeSpec { get; set; }
        public string GenerateRobot { get; set; }
        public string DecomposeTasks { get; set; }
        public string ReconstructSpec { get; set; }
        public string MapTaskToSolution { get; set; }
        public string GenerateAlgorithmDraft { get; set; }
        public string ConsolidateAlgorithm { get; set; }

        public static RpaPromptSet Load(string promptsPath)
        {
            return new RpaPromptSet
            {
                AnalyzeSpec = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "analyze-spec.txt", RpaPromptDefaults.AnalyzeSpec),
                GenerateRobot = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "generate-robot.txt", RpaPromptDefaults.GenerateRobot),
                DecomposeTasks = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "decompose-tasks.txt", RpaPromptDefaults.DecomposeTasks),
                ReconstructSpec = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "reconstruct-spec.txt", RpaPromptDefaults.ReconstructSpec),
                MapTaskToSolution = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "map-task-to-solution.txt", RpaPromptDefaults.MapTaskToSolution),
                GenerateAlgorithmDraft = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "generate-algorithm.txt", RpaPromptDefaults.GenerateAlgorithmDraft),
                ConsolidateAlgorithm = PromptLibrary.Load(promptsPath, PromptLibrary.Categories.Tasks, "consolidate-algorithm.txt", RpaPromptDefaults.ConsolidateAlgorithm)
            };
        }
    }
}
