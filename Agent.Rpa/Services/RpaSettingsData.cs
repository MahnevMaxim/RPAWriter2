using System.Collections.Generic;
using Agent.Core.Services;

using Agent.Rpa.Contracts;
using Agent.Rpa.Models;

namespace Agent.Rpa.Services
{
    /// <summary>RPA-domain settings, persisted separately from the shared <c>SettingsData</c>.</summary>
    public class RpaSettingsData
    {
        public string KnowledgeBasePath { get; set; } = "";
        public string RobotsRootPath { get; set; } = "";
        public string RobotTemplateName { get; set; } = "";
        public bool ShowRobotCreator { get; set; } = true;
        public string PackagesPath { get; set; } = "";
        public string RulesPath { get; set; } = PromptPaths.ResolveRulesDirectory();

        /// <summary>Security audit scope: "Full" (all areas) or "CredentialsOnly" (passwords &amp; logins).</summary>
        public string SecurityAuditMode { get; set; } = "Full";

        /// <summary>Project name to deploy (build) in the Publish configuration.</summary>
        public string DeployProjectName { get; set; } = "";

        /// <summary>Directory scanned for third-party IProdLogProvider DLLs.</summary>
        public string LogProvidersPath { get; set; } = "";

        /// <summary>Recently used production robot names (most recent first).</summary>
        public List<string> RobotNameHistory { get; set; } = new List<string>();

        /// <summary>Names of the RPA-domain agent tools the user has disabled.</summary>
        public List<string> DisabledRpaTools { get; set; } = new List<string>();

        /// <summary>Normalizes the settings in place; pure and independent of any store.</summary>
        public static void Normalize(RpaSettingsData settings)
        {
            if (settings == null) return;
            if (string.IsNullOrWhiteSpace(settings.RulesPath))
                settings.RulesPath = PromptPaths.ResolveRulesDirectory();
            if (string.IsNullOrWhiteSpace(settings.SecurityAuditMode) ||
                (!string.Equals(settings.SecurityAuditMode, "Full", System.StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.SecurityAuditMode, "CredentialsOnly", System.StringComparison.OrdinalIgnoreCase)))
                settings.SecurityAuditMode = "Full";
            if (settings.RobotNameHistory == null)
                settings.RobotNameHistory = new List<string>();
            if (settings.DisabledRpaTools == null)
                settings.DisabledRpaTools = new List<string>();
        }
    }
}
