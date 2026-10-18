using System;
using System.Collections.Generic;
using System.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>Registry of agent tools with per-mode filtering.</summary>
    public sealed class AgentToolRegistry
    {
        private readonly Dictionary<string, IAgentTool> _tools =
            new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);

        public void Register(IAgentTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _tools[tool.Name] = tool;
        }

        public IAgentTool Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            IAgentTool tool;
            return _tools.TryGetValue(name.Trim(), out tool) ? tool : null;
        }

        /// <summary>Tools allowed in the given mode: Act gets everything, Plan gets read-only only.</summary>
        public IReadOnlyList<IAgentTool> ForMode(AgentMode mode)
        {
            return ForMode(mode, AgentPermissions.All);
        }

        /// <summary>Tools allowed for a mode AND whose permission category is enabled.</summary>
        public IReadOnlyList<IAgentTool> ForMode(AgentMode mode, AgentPermissions allowed)
        {
            return _tools.Values
                .Where(t => (mode == AgentMode.Act || !t.Mutates) && (allowed & t.Permission) != 0)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
