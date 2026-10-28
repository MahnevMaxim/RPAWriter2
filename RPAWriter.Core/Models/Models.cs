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
        Act,
        /// <summary>Help mode: agent answers questions and explains features without touching the project.</summary>
        Help
    }

    /// <summary>
    /// Category-level permissions for agent tools. Each tool declares one category;
    /// a tool is allowed only while its category is enabled in the settings.
    /// </summary>
    [Flags]
    public enum AgentPermissions
    {
        /// <summary>Read files, specs, KB records, logs and compiler output.</summary>
        ReadFiles = 1,
        /// <summary>Write/edit files, generate robots, import the knowledge base.</summary>
        EditFiles = 2,
        /// <summary>Run arbitrary shell/PowerShell commands.</summary>
        ExecuteCommand = 4,
        /// <summary>Fetch or download web resources.</summary>
        WebFetch = 8,
        /// <summary>Call MCP-provided tools.</summary>
        Mcp = 16,
        /// <summary>Spawn sub-agents / delegate tasks (spawn_agent tool).</summary>
        Subagents = 32,
        /// <summary>All permissions enabled.</summary>
        All = ReadFiles | EditFiles | ExecuteCommand | WebFetch | Mcp | Subagents
    }

    /// <summary>How the agent talks to the model about tools.</summary>
    public enum ToolCallingMode
    {
        /// <summary>Model outputs JSON {"action":"final"/"tool_call",...} in plain text.</summary>
        JsonChat,
        /// <summary>Native OpenAI-style tools / tool_calls.</summary>
        Native
    }

    /// <summary>How the agent uses the project memory bank.</summary>
    public enum MemoryBankMode
    {
        /// <summary>Memory bank is not used at all.</summary>
        Disabled,
        /// <summary>Memory bank is exposed as dedicated tools (read_memory / update_memory).</summary>
        Tool,
        /// <summary>Memory bank is a convention: read/update the files with regular file tools.</summary>
        Convention
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
        /// <summary>The agent's final answer in Plan mode (read-only planning).</summary>
        ResultAnswerPlan,
        /// <summary>The agent's final answer in Act mode (changes were made).</summary>
        ResultAnswerAct,
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
        DiffEdit,
        /// <summary>An interactive question asked by the agent, with answer options.</summary>
        Question,
        /// <summary>A sub-agent (spawn_agent) status card: live progress and final report.</summary>
        Subagent,
        /// <summary>A saved file checkpoint (shadow git snapshot) with Compare/Restore actions.</summary>
        Checkpoint,
        /// <summary>A per-iteration diff indicator: a button that opens the diff tool window.</summary>
        Diff
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

    /// <summary>Severity of a log entry supplied by a production-log provider.</summary>
    public enum LogLevel
    {
        /// <summary>Most verbose diagnostics.</summary>
        Trace,

        /// <summary>Debug-level diagnostics.</summary>
        Debug,

        /// <summary>Informational message.</summary>
        Info,

        /// <summary>Non-fatal warning.</summary>
        Warning,

        /// <summary>Error that likely needs attention.</summary>
        Error,

        /// <summary>Fatal failure.</summary>
        Fatal
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

        /// <summary>Estimated effort for the task in hours (AI-provided); null when unknown.</summary>
        public double? EstimatedHours { get; set; }

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

    /// <summary>Result of reconciling several algorithm drafts: final algorithm + clarification questions.</summary>
    public class AlgorithmConsolidation
    {
        public string AlgorithmMarkdown { get; set; }
        public string QuestionsMarkdown { get; set; }
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
        Delay,
        TryCatch,
        Throw,
        Return,
        InvokeWorkflow
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

        /// <summary>Full MWF activity type chosen by a user/AI-managed mapping, when one was applied.</summary>
        public string MappedActivity { get; set; }

        /// <summary>Name of the workflow class referenced by an <see cref="ActivityNodeKind.InvokeWorkflow"/> node.</summary>
        public string ScriptRef { get; set; }

        public List<ArgumentAssignment> Arguments { get; set; } = new List<ArgumentAssignment>();
        public string Variable { get; set; }
        public string Expression { get; set; }
        public List<ActivityNode> Children { get; set; } = new List<ActivityNode>();

        /// <summary>Body of the "Else" branch for an <see cref="ActivityNodeKind.If"/> node.</summary>
        public List<ActivityNode> ElseChildren { get; set; } = new List<ActivityNode>();

        /// <summary>Body of the catch handlers for a <see cref="ActivityNodeKind.TryCatch"/> node.</summary>
        public List<ActivityNode> CatchChildren { get; set; } = new List<ActivityNode>();

        /// <summary>Body of the finally handler for a <see cref="ActivityNodeKind.TryCatch"/> node.</summary>
        public List<ActivityNode> FinallyChildren { get; set; } = new List<ActivityNode>();
    }

    /// <summary>Robot plan — a tree of WF activities.</summary>
    public class RobotPlan
    {
        public string Name { get; set; }
        public ActivityNode Root { get; set; }
    }

    /// <summary>Binding of one PIX activity property to a field of <see cref="ActivityNode"/>.</summary>
    public class PixActivityParameter
    {
        /// <summary>PIX property name inside <c>PropertyValues</c>.</summary>
        public string Pix { get; set; }

        /// <summary>Target field on the generated activity node: Expression | Variable | Argument.</summary>
        public string Target { get; set; }

        /// <summary>.NET type of the PIX property (e.g. System.String, System.Data.DataTable).</summary>
        public string Type { get; set; }

        /// <summary>Human-readable argument name shown in the mapping UI.</summary>
        public string DisplayName { get; set; }

        /// <summary>Argument description used by the UI and the AI mapping prompts.</summary>
        public string Description { get; set; }

        /// <summary>True when the argument is required (from attribute metadata, when available).</summary>
        public bool IsRequired { get; set; }

        /// <summary>Default value of the argument (from attribute metadata, when available).</summary>
        public string DefaultValue { get; set; }
    }

    /// <summary>Describes how one PIX activity maps to a Workflow Foundation snippet.</summary>
    public class PixActivityDescriptor
    {
        /// <summary>Full PIX activity name (the value of <c>_activityFullName</c>).</summary>
        public string ActivityFullName { get; set; }

        /// <summary>Name of the WF snippet file to use, or null for structural branch roles.</summary>
        public string WfSnippet { get; set; }

        /// <summary>Human-readable display name used when the PIX step has no text.</summary>
        public string DefaultDisplayName { get; set; }

        /// <summary>Russian description of what the activity does.</summary>
        public string Description { get; set; }

        /// <summary>Structural role: container | if | then | elseIf | else | tryCatch | try | catch | finally | statement.</summary>
        public string Role { get; set; }

        /// <summary>Parent activity that owns this branch role (e.g. BR.Core.Base.If).</summary>
        public string Parent { get; set; }

        /// <summary>Property-to-field bindings applied while building the activity node.</summary>
        public List<PixActivityParameter> Parameters { get; set; } = new List<PixActivityParameter>();

        /// <summary>Static warnings emitted whenever this activity is converted.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Result of converting a PIX robot into plain Workflow Foundation.</summary>
    public class PixConversionResult
    {
        public RobotPlan Plan { get; set; }
        public string Xaml { get; set; }
        public string CSharpCode { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Applied PIX → MWF mappings, formatted as "pixFullName → mwfFullName".</summary>
        public List<string> Mappings { get; set; } = new List<string>();
    }

    /// <summary>Result of converting a single PIX script (.pix) into a Workflow Foundation .xaml workflow.</summary>
    public class PixScriptConversionResult
    {
        /// <summary>Source .pix file name (e.g. main.pix).</summary>
        public string SourceFileName { get; set; }

        /// <summary>Target .xaml file name inside the MWF robot folder (e.g. Main.xaml).</summary>
        public string OutputFileName { get; set; }

        /// <summary>Generated Workflow Foundation XAML.</summary>
        public string Xaml { get; set; }

        /// <summary>Generated C# helper code for this script.</summary>
        public string CSharpCode { get; set; }

        /// <summary>Warnings collected while converting this script.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>PIX to MWF mappings applied while converting this script.</summary>
        public List<string> Mappings { get; set; } = new List<string>();
    }

    /// <summary>Result of converting a whole PIX robot folder into a Workflow Foundation (MWF) robot.</summary>
    public class PixRobotConversionResult
    {
        /// <summary>Robot name taken from the .pixproj file (or the folder name).</summary>
        public string RobotName { get; set; }

        /// <summary>Per-script conversion results, in generation order (Main first).</summary>
        public List<PixScriptConversionResult> Scripts { get; set; } = new List<PixScriptConversionResult>();

        /// <summary>Combined C# helper code for all scripts (Robots.RpaActions).</summary>
        public string AggregateCSharpCode { get; set; }

        /// <summary>All warnings collected across scripts.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>All applied PIX to MWF mappings across scripts.</summary>
        public List<string> AppliedMappings { get; set; } = new List<string>();
    }

    /// <summary>Result of importing PIX activities from the installed PIX assemblies.</summary>
    public class PixActivityLibraryImportResult
    {
        /// <summary>Number of scanned assemblies (<c>Core.dll</c> plus <c>Activities.*.dll</c>).</summary>
        public int DllsScanned { get; set; }

        /// <summary>Number of concrete PIX activities discovered across all assemblies.</summary>
        public int ActivitiesImported { get; set; }

        /// <summary>Number of descriptor JSON files written to the library directory.</summary>
        public int FilesWritten { get; set; }

        /// <summary>Number of stale descriptor files removed because their activity no longer exists.</summary>
        public int FilesDeleted { get; set; }

        /// <summary>Non-fatal errors collected while scanning/reflecting/writing.</summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Assembly names that failed to load (obsolete-file cleanup is skipped when this is non-empty).</summary>
        public List<string> FailedAssemblies { get; set; } = new List<string>();
    }

    /// <summary>Property of a Workflow Foundation activity exposed to the converter.</summary>
    public class WfActivityParameter
    {
        /// <summary>Property name.</summary>
        public string Name { get; set; }

        /// <summary>Value type of the argument (inner generic type, e.g. System.String).</summary>
        public string Type { get; set; }

        /// <summary>Human-readable argument name shown in the mapping UI.</summary>
        public string DisplayName { get; set; }

        /// <summary>Argument description used by the UI and the AI mapping prompts.</summary>
        public string Description { get; set; }

        /// <summary>Data-flow direction: In | Out | InOut | Property. Empty when unknown.</summary>
        public string Direction { get; set; }

        /// <summary>True when the argument is required (from attribute metadata, when available).</summary>
        public bool IsRequired { get; set; }

        /// <summary>Default value of the argument (from attribute metadata, when available).</summary>
        public string DefaultValue { get; set; }
    }

    /// <summary>Describes one Workflow Foundation activity discovered in Activities.*.dll.</summary>
    public class WfActivityDescriptor
    {
        /// <summary>Full CLR type name of the activity.</summary>
        public string ActivityFullName { get; set; }

        /// <summary>Human-readable display name used by the converter UI.</summary>
        public string DefaultDisplayName { get; set; }

        /// <summary>Russian description of what the activity does.</summary>
        public string Description { get; set; }

        /// <summary>Full name of the base activity type (typically System.Activities.Activity).</summary>
        public string BaseType { get; set; }

        /// <summary>Assembly file name the activity was found in.</summary>
        public string AssemblyName { get; set; }

        /// <summary>Public instance properties exposed by the activity.</summary>
        public List<WfActivityParameter> Parameters { get; set; } = new List<WfActivityParameter>();
    }

    /// <summary>Result of importing Workflow Foundation activities from Activities.*.dll.</summary>
    public class WfActivityLibraryImportResult
    {
        /// <summary>Number of scanned assemblies (<c>Activities.*.dll</c>).</summary>
        public int DllsScanned { get; set; }

        /// <summary>Number of concrete WF activities discovered across all assemblies.</summary>
        public int ActivitiesImported { get; set; }

        /// <summary>Number of descriptor JSON files written to the library directory.</summary>
        public int FilesWritten { get; set; }

        /// <summary>Number of stale descriptor files removed because their activity no longer exists.</summary>
        public int FilesDeleted { get; set; }

        /// <summary>Non-fatal errors collected while scanning/reflecting/writing.</summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Assembly names that failed to load (obsolete-file cleanup is skipped when this is non-empty).</summary>
        public List<string> FailedAssemblies { get; set; } = new List<string>();
        /// <summary>Number of C# source files scanned (used only by the source importer).</summary>
        public int SourceFilesScanned { get; set; }

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

    /// <summary>Result of a read-only security audit over a set of robot solutions.</summary>
    public class RobotSecurityAuditResult
    {
        /// <summary>Number of solutions that were processed (audited or attempted).</summary>
        public int SolutionsAudited { get; set; }

        /// <summary>Number of solutions whose audit failed.</summary>
        public int SolutionsFailed { get; set; }

        /// <summary>Non-fatal issues encountered during the audit (missing files, per-solution failures).</summary>
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

    /// <summary>One executed task of a production robot (a row in the tasks table).</summary>
    public class ProdTask
    {
        public string TaskId { get; set; }
        public string Name { get; set; }
        public string RobotName { get; set; }
        public string Status { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>A single key/value parameter attached to a production log entry.</summary>
    public class LogParameter
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// Filter passed to a production-log provider. The provider applies it and returns
    /// only the matching tasks and log entries.
    /// </summary>
    public class ProdLogQuery
    {
        /// <summary>Minimum log severity to return (inclusive); null returns all levels.</summary>
        public LogLevel? MinLevel { get; set; }

        /// <summary>Inclusive lower bound of the run start time (UTC); null means unbounded.</summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>Inclusive upper bound of the run start time (UTC); null means unbounded.</summary>
        public DateTime? ToUtc { get; set; }
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

    /// <summary>
    /// A snapshot of an exception captured from the Visual Studio debugger while it is
    /// stopped on the exception. Stored as plain text so it can be handed to the agent
    /// without any dependency on EnvDTE.
    /// </summary>
    public class DebuggerExceptionSnapshot
    {
        /// <summary>Fully qualified exception type, e.g. "System.NullReferenceException".</summary>
        public string TypeName { get; set; } = "";

        /// <summary>Short message of the top-level exception.</summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Full exception dump produced by <c>Exception.ToString()</c>: type, message, stack
        /// trace and the complete chain of nested <c>InnerException</c> values.
        /// </summary>
        public string ExceptionDump { get; set; } = "";

        /// <summary>Reason the debugger entered break mode (e.g. "dbgEventReasonExceptionThrown").</summary>
        public string BreakReason { get; set; } = "";

        /// <summary>Current stack frame / function name at the stop location, when available.</summary>
        public string Location { get; set; } = "";
    }

    /// <summary>Log entry.</summary>
    public class LogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string RawLine { get; set; }
        public List<LogParameter> Parameters { get; set; } = new List<LogParameter>();
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

    /// <summary>An image attached to a chat message, encoded as a base64 data URL part.</summary>
    public sealed class ChatImagePart
    {
        /// <summary>MIME type of the image, e.g. "image/png" or "image/jpeg".</summary>
        public string MediaType { get; set; } = "image/png";

        /// <summary>Base64-encoded image bytes (without the "data:...;base64," prefix).</summary>
        public string DataBase64 { get; set; } = "";
    }


    /// <summary>A single chat message passed to the AI provider (role + content).</summary>
    public class ChatMessageData
    {
        /// <summary>Message role: "system" | "user" | "assistant" | "tool".</summary>
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";

        /// <summary>Images attached to this message (role "user" only). Empty for plain text messages.</summary>
        public IReadOnlyList<ChatImagePart> Images { get; set; } = new List<ChatImagePart>();

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
