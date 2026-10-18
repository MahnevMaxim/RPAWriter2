using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>A tool the agent can invoke from the chat: read-only or mutating.</summary>
    public interface IAgentTool
    {
        /// <summary>Machine name used in the tool_call JSON.</summary>
        string Name { get; }

        /// <summary>Human-readable description shown in the system prompt.</summary>
        string Description { get; }

        /// <summary>JSON schema describing the accepted arguments (shown in the system prompt).</summary>
        string ParametersSchema { get; }

        /// <summary>True when the tool changes state (files, solutions, builds, KB).</summary>
        bool Mutates { get; }

        /// <summary>Permission category the tool belongs to (gated by the settings).</summary>
        AgentPermissions Permission { get; }

        /// <summary>Executes the tool and returns a text result fed back to the model.</summary>
        Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken);

        /// <summary>
        /// Produces a short text for the chat progress line (e.g. a file path), or null when
        /// the full execution result should be shown instead.
        /// </summary>
        Task<string> GetDisplayResultAsync(JObject arguments, CancellationToken cancellationToken);
    }
}
