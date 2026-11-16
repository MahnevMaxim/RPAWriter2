using System.Collections.Generic;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Static catalog of the native (non-RPA-domain) agent tools that are individually
    /// toggleable in Settings → Native tools. MCP tools are not listed here: they are governed
    /// by the MCP permission category and the configured MCP servers.
    /// </summary>
    public static class NativeToolCatalog
    {
        public const string GroupFiles = "Files & navigation";
        public const string GroupRoslyn = "Roslyn (C#)";
        public const string GroupBuild = "Build & diagnostics";
        public const string GroupEdit = "Edit & execute";
        public const string GroupWeb = "Web";
        public const string GroupInteraction = "Interaction";
        public const string GroupMemory = "Memory bank";

        public static readonly IReadOnlyList<AgentToolCatalogEntry> All = new List<AgentToolCatalogEntry>
        {
            new AgentToolCatalogEntry("read_files", GroupFiles, "Read text or image files by absolute path, with optional line ranges; several files per call.", false),
            new AgentToolCatalogEntry("list_files", GroupFiles, "List files and directories in a directory (recursively or top-level only).", false),
            new AgentToolCatalogEntry("search_codebase", GroupFiles, "Regex search across the codebase, several queries in parallel.", false),
            new AgentToolCatalogEntry("open_file_in_visual_studio", GroupFiles, "Open a file in the Visual Studio editor and activate its tab.", false),
            new AgentToolCatalogEntry("git_status", GroupFiles, "Show the git working-tree status of the open solution's repository.", false),
            new AgentToolCatalogEntry("git_diff", GroupFiles, "Show the uncommitted git diff of the repository (optionally for one path).", false),

            new AgentToolCatalogEntry("code_outline", GroupRoslyn, "Semantic outline of a C# file: types and members with signatures, without method bodies.", false),
            new AgentToolCatalogEntry("find_symbol_usages", GroupRoslyn, "Find all compiler-verified references to a C# symbol.", false),
            new AgentToolCatalogEntry("get_inheritance", GroupRoslyn, "Return the inheritance tree of a C# type.", false),
            new AgentToolCatalogEntry("get_diagnostics", GroupRoslyn, "Return Roslyn background diagnostics for a C# file without a build.", false),

            new AgentToolCatalogEntry("build", GroupBuild, "Run a Release build of the open solution.", true),
            new AgentToolCatalogEntry("read_errors", GroupBuild, "Read compiler errors from the Visual Studio Error List.", false),
            new AgentToolCatalogEntry("read_output", GroupBuild, "Read the Visual Studio Output window text.", false),
            new AgentToolCatalogEntry("analyze_logs", GroupBuild, "Analyze log files at the given paths.", false),

            new AgentToolCatalogEntry("editor", GroupEdit, "Controlled filesystem edits: insert or replace text, or create a file.", true),
            new AgentToolCatalogEntry("run_commands", GroupEdit, "Run non-interactive PowerShell shell commands.", true),

            new AgentToolCatalogEntry("fetch_web_content", GroupWeb, "Fetch URLs and analyze them against a prompt.", false),
            new AgentToolCatalogEntry("download_file", GroupWeb, "Download a file from a URL and save it to disk.", true),

            new AgentToolCatalogEntry("ask_question", GroupInteraction, "Ask the user one clarifying question with 2-5 answer options.", false),
            new AgentToolCatalogEntry("skills", GroupInteraction, "Execute a skill (slash command) from the skills directory.", false),
            new AgentToolCatalogEntry("submit_and_exit", GroupInteraction, "Submit the final answer and exit the conversation.", false),
            new AgentToolCatalogEntry("spawn_agent", GroupInteraction, "Spawn a sub-agent with a separate context window.", false),

            new AgentToolCatalogEntry("read_memory", GroupMemory, "Read a project memory bank file (memory-bank/*.md).", false),
            new AgentToolCatalogEntry("update_memory", GroupMemory, "Write a project memory bank file (memory-bank/*.md).", true),
        };
    }
}