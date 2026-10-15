using System;
using System.IO;
using Newtonsoft.Json;

namespace RPAWriter2.Services
{
    /// <summary>Extension settings persisted locally as JSON.</summary>
    public class SettingsData
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public string KnowledgeBasePath { get; set; } = "";
        public string RobotsRootPath { get; set; } = "";
        public string RobotTemplateName { get; set; } = "";
        public string RulesPath { get; set; } = "";
    }

    /// <summary>Loads/saves settings at %AppData%\RPAWriter2\settings.json.</summary>
    public static class SettingsStore
    {
        public static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2");

        public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

        public static SettingsData Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonConvert.DeserializeObject<SettingsData>(json) ?? new SettingsData();
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }
            return new SettingsData();
        }

        public static void Save(SettingsData settings)
        {
            if (settings == null) return;
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }
    }
}
