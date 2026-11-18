using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Rpa.Models;

namespace Agent.Rpa.Contracts
{
    /// <summary>Access to local logs (files, ActivityLog, Event Log).</summary>
    public interface ILogAccessService
    {
        Task<IReadOnlyList<string>> ReadLocalLogsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    }

    /// <summary>Downloads and analyzes remote robot logs.</summary>
    public interface IRemoteLogService
    {
        Task<IReadOnlyList<string>> DownloadAsync(RobotDeploymentConfig config, CancellationToken cancellationToken = default);
        Task<LogAnalysis> AnalyzeAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Third-party production-log provider. Supplied by an external library that
    /// implements this interface; the extension discovers it at runtime.
    /// </summary>
    public interface IProdLogProvider
    {
        /// <summary>Human-readable provider name shown in the UI (e.g. "Orchestrator logs").</summary>
        string Name { get; }

        /// <summary>
        /// Returns the completed tasks executed by the given robot (e.g. "RPA-1212"),
        /// filtered by the supplied query (run date range). The provider applies the filter.
        /// </summary>
        Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, ProdLogQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the log entries (objects) of the selected task, filtered by the supplied
        /// query (minimum severity). The provider applies the filter.
        /// </summary>
        Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, ProdLogQuery query, CancellationToken cancellationToken = default);
    }

    /// <summary>One structured log entry supplied by a production-log provider.</summary>
    public interface IProdLog
    {
        DateTime? Timestamp { get; }
        LogLevel Level { get; }
        string Source { get; }
        string Message { get; }

        /// <summary>Optional original raw text; null/empty when the provider keeps no raw line.</summary>
        string RawLine { get; }

        /// <summary>Optional key/value parameters of the log line; empty when none.</summary>
        IReadOnlyList<LogParameter> Parameters { get; }
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

    /// <summary>Converts a UiPath robot (XAML) into plain Workflow Foundation.</summary>
    public interface IUiPathConverter
    {
        UiPathConversionResult Convert(string uipathXaml);
    }

    /// <summary>
    /// Converts a PIX robot (.pix XML) into plain Workflow Foundation using the
    /// activity library (WF snippets + PIX descriptors).
    /// </summary>
    public interface IPixConverter
    {
        PixConversionResult Convert(string pixXml);
    }

    /// <summary>
    /// Supplies the production-log providers available on this machine.
    /// Injected into the log analysis model so discovery (file system, AppData paths) can be
    /// replaced by a fake in tests.
    /// </summary>
    public interface ILogProviderSource
    {
        /// <summary>Directory configured by the user, or the default Providers folder when none is set.</summary>
        string ResolveDirectory();

        /// <summary>External providers found in the directory; empty when the folder has none.</summary>
        IReadOnlyList<IProdLogProvider> Discover(string directory);

        /// <summary>Offline fallback provider, used only when discovery returns nothing.</summary>
        IProdLogProvider CreateFallback();
    }

    /// <summary>Text currently shown in the Visual Studio Output window (one analysis source of the model).</summary>
    public interface IOutputWindowTextSource
    {
        Task<string> GetTextAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The slice of user settings the log analysis model reads and writes:
    /// the provider folder and the robot-name history.
    /// </summary>
    public interface ILogAnalysisSettings
    {
        /// <summary>User-configured log provider directory (may be empty: the default folder is used).</summary>
        string LogProvidersPath { get; }

        /// <summary>Remembered robot names, most recent first.</summary>
        IReadOnlyList<string> GetRobotNameHistory();

        /// <summary>Stores the robot-name history and persists the settings.</summary>
        void SetRobotNameHistory(IReadOnlyList<string> history);
    }

    /// <summary>
    /// RPA-domain AI capability: spec analysis, task decomposition, task-to-solution
    /// mapping and robot plan generation. Extends the generic <see cref="IAiProvider"/>
    /// (chat), so a single provider instance serves both chat and RPA workflows.
    /// </summary>
    public interface IRpaAiProvider : IAiProvider
    {
        Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default);
        Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default);
        Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf);
        Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf);
    }

    /// <summary>
    /// Optional RPA capability: generates a step-by-step algorithm from a technical
    /// specification and reconciles several independently generated drafts into a final
    /// algorithm plus a list of clarification questions.
    /// </summary>
    public interface IAlgorithmGenerator
    {
        /// <summary>Generates one independent algorithm draft (Markdown) from the spec Markdown.</summary>
        Task<string> GenerateAlgorithmDraftAsync(string specMarkdown, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reconciles the spec and the generated drafts: returns the final algorithm Markdown
        /// and the list of questions about the spec, as separate Markdown strings.
        /// </summary>
        Task<AlgorithmConsolidation> ConsolidateAlgorithmAsync(string specMarkdown, IReadOnlyList<string> drafts, CancellationToken cancellationToken = default);
    }

    /// <summary>Reads specification documents from Word/Excel/plain text.</summary>
    public interface IDocumentReader
    {
        /// <summary>
        /// Reads a specification into a <see cref="SpecModel"/>. When <paramref name="mediaOutputDir"/>
        /// is provided, embedded images are extracted into that directory and referenced in
        /// <see cref="SpecModel.MarkdownText"/> using the directory's own name (e.g. <c>media/image1.png</c>).
        /// </summary>
        SpecModel Read(string path, string mediaOutputDir = null);
    }

    /// <summary>Knowledge base: persists solutions and retrieves similar tasks.</summary>
    public interface IKnowledgeBase
    {
        Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default);
        Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default);
        Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default);
    }
}
