using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Context;
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
        Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf);
        Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf);
        /// <summary>Free-form chat with the agent (UI-facing). Returns the answer and, when the provider emits it, the model's "reasoning".</summary>
        Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Multi-turn chat with the agent: carries the full message history (user/assistant/tool)
        /// and an explicit system prompt. Used by the agentic tool loop.
        /// </summary>
        Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Multi-turn chat that also passes the available tools. In native tool-calling mode the
        /// provider emits a <c>tools</c> array and returns <see cref="ChatResponse.ToolCalls"/>;
        /// in JSON-over-chat mode the tools are ignored and the model returns JSON text instead.
        /// </summary>
        Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional capability: streams the model's response (reasoning and content) as
    /// incremental deltas instead of returning only the completed response.
    /// Providers that cannot stream fall back to
    /// <see cref="IAiProvider.ChatAsync(IReadOnlyList{ChatMessageData}, string, CancellationToken)"/>.
    /// </summary>
    public interface IStreamingAiProvider
    {
        Task<ChatResponse> ChatStreamingAsync(
            IReadOnlyList<ChatMessageData> messages,
            string systemPrompt,
            IProgress<ChatStreamDelta> progress,
            CancellationToken cancellationToken = default);

        /// <summary>Streaming variant that also passes the available tools (native tool calling).</summary>
        Task<ChatResponse> ChatStreamingAsync(
            IReadOnlyList<ChatMessageData> messages,
            string systemPrompt,
            IReadOnlyList<ToolDefinition> tools,
            IProgress<ChatStreamDelta> progress,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional capability for AI providers that can report token usage
    /// relative to the model's context window.
    /// </summary>
    public interface ITokenUsageProvider
    {
        TokenUsage TotalTokenUsage { get; }
        int ContextWindowSize { get; }
        event EventHandler<TokenUsage> TokenUsageUpdated;
    }

    /// <summary>
    /// Optional capability for AI providers that report each outgoing message
    /// right before it is sent to the model.
    /// </summary>
    public interface IMessageNotifier
    {
        event EventHandler<AiMessageEventArgs> MessageSent;
    }

    /// <summary>
    /// Optional capability for AI providers that report the full assembled request
    /// (system prompt + all messages) right before it is sent to the model.
    /// </summary>
    public interface IPromptNotifier
    {
        event EventHandler<PromptAssembledEventArgs> PromptAssembled;
    }

    /// <summary>
    /// Optional capability for AI providers that report every retry attempt,
    /// including the provider's status code and response body.
    /// </summary>
    public interface IRetryNotifier
    {
        event EventHandler<RetryAttemptEventArgs> RetryAttempt;
    }

    /// <summary>
    /// Optional capability for AI providers that load prompt templates from disk
    /// and can re-read them on demand (e.g. after the files were edited).
    /// </summary>
    public interface IPromptReloadable
    {
        void ReloadPromptsFromDisk();
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
        Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default);
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
    /// Third-party production-log provider. Supplied by an external library that
    /// implements this interface; the extension discovers it at runtime.
    /// </summary>
    public interface IProdLogProvider
    {
        /// <summary>Human-readable provider name shown in the UI (e.g. "Orchestrator logs").</summary>
        string Name { get; }

        /// <summary>Returns the completed tasks executed by the given robot (e.g. "RPA-1212").</summary>
        Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, CancellationToken cancellationToken = default);

        /// <summary>Returns the log entries (objects) of the selected task.</summary>
        Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, CancellationToken cancellationToken = default);
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
    }

    /// <summary>
    /// One-click build using a given solution configuration, plus reading compiler diagnostics and the VS output pane.
    /// </summary>
    public interface IBuildFeedbackService
    {
        Task BuildAsync(string configuration, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Raised by providers instead of a bare transport exception, so the agent
    /// loop can react to the failure class (e.g. compact and retry on context overflow).
    /// </summary>
    public class ProviderErrorException : Exception
    {
        public ProviderErrorException(ProviderErrorKind kind, string message, int? statusCode = null, string body = null, Exception innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
            StatusCode = statusCode;
            Body = body;
        }

        /// <summary>Classified failure kind.</summary>
        public ProviderErrorKind Kind { get; }

        /// <summary>HTTP status code returned by the provider, or null when no response was received.</summary>
        public int? StatusCode { get; }

        /// <summary>Response body returned by the provider (may be truncated), or null.</summary>
        public string Body { get; }
    }

    /// <summary>Estimates the number of tokens an outgoing request will consume.</summary>
    public interface ITokenEstimator
    {
        int Estimate(string systemPrompt, IReadOnlyList<ChatMessageData> messages);
    }

    /// <summary>
    /// Condenses a conversation transcript so it fits the context budget: summarization
    /// of older messages with a fallback to rule-based truncation.
    /// </summary>
    public interface IContextCondenser
    {
        Task<IReadOnlyList<ChatMessageData>> CondenseAsync(
            IReadOnlyList<ChatMessageData> transcript,
            ContextBudget budget,
            CancellationToken cancellationToken);
    }
}
