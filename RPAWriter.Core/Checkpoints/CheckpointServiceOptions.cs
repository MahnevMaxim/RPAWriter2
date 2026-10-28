using System;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Configuration for a per-task checkpoint service.</summary>
    public sealed class CheckpointServiceOptions
    {
        /// <summary>Task/conversation id; the shadow repo lives at StateDir/tasks/{TaskId}/checkpoints.</summary>
        public string TaskId { get; set; }

        /// <summary>Absolute path to the workspace (solution) root being snapshotted.</summary>
        public string WorkspaceDir { get; set; }

        /// <summary>Absolute base directory where checkpoint repositories are stored.</summary>
        public string StateDir { get; set; }

        /// <summary>Optional logger (null = no logging).</summary>
        public Action<string> Log { get; set; }
    }
}
