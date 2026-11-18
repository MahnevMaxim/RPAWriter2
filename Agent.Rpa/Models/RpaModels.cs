using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Agent.Rpa.Models
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

    /// <summary>How knowledge-base formation should treat a previous run.</summary>
    public enum KnowledgeBaseFormationMode
    {
        /// <summary>Start over: clear previous state and re-form every robot.</summary>
        Override,
        /// <summary>Resume: skip robots already imported by a previous run.</summary>
        Continue
    }

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
}

