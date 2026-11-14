using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Outcome of a tool approval request: run the tool or reject it.</summary>
    public enum ToolApprovalDecision
    {
        /// <summary>Execute the tool.</summary>
        Approve,

        /// <summary>Skip the tool and feed a rejection note back to the model instead.</summary>
        Reject
    }

    /// <summary>
    /// Data the approval gate needs to present a tool call to the user before execution.
    /// UI-free: the host renders it and answers with a <see cref="ToolApprovalDecision"/>.
    /// </summary>
    public sealed class ToolApprovalRequest
    {
        /// <summary>Machine name of the tool being invoked.</summary>
        public string ToolName { get; set; } = "";

        /// <summary>Permission category the tool belongs to.</summary>
        public AgentPermissions Permission { get; set; }

        /// <summary>True when the tool changes state (files, solutions, builds, KB).</summary>
        public bool Mutates { get; set; }

        /// <summary>Human-readable tool description (from the registry).</summary>
        public string Description { get; set; } = "";

        /// <summary>Compact JSON of the tool arguments, for the approval card body.</summary>
        public string ArgumentsSummary { get; set; } = "";
    }

    /// <summary>
    /// Signature of the approval gate. Returning <see cref="ToolApprovalDecision.Approve"/> runs
    /// the tool; <see cref="ToolApprovalDecision.Reject"/> skips it. A <c>null</c> gate means
    /// "always approve" (the pre-gating behavior).
    /// </summary>
    public delegate Task<ToolApprovalDecision> ToolApprovalGate(ToolApprovalRequest request, CancellationToken cancellationToken);
}
