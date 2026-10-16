using System;
using System.IO;
using Newtonsoft.Json;
using RPAWriter.Core.Services;

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
        public string RulesPath { get; set; } = PromptPaths.ResolveRulesDirectory();
        public int ContextWindowSize { get; set; } = 409600;
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
                    var settings = JsonConvert.DeserializeObject<SettingsData>(json) ?? new SettingsData();
                    EnsureDefaults(settings);
                    return settings;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }
            var defaults = new SettingsData();
            EnsureDefaults(defaults);
            return defaults;
        }

        private static void EnsureDefaults(SettingsData settings)
        {
            if (settings == null) return;
            if (string.IsNullOrWhiteSpace(settings.RulesPath))
                settings.RulesPath = PromptPaths.ResolveRulesDirectory();
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
