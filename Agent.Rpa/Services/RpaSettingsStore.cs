using System.IO;
using Newtonsoft.Json;
using Agent.Core.Services;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;

namespace Agent.Rpa.Services
{
    /// <summary>Loads/saves RPA-domain settings at %AppData%\RPAWriter2\&lt;version&gt;\rpa-settings.json.</summary>
    public sealed class RpaSettingsStore : IRpaSettingsStore
    {
        public static string SettingsFile => Path.Combine(AppDataPaths.Resolve(), "rpa-settings.json");

        public RpaSettingsData Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<RpaSettingsData>(json) ?? new RpaSettingsData();
                    RpaSettingsData.Normalize(settings);
                    return settings;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }
            var defaults = new RpaSettingsData();
            RpaSettingsData.Normalize(defaults);
            return defaults;
        }

        public void Save(RpaSettingsData settings)
        {
            if (settings == null) return;
            try
            {
                Directory.CreateDirectory(AppDataPaths.Resolve());
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }
    }
}
