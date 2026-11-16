using System;
using System.Collections.Generic;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Immutable-in-intent snapshot of the tool environment available to the agent at the moment
    /// a dialogue turn starts. It bundles the registry, calling protocol, permissions, memory-bank
    /// mode, read roots and user addressing so the system prompt, the native tool definitions and
    /// the execution gate all derive from the same, up-to-date set of tools — never from a stale
    /// snapshot captured when the runner was constructed.
    /// </summary>
    public sealed class AgentToolEnvironment
    {
        /// <summary>Tool registry built from the settings and MCP adapters active at turn start.</summary>
        public AgentToolRegistry Registry { get; set; }

        /// <summary>Tool calling protocol active at turn start (Native or JsonChat).</summary>
        public ToolCallingMode ToolCallingMode { get; set; }

        /// <summary>Permission categories enabled at turn start.</summary>
        public AgentPermissions AllowedPermissions { get; set; }

        /// <summary>Memory-bank convention active at turn start.</summary>
        public MemoryBankMode MemoryBankMode { get; set; }

        /// <summary>Read roots shown to the model (empty list means none).</summary>
        public IReadOnlyList<string> AllowedReadPaths { get; set; }

        /// <summary>User name used for addressing, or empty.</summary>
        public string UserName { get; set; }

        /// <summary>Whether the agent should address the user by name.</summary>
        public bool AddressByName { get; set; }

        /// <summary>Builds the agent system prompt for a mode from this snapshot.</summary>
        public string BuildPrompt(AgentMode mode)
        {
            if (Registry == null)
                throw new InvalidOperationException("Registry must be set before building the prompt.");
            return AgentPromptBuilder.Build(mode, Registry, ToolCallingMode, AllowedPermissions, UserName, AddressByName, MemoryBankMode, AllowedReadPaths);
        }

        /// <summary>Builds the native tool definitions for a mode from this snapshot.</summary>
        public IReadOnlyList<ToolDefinition> BuildToolDefinitions(AgentMode mode)
        {
            if (Registry == null)
                throw new InvalidOperationException("Registry must be set before building tool definitions.");
            return ToolSchemaConverter.BuildToolDefinitions(Registry, mode, AllowedPermissions);
        }
    }
}
