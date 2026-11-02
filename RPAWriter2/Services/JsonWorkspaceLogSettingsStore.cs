using System.IO;
using Newtonsoft.Json;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Loads/saves <see cref="WorkspaceLogSettings"/> at
    /// <c>%AppData%\RPAWriter2\&lt;version&gt;\workspaces\&lt;id&gt;\settings.json</c>. When no
    /// workspace is active the store returns defaults and skips writes.
    /// </summary>
    public sealed class JsonWorkspaceLogSettingsStore : IWorkspaceLogSettingsStore
    {
        private static string SettingsFile =>
            string.IsNullOrWhiteSpace(AppDataPaths.WorkspaceId)
                ? ""
                : Path.Combine(AppDataPaths.WorkspaceScopedRoot, "settings.json");

        public WorkspaceLogSettings Load()
        {
            try
            {
                if (!string.IsNullOrEmpty(SettingsFile) && File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonConvert.DeserializeObject<WorkspaceLogSettings>(json) ?? new WorkspaceLogSettings();
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            return new WorkspaceLogSettings();
        }

        public void Save(WorkspaceLogSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(SettingsFile))
                return;

            try
            {
                var directory = Path.GetDirectoryName(SettingsFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }
    }
}
