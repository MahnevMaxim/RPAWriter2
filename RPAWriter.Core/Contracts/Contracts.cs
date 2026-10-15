using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// AI agent: spec analysis, task decomposition, task-to-solution mapping,
    /// and robot plan generation. Backed by an OpenAI-compatible provider or a stub.
    /// </summary>
    public interface IAiProvider
    {
        Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default);
        Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default);
        Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, string robotCode, CancellationToken cancellationToken = default);
        /// <summary>Free-form chat with the agent (UI-facing). Returns the answer and, when the provider emits it, the model's "reasoning".</summary>
        Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads specification documents from Word/Excel/plain text.
    /// </summary>
    public interface IDocumentReader
    {
        SpecModel Read(string path);
    }

    /// <summary>
    /// Knowledge base: persists solutions and retrieves similar tasks.
    /// </summary>
    public interface IKnowledgeBase
    {
        Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default);
        Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Access to local logs (files, ActivityLog, Event Log).
    /// </summary>
    public interface ILogAccessService
    {
        Task<IReadOnlyList<string>> ReadLocalLogsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Downloads and analyzes remote robot logs.
    /// </summary>
    public interface IRemoteLogService
    {
        Task<IReadOnlyList<string>> DownloadAsync(RobotDeploymentConfig config, CancellationToken cancellationToken = default);
        Task<LogAnalysis> AnalyzeAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One-click Release build, plus reading compiler diagnostics and the VS output pane.
    /// </summary>
    public interface IBuildFeedbackService
    {
        Task BuildReleaseAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default);
        Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Generates robot artifacts: XAML (standard WF activities) and C# code
    /// invoked through InvokeMethod/Assign.
    /// </summary>
    public interface IRobotGenerator
    {
        string EmitWorkflowXaml(RobotPlan plan);
        string EmitHelperCode(RobotPlan plan);
    }

    /// <summary>
    /// Converts a UiPath robot (XAML) into plain Workflow Foundation.
    /// </summary>
    public interface IUiPathConverter
    {
        UiPathConversionResult Convert(string uipathXaml);
    }
}
