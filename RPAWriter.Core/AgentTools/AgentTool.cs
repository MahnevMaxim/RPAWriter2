using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Default tool implementation backed by a delegate.</summary>
    public sealed class AgentTool : IAgentTool
    {
        private readonly Func<JObject, CancellationToken, Task<string>> _handler;

        public AgentTool(
            string name,
            string description,
            string parametersSchema,
            bool mutates,
            Func<JObject, CancellationToken, Task<string>> handler)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? "";
            ParametersSchema = parametersSchema ?? "{}";
            Mutates = mutates;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public string Name { get; }
        public string Description { get; }
        public string ParametersSchema { get; }
        public bool Mutates { get; }

        public Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken)
        {
            return _handler(arguments ?? new JObject(), cancellationToken);
        }
    }
}
