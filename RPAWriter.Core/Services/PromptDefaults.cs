namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Built-in fallback prompt templates used when the deployed prompt files are missing
    /// (e.g. during unit tests). Kept identical to the shipped .txt files so behaviour
    /// is unchanged when files are absent.
    /// </summary>
    public static class PromptDefaults
    {
        public const string SystemPrompt =
            "You are an RPA robot development agent for Windows Workflow Foundation. " +
            "Always respond with strictly valid JSON and no prose.";

        public const string ChatSystemPrompt =
            "You are an RPA robot development assistant for Windows Workflow Foundation. " +
            "Always answer in Russian. " +
            "Answer concisely and to the point in plain text, without JSON, unless explicitly requested.";

        public const string AnalyzeSpec =
            "Analyze the technical specification and return JSON of the form " +
            "{\"summary\":\"...\",\"tasks\":[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}]}.\n\n" +
            "Spec:\n{spec}";

        public const string GenerateRobot =
            "Construct a workflow plan. Return JSON of the form {\"name\":\"...\",\"steps\":[" +
            "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"...\"}," +
            "{\"kind\":\"Assign\",\"variable\":\"...\",\"expression\":\"...\"}]}. " +
            "kind: Sequence|InvokeMethod|Assign|WriteLine|Delay. Source analysis: {analysis}";

        public const string DecomposeTasks =
            "Decompose the document into small atomic tasks. Return a JSON array: " +
            "[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}]. " +
            "Write the action, target and description of each task in Russian.\n\n" +
            "Document:\n{document}";

        public const string ReconstructSpec =
            "Reconstruct the technical specification of an RPA robot from its source code. " +
            "Start from the entry point (main.xaml) and follow the files it invokes; the provided list is the reachable set. " +
            "Return JSON of the form {\"name\":\"...\",\"description\":\"...\",\"steps\":[\"...\"],\"inputs\":[\"...\"],\"outputs\":[\"...\"]}. " +
            "Write the description, steps, inputs and outputs in Russian.\n\n" +
            "Robot name: {robotName}\n\nRobot files (relativePath + content):\n{files}";

        public const string MapTaskToSolution =
            "Locate the solution for the task in the robot source files. Return JSON " +
            "{\"xamlFragment\":\"...\",\"csharpCode\":\"...\",\"solutionPaths\":[\"relative/path/File.xaml\",\"relative/path/File.cs\"]}. " +
            "solutionPaths must list the file(s) (relative path) where the task is implemented.\n\n" +
            "Task: {task}\n\nRobot files (relativePath + content):\n{files}";

        public const string AgentPlanSystemPrompt =
            "You are an RPA robot development agent for Windows Workflow Foundation, running in PLAN mode.\n" +
            "In PLAN mode you must only read, analyze and plan. Never modify files, never create robots, never run builds, never import or write data.\n" +
            "Always answer in Russian unless the user asks otherwise.\n\n" +
            "Available tools (read-only):\n{tools}\n\n" +
            "To use a tool, output EXACTLY one JSON object and nothing else:\n" +
            "{\"action\":\"tool_call\",\"tool\":\"<tool name>\",\"arguments\":{...}}\n\n" +
            "To give the final answer, output EXACTLY one JSON object and nothing else:\n" +
            "{\"action\":\"final\",\"text\":\"<your answer in Russian>\"}\n\n" +
            "You may call several tools one after another, then finish with a final answer.";

        public const string AgentActSystemPrompt =
            "You are an RPA robot development agent for Windows Workflow Foundation, running in ACT mode.\n" +
            "In ACT mode you can read, analyze, plan AND perform real changes: write files, create robots, run builds, import the knowledge base.\n" +
            "Always answer in Russian unless the user asks otherwise.\n\n" +
            "Available tools:\n{tools}\n\n" +
            "To use a tool, output EXACTLY one JSON object and nothing else:\n" +
            "{\"action\":\"tool_call\",\"tool\":\"<tool name>\",\"arguments\":{...}}\n\n" +
            "To give the final answer, output EXACTLY one JSON object and nothing else:\n" +
            "{\"action\":\"final\",\"text\":\"<your answer in Russian>\"}\n\n" +
            "You may call several tools one after another, then finish with a final answer.";
    }
}
