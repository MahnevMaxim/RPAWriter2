using System.Collections.Generic;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Static catalog of the RPA-domain agent tools that are individually toggleable in
    /// Settings → RPA. It is the single source of truth for the tool names, descriptions and
    /// groups so the settings UI and the tool factory never drift apart.
    /// </summary>
    public static class RpaToolCatalog
    {
        public const string GroupRpa = "RPA";
        public const string GroupSpec = "Spec";
        public const string GroupKb = "KB";
        public const string GroupRpaConvert = "RPA convert";

        public static readonly IReadOnlyList<AgentToolCatalogEntry> All = new List<AgentToolCatalogEntry>
        {
            new AgentToolCatalogEntry("read_robot_sources", GroupRpa, "List the robot source files (.xaml/.cs) under a directory with their contents.", false),
            new AgentToolCatalogEntry("generate_robot", GroupRpa, "Generate a robot (XAML/C#) from the imported spec and save the artifacts next to the spec.", true),
            new AgentToolCatalogEntry("create_robot", GroupRpa, "Create a robot solution (RPA-<number>) from the configured template.", true),

            new AgentToolCatalogEntry("read_spec", GroupSpec, "Return the currently imported specification (name, steps, inputs, outputs, tasks, raw text).", false),
            new AgentToolCatalogEntry("analyze_spec", GroupSpec, "Analyze the imported spec into a summary and tasks.", false),
            new AgentToolCatalogEntry("decompose_tasks", GroupSpec, "Decompose a text into atomic tasks.", false),
            new AgentToolCatalogEntry("reconstruct_spec", GroupSpec, "Reconstruct a spec from robot sources under a directory.", false),
            new AgentToolCatalogEntry("map_task_to_solution", GroupSpec, "Locate the solution for a task in robot sources.", false),

            new AgentToolCatalogEntry("list_kb", GroupKb, "List the solutions in the knowledge base.", false),
            new AgentToolCatalogEntry("find_similar", GroupKb, "Find similar tasks in the knowledge base.", false),
            new AgentToolCatalogEntry("kb_import", GroupKb, "Import the knowledge base from the robots directory.", true),
            new AgentToolCatalogEntry("kb_add", GroupKb, "Add a record to the knowledge base.", true),

            new AgentToolCatalogEntry("convert_uipath", GroupRpaConvert, "Convert a UiPath XAML file to Workflow Foundation and write the artifacts.", true),
            new AgentToolCatalogEntry("list_pix_mwf_activities", GroupRpaConvert, "List the PIX and MWF activities known to the converter libraries together with their full argument descriptions (name, type, direction, description, required, default value). Use this to obtain the data needed to match PIX arguments to MWF arguments.", false),
            new AgentToolCatalogEntry("map_pix_mwf_arguments", GroupRpaConvert, "Map one PIX activity argument to one MWF activity argument in the PIX to MWF mapping store. pix_activity and pix_argument are required. Pass an empty mwf_argument to unmap. conversion is one of None, ToString, Parse, DateTimeFormat, Custom; conversion_expression is the format string or a custom C# expression template where {0} is the source value.", true),
        };
    }
}