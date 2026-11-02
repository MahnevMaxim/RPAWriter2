namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Per-workspace settings. By decision (documentation/14-workspaces-and-mvvm-plan.md) only the
    /// two log file paths are workspace-scoped; everything else stays global. Stored as
    /// <c>workspaces/&lt;id&gt;/settings.json</c>.
    /// </summary>
    public class WorkspaceLogSettings
    {
        /// <summary>Full path to the extension log file; empty means "use the default location".</summary>
        public string LogFilePath { get; set; } = "";

        /// <summary>Full path to the chat log file; empty means "use the default location".</summary>
        public string ChatLogFilePath { get; set; } = "";
    }
}
