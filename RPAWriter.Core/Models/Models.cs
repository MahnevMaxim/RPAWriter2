using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RPAWriter.Core.Models
{
    /// <summary>RPA platform of a robot under reverse engineering.</summary>
    public enum RobotPlatform
    {
        /// <summary>Microsoft Workflow Foundation (Visual Studio workflow project).</summary>
        Mwf,
        /// <summary>UiPath (the agent decides details itself).</summary>
        UiPath,
        /// <summary>PIX — a plain set of XAML files.</summary>
        Pix,
        /// <summary>Visual Studio code-only (C#) project.</summary>
        Wr
    }

    /// <summary>Operating mode of the agent chat: read-only planning vs. full action.</summary>
    public enum AgentMode
    {
        /// <summary>Agent reads, analyzes and plans only; mutating tools are not exposed.</summary>
        Plan,
        /// <summary>Agent can perform real changes (write files, create robots, run builds).</summary>
        Act
    }

    /// <summary>How the agent talks to the model about tools.</summary>
    public enum ToolCallingMode
    {
        /// <summary>Model outputs JSON {"action":"final"/"tool_call",...} in plain text.</summary>
        JsonChat,
        /// <summary>Native OpenAI-style tools / tool_calls.</summary>
        Native
    }

    /// <summary>
    /// Kind of a chat entry in the UI transcript. The kind determines the label and the
    /// renderer, mirroring Cline's discriminated message kinds (say/ask) with a typed payload.
    /// </summary>
    public enum ChatMessageKind
    {
        /// <summary>A message typed by the user.</summary>
        User,
        /// <summary>The agent's final answer (markdown).</summary>
        AgentText,
        /// <summary>A tool invocation that was executed.</summary>
        ToolCall,
        /// <summary>A tool invocation that was blocked (e.g. mutating tool in Plan mode).</summary>
        ToolBlocked,
        /// <summary>An extension-side error.</summary>
        Error,
        /// <summary>A system notice (informational).</summary>
        SystemNotice,
        /// <summary>A context compaction notice.</summary>
        Compaction,
        /// <summary>A Plan/Act mode switch notice.</summary>
        ModeSwitch,
        /// <summary>A diff edit produced by apply_fix (reserved for a future renderer).</summary>
        DiffEdit
    }

    /// <summary>How knowledge-base formation should treat a previous run.</summary>
    public enum KnowledgeBaseFormationMode
    {
        /// <summary>Start over: clear previous state and re-form every robot.</summary>
        Override,
        /// <summary>Resume: skip robots already imported by a previous run.</summary>
        Continue
    }

    /// <summary>Class of an AI provider failure, used by the agent loop to recover from context overflow.</summary>
    public enum ProviderErrorKind
    {
        /// <summary>No classification available.</summary>
        None,
        /// <summary>Authentication/authorization failed (HTTP 401/403).</summary>
        Auth,
        /// <summary>Rate limit exceeded (HTTP 429).</summary>
        RateLimit,
        /// <summary>The request exceeded the model's context window.</summary>
        ContextWindowExceeded,
        /// <summary>The response was blocked by a content filter.</summary>
        ContentFilter,
        /// <summary>A transient provider-side error worth retrying.</summary>
        Transient,
        /// <summary>Any other, unclassified failure.</summary>
        Other
    }


    /// <summary>Kind of a typed SAP transaction task.</summary>
    public enum SapActionKind
    {
        /// <summary>Setup / launch of a SAP transaction (настройка и запуск транзакции).</summary>
        Setup,

        /// <summary>Working inside a running transaction (работа с транзакцией).</summary>
        Work,

        /// <summary>Getting a table / data out of the transaction (получение таблицы).</summary>
        GetTable
    }

    /// <summary>Atomic task extracted from a spec: action, target, inputs/outputs, keywords, activity type.</summary>
    public class TaskDescriptor
    {
        public string Action { get; set; }
        public string Target { get; set; }
        public string Description { get; set; }
        public List<string> Inputs { get; set; } = new List<string>();
        public List<string> Outputs { get; set; } = new List<string>();
        public List<string> Keywords { get; set; } = new List<string>();
        public string ActivityType { get; set; }

        /// <summary>SAP transaction code/name (e.g. "VA01", "SE16N"); empty for non-SAP tasks.</summary>
        public string SapTransactionCode { get; set; }

        /// <summary>Action kind of a typed SAP transaction task; null for non-SAP tasks.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SapActionKind? SapActionKind { get; set; }

        /// <summary>True when this task is a typed SAP transaction task.</summary>
        [JsonIgnore]
        public bool IsSapTransactionTask => !string.IsNullOrWhiteSpace(SapTransactionCode);
    }

    /// <summary>Normalized technical specification.</summary>
    public class SpecModel
    {
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public string RawText { get; set; }
        public string MarkdownText { get; set; }
        public string Description { get; set; }
        public List<string> Steps { get; set; } = new List<string>();
        public List<string> Inputs { get; set; } = new List<string>();
        public List<string> Outputs { get; set; } = new List<string>();
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
    }

    /// <summary>Result of the agent's analysis of a spec.</summary>
    public class SpecAnalysis
    {
        public string Summary { get; set; }
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
        public List<SimilarTaskMatch> ReusedSolutions { get; set; } = new List<SimilarTaskMatch>();
    }

    /// <summary>Workflow node kinds (standard WF activities).</summary>
    public enum ActivityNodeKind
    {
        Sequence,
        If,
        While,
        ForEach,
        Assign,
        InvokeMethod,
        WriteLine,
        Delay
    }

    /// <summary>Argument for InvokeMethod.</summary>
    public class ArgumentAssignment
    {
        public string Parameter { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Node of a robot plan.</summary>
    public class ActivityNode
    {
        public ActivityNodeKind Kind { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public List<ArgumentAssignment> Arguments { get; set; } = new List<ArgumentAssignment>();
        public string Variable { get; set; }
        public string Expression { get; set; }
        public List<ActivityNode> Children { get; set; } = new List<ActivityNode>();
    }

    /// <summary>Robot plan — a tree of WF activities.</summary>
    public class RobotPlan
    {
        public string Name { get; set; }
        public ActivityNode Root { get; set; }
    }

    /// <summary>Knowledge-base record: task + solution (XAML/C#) + source robot + documentation.</summary>
    public class SolutionRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
        public string XamlFragment { get; set; }
        public string CSharpCode { get; set; }
        public string SourceRobot { get; set; }
        public string Documentation { get; set; }
        public DateTime CreatedUtc { get; set; }

        /// <summary>File(s) where the task is implemented, relative to the robots root.</summary>
        public List<string> SolutionPaths { get; set; } = new List<string>();

        /// <summary>When the knowledge-base entry was generated (absolute UTC timestamp).</summary>
        public DateTime SpecGeneratedUtc { get; set; }

        /// <summary>Latest modification time of the robot source files (for commit-based refresh).</summary>
        public DateTime? SourceUpdatedUtc { get; set; }
    }

    /// <summary>A robot source file: path relative to the robots root + its content.</summary>
    public class RobotSourceFile
    {
        public string RelativePath { get; set; }
        public string Content { get; set; }
    }

    /// <summary>A decomposed task paired with the solution file(s) where it is implemented.</summary>
    public class RobotTaskEntry
    {
        public TaskDescriptor Task { get; set; }
        public List<string> SolutionPaths { get; set; } = new List<string>();
    }

    /// <summary>Result of importing the knowledge base from a robots directory.</summary>
    public class RobotKnowledgeBaseImportResult
    {
        public int RobotsProcessed { get; set; }
        public int SpecFilesWritten { get; set; }
        public int TaskFilesWritten { get; set; }
        public int TasksAdded { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>A matched similar task with a similarity score.</summary>
    public class SimilarTaskMatch
    {
        public TaskDescriptor Query { get; set; }
        public SolutionRecord Solution { get; set; }
        public double Score { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>Robot server access configuration.</summary>
    public class RobotDeploymentConfig
    {
        public string RobotId { get; set; }
        public string Transport { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> LogPaths { get; set; } = new List<string>();
        public List<string> Filters { get; set; } = new List<string>();
    }

    /// <summary>Compiler error, warning or message from the Visual Studio Error List.</summary>
    public class BuildError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Project { get; set; }
        public string Severity { get; set; }
        public int ErrorLevel { get; set; }
        public string ErrorLevelName { get; set; }
        public string Category { get; set; }
    }

    /// <summary>Log entry.</summary>
    public class LogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string RawLine { get; set; }
    }

    /// <summary>Log analysis result.</summary>
    public class LogAnalysis
    {
        public List<LogEntry> Entries { get; set; } = new List<LogEntry>();
        public List<LogEntry> Errors { get; set; } = new List<LogEntry>();
        public List<LogEntry> Warnings { get; set; } = new List<LogEntry>();
        public string Summary { get; set; }
    }

    /// <summary>Result of the end-to-end robot generation pipeline.</summary>
    public class PipelineResult
    {
        public SpecModel Spec { get; set; }
        public SpecAnalysis Analysis { get; set; }
        public RobotPlan Plan { get; set; }
        public string Xaml { get; set; }
        public string CSharpCode { get; set; }
        public List<SimilarTaskMatch> Reused { get; set; } = new List<SimilarTaskMatch>();
    }

    /// <summary>Result of a UiPath → Workflow Foundation robot conversion.</summary>
    public class UiPathConversionResult
    {
        public RobotPlan Plan { get; set; }
        public string Xaml { get; set; }
        public string CSharpCode { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Agent response in free-form chat: final text + the model's "reasoning" (when the provider emits it).</summary>
    public class ChatResponse
    {
        public string Reasoning { get; set; }
        public string Answer { get; set; }

        /// <summary>
        /// Provider-reported completion reason, e.g. "stop" (finished normally) or
        /// "length" (cut off at the output-token limit). Empty when the provider
        /// does not report it.
        /// </summary>
        public string FinishReason { get; set; }

        /// <summary>Native tool calls returned by the model (empty in JSON-over-chat mode).</summary>
        public IReadOnlyList<NativeToolCall> ToolCalls { get; set; } = new List<NativeToolCall>();
    }

    /// <summary>OpenAI function-calling schema for one tool.</summary>
    public sealed class ToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>OpenAI JSON Schema of arguments ({"type":"object","properties":...}).</summary>
        public Newtonsoft.Json.Linq.JObject Parameters { get; set; }
    }

    /// <summary>One native tool_call returned by the model.</summary>
    public sealed class NativeToolCall
    {
        /// <summary>tool_call_id that must be echoed back in the tool-result message.</summary>
        public string Id { get; set; }

        /// <summary>function.name.</summary>
        public string Name { get; set; }

        /// <summary>function.arguments parsed as JSON.</summary>
        public Newtonsoft.Json.Linq.JObject Arguments { get; set; }
    }

    /// <summary>A single chat message passed to the AI provider (role + content).</summary>
    public class ChatMessageData
    {
        /// <summary>Message role: "system" | "user" | "assistant" | "tool".</summary>
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";

        /// <summary>tool_call_id of the native tool call this message answers (role "tool" only).</summary>
        public string ToolCallId { get; set; } = "";

        /// <summary>Native tool calls made by an assistant message (role "assistant" only).</summary>
        public IReadOnlyList<NativeToolCall> ToolCalls { get; set; } = new List<NativeToolCall>();
    }

    /// <summary>A single incremental chunk of a streamed chat response.</summary>
    public class ChatStreamDelta
    {
        /// <summary>Incremental reasoning text, when the model emits a reasoning chunk.</summary>
        public string ReasoningDelta { get; set; }

        /// <summary>Incremental answer content text, when the model emits a content chunk.</summary>
        public string ContentDelta { get; set; }
    }

    /// <summary>Token usage reported by the AI provider for a single request.</summary>
    public class TokenUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }

        /// <summary>
        /// Input tokens served from the provider's prompt cache.
        /// DeepSeek: <c>prompt_cache_hit_tokens</c>; OpenAI: <c>prompt_tokens_details.cached_tokens</c>.
        /// </summary>
        public int CacheHitTokens { get; set; }

        /// <summary>Input tokens not served from cache (DeepSeek: <c>prompt_cache_miss_tokens</c>).</summary>
        public int CacheMissTokens { get; set; }

        /// <summary>Estimated cost of all requests in USD, computed from per-million token prices.</summary>
        public double EstimatedCostUsd { get; set; }
    }

    /// <summary>Outgoing AI message reported by a provider right before it is sent.</summary>
    public class AiMessageEventArgs : EventArgs
    {
        /// <summary>Message role, e.g. "system" or "user".</summary>
        public string Role { get; set; }
        /// <summary>Message text sent to the AI provider.</summary>
        public string Content { get; set; }
    }

    // Reported by a provider when a full outgoing request (system prompt + all messages) is assembled.
    public class PromptAssembledEventArgs : EventArgs
    {
        public string SystemPrompt { get; set; }
        public IReadOnlyList<ChatMessageData> Messages { get; set; }
        public int EstimatedInputTokens { get; set; }
    }

    /// <summary>Reported by a provider on each retry attempt while a request keeps failing.</summary>
    public class RetryAttemptEventArgs : EventArgs
    {
        /// <summary>1-based number of the retry attempt.</summary>
        public int Attempt { get; set; }
        /// <summary>HTTP status code returned by the provider, or null when no response was received.</summary>
        public int? StatusCode { get; set; }
        /// <summary>Reason phrase returned by the provider.</summary>
        public string ReasonPhrase { get; set; }
        /// <summary>Response body returned by the provider (truncated).</summary>
        public string Body { get; set; }
        /// <summary>Delay before the next attempt.</summary>
        public TimeSpan WaitBeforeRetry { get; set; }
        /// <summary>Time elapsed since the first attempt of this request.</summary>
        public TimeSpan Elapsed { get; set; }
        /// <summary>Time remaining until the retry budget is exhausted.</summary>
        public TimeSpan Remaining { get; set; }
        /// <summary>Whether another attempt will be made.</summary>
        public bool WillRetry { get; set; }
    }
}
