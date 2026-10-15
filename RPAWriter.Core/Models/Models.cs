using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Models
{
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

    /// <summary>Compiler error/warning.</summary>
    public class BuildError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public string Severity { get; set; }
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
    }
}
