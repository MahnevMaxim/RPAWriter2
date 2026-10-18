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
        public string PackagesPath { get; set; } = "";
        public string RulesPath { get; set; } = PromptPaths.ResolveRulesDirectory();
        public int ContextWindowSize { get; set; } = 409600;

        /// <summary>HTTP request timeout in seconds (per provider request).</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Total budget for retrying a failing provider request, in minutes.</summary>
        public int RetryTimeoutMinutes { get; set; } = 30;

        /// <summary>Initial delay between retries, in seconds (doubles each attempt).</summary>
        public int RetryBaseDelaySeconds { get; set; } = 1;

        /// <summary>Maximum delay between retries, in seconds.</summary>
        public int RetryMaxDelaySeconds { get; set; } = 30;

        /// <summary>Price per 1M uncached input (cache-miss) tokens, in USD.</summary>
        public double InputPricePerMillion { get; set; } = 0.27;

        /// <summary>Price per 1M cache-hit input tokens, in USD.</summary>
        public double CacheHitPricePerMillion { get; set; } = 0.07;

        /// <summary>Price per 1M output tokens, in USD.</summary>
        public double OutputPricePerMillion { get; set; } = 1.10;

        /// <summary>Automatically summarize the conversation when approaching the context limit.</summary>
        public bool AutoCompact { get; set; } = true;

        /// <summary>Fraction of the context window that triggers compaction (0..1).</summary>
        public double CompactThreshold { get; set; } = 0.85;

        /// <summary>Fraction of the context window reserved for the model's reply (0..1).</summary>
        public double ReserveFraction { get; set; } = 0.12;

        /// <summary>How many most-recent messages are never summarized/truncated.</summary>
        public int KeepRecentMessages { get; set; } = 6;

        /// <summary>Maximum characters of a tool result fed back into the conversation.</summary>
        public int ToolResultMaxChars { get; set; } = 12000;

        /// <summary>Use the "tool" role for tool results; false falls back to "user".</summary>
        public bool UseToolRole { get; set; } = true;

        /// <summary>How the agent calls tools: "Native" (function calling) or "JsonChat" (JSON over chat).</summary>
        public string ToolCallingMode { get; set; } = "JsonChat";

        /// <summary>Maximum number of agent-loop iterations per run.</summary>
        public int MaxIterations { get; set; } = 8;

        /// <summary>Full path to the log file; empty uses the default location next to the extension.</summary>
        public string LogFilePath { get; set; } = "";

        /// <summary>Directory scanned for third-party IProdLogProvider DLLs; empty uses %AppData%\RPAWriter2\Providers.</summary>
        public string LogProvidersPath { get; set; } = "";
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
            if (settings.ContextWindowSize <= 0)
                settings.ContextWindowSize = 409600;
            if (settings.TimeoutSeconds <= 0)
                settings.TimeoutSeconds = 300;
            if (settings.RetryTimeoutMinutes <= 0)
                settings.RetryTimeoutMinutes = 30;
            if (settings.RetryBaseDelaySeconds <= 0)
                settings.RetryBaseDelaySeconds = 1;
            if (settings.RetryMaxDelaySeconds <= 0)
                settings.RetryMaxDelaySeconds = 30;
            if (settings.InputPricePerMillion < 0)
                settings.InputPricePerMillion = 0.27;
            if (settings.CacheHitPricePerMillion < 0)
                settings.CacheHitPricePerMillion = 0.07;
            if (settings.OutputPricePerMillion < 0)
                settings.OutputPricePerMillion = 1.10;
            if (settings.CompactThreshold <= 0 || settings.CompactThreshold > 1)
                settings.CompactThreshold = 0.85;
            if (settings.ReserveFraction < 0 || settings.ReserveFraction > 0.5)
                settings.ReserveFraction = 0.12;
            if (settings.KeepRecentMessages <= 0)
                settings.KeepRecentMessages = 6;
            if (settings.ToolResultMaxChars <= 0)
                settings.ToolResultMaxChars = 12000;
            if (settings.MaxIterations <= 0)
                settings.MaxIterations = 8;
            if (string.IsNullOrWhiteSpace(settings.ToolCallingMode) ||
                (!string.Equals(settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.ToolCallingMode, "JsonChat", StringComparison.OrdinalIgnoreCase)))
                settings.ToolCallingMode = "JsonChat";
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
