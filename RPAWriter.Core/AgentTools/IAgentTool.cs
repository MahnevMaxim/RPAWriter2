using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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

        /// <summary>Executes the tool and returns a text result fed back to the model.</summary>
        Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken);
    }
}
