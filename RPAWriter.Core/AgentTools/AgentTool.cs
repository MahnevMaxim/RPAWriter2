using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Default tool implementation backed by a delegate.</summary>
    public sealed class AgentTool : IAgentTool
    {
        private readonly Func<JObject, CancellationToken, Task<string>> _handler;
        private readonly Func<JObject, CancellationToken, Task<string>> _displayHandler;

        public AgentTool(
            string name,
            string description,
            string parametersSchema,
            bool mutates,
            Func<JObject, CancellationToken, Task<string>> handler,
            Func<JObject, CancellationToken, Task<string>> displayHandler = null,
            AgentPermissions? permission = null,
            bool completesRun = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? "";
            ParametersSchema = parametersSchema ?? "{}";
            Mutates = mutates;
            Permission = permission ?? (mutates ? AgentPermissions.EditFiles : AgentPermissions.ReadFiles);
            CompletesRun = completesRun;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _displayHandler = displayHandler;
        }

        public string Name { get; }
        public string Description { get; }
        public string ParametersSchema { get; }
        public bool Mutates { get; }
        public AgentPermissions Permission { get; }
        public bool CompletesRun { get; }

        public Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken)
        {
            return _handler(arguments ?? new JObject(), cancellationToken);
        }

        public Task<string> GetDisplayResultAsync(JObject arguments, CancellationToken cancellationToken)
        {
            return _displayHandler != null
                ? _displayHandler(arguments ?? new JObject(), cancellationToken)
                : Task.FromResult<string>(null);
        }
    }
}
