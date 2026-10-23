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
                .Where(t => IsExecutable(t, mode, allowed))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Single source of truth for whether a tool may actually run in the given mode and with the
        /// given enabled permissions. This is the enforcement boundary: the same predicate is used both
        /// to expose tools to the model (<see cref="ForMode"/>) and to gate execution here, so a tool that
        /// is hidden from the model can never be invoked.
        /// </summary>
        public bool TryGetExecutableTool(string name, AgentMode mode, AgentPermissions allowed, out IAgentTool tool, out string blockReason)
        {
            tool = Find(name);
            if (tool == null)
            {
                blockReason = "Unknown tool: \"" + (name ?? "") + "\". Use one of the tools listed in the system prompt.";
                return false;
            }

            if (!IsExecutable(tool, mode, allowed))
            {
                blockReason = mode == AgentMode.Help
                    ? "Blocked: Help mode exposes no tools."
                    : tool.Mutates && mode != AgentMode.Act
                        ? "Blocked: this mutating tool is not allowed in Plan mode."
                        : "Blocked: tool \"" + tool.Name + "\" requires the \"" + PermissionName(tool.Permission) + "\" permission, which is disabled in Settings.";
                return false;
            }

            blockReason = null;
            return true;
        }

        /// <summary>True when the tool is allowed to run for the given mode and enabled permission categories.</summary>
        private static bool IsExecutable(IAgentTool tool, AgentMode mode, AgentPermissions allowed)
        {
            // Help is a pure support chat: it answers from the bundled documentation and exposes no tools.
            if (mode == AgentMode.Help)
                return false;

            // Plan is read-only: mutating tools are never allowed.
            if (tool.Mutates && mode != AgentMode.Act)
                return false;

            // Every tool is additionally gated by its permission category.
            if ((allowed & tool.Permission) == 0)
                return false;

            return true;
        }

        private static string PermissionName(AgentPermissions permission)
        {
            switch (permission)
            {
                case AgentPermissions.ReadFiles: return "read files";
                case AgentPermissions.EditFiles: return "edit files";
                case AgentPermissions.ExecuteCommand: return "execute command";
                case AgentPermissions.WebFetch: return "web fetch";
                case AgentPermissions.Mcp: return "MCP";
                case AgentPermissions.Subagents: return "sub-agents";
                default: return "unknown";
            }
        }
    }
}
