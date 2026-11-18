using System;
using System.Collections.Generic;
using System.Linq;
using Agent.Core.AgentTools;
using Agent.Mvvm;
using Agent.Models;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Services;

namespace Agent.Rpa.Models
{
    /// <summary>
    /// RPA-domain settings model: robot generation, spec, knowledge base, converters and
    /// production-log settings. UI-free by contract; persistence is injected through
    /// <see cref="IRpaSettingsStore"/>.
    /// </summary>
    public sealed class RpaSettingsModel : BaseModel
    {
        private readonly IRpaSettingsStore _store;

        private string _knowledgeBasePath = "";
        private string _robotsRootPath = "";
        private string _robotTemplateName = "";
        private bool _showRobotCreator = true;
        private string _packagesPath = "";
        private string _rulesPath = "";
        private string _securityAuditMode = "Full";
        private string _deployProjectName = "";
        private string _logProvidersPath = "";
        private List<string> _robotNameHistory = new List<string>();

        private readonly List<AgentToolToggleGroup> _agentToolGroups = new List<AgentToolToggleGroup>();

        public RpaSettingsModel(IRpaSettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            RebuildAgentToolGroups();
        }

        public override void Run() => Load();

        public void Load()
        {
            var data = _store.Load();
            Apply(data);
            ApplyDisabledRpaTools(data.DisabledRpaTools);
        }

        public RpaSettingsData ToSettingsData() => new RpaSettingsData
        {
            KnowledgeBasePath = _knowledgeBasePath,
            RobotsRootPath = _robotsRootPath,
            RobotTemplateName = _robotTemplateName,
            ShowRobotCreator = _showRobotCreator,
            PackagesPath = _packagesPath,
            RulesPath = _rulesPath,
            SecurityAuditMode = _securityAuditMode,
            DeployProjectName = _deployProjectName,
            LogProvidersPath = _logProvidersPath,
            RobotNameHistory = new List<string>(_robotNameHistory),
            DisabledRpaTools = BuildDisabledRpaTools()
        };

        public void Save()
        {
            var data = ToSettingsData();
            RpaSettingsData.Normalize(data);
            Apply(data);
            _store.Save(data);
        }

        private void Apply(RpaSettingsData data)
        {
            if (data == null) return;
            KnowledgeBasePath = data.KnowledgeBasePath;
            RobotsRootPath = data.RobotsRootPath;
            RobotTemplateName = data.RobotTemplateName;
            ShowRobotCreator = data.ShowRobotCreator;
            PackagesPath = data.PackagesPath;
            RulesPath = data.RulesPath;
            SecurityAuditMode = data.SecurityAuditMode;
            DeployProjectName = data.DeployProjectName;
            LogProvidersPath = data.LogProvidersPath;
            _robotNameHistory = data.RobotNameHistory == null
                ? new List<string>()
                : new List<string>(data.RobotNameHistory);
        }

        public IReadOnlyList<AgentToolToggleGroup> AgentToolGroups => _agentToolGroups;

        private void RebuildAgentToolGroups()
        {
            _agentToolGroups.Clear();
            PopulateGroups(_agentToolGroups, RpaToolCatalog.All);
        }

        private static void PopulateGroups(List<AgentToolToggleGroup> groups, IEnumerable<AgentToolCatalogEntry> entries)
        {
            foreach (var entry in entries)
            {
                var group = groups.FirstOrDefault(g => string.Equals(g.Name, entry.Group, StringComparison.Ordinal));
                if (group == null)
                {
                    group = new AgentToolToggleGroup(entry.Group);
                    groups.Add(group);
                }
                group.Items.Add(new AgentToolToggleItem(entry.Name, entry.Group, entry.Description, entry.Mutates, isEnabled: true));
            }
        }

        private void ApplyDisabledRpaTools(IReadOnlyCollection<string> disabledNames)
        {
            foreach (var group in _agentToolGroups)
                foreach (var item in group.Items)
                    item.IsEnabled = disabledNames == null || !disabledNames.Contains(item.Name);
        }

        private List<string> BuildDisabledRpaTools()
        {
            var disabled = new List<string>();
            foreach (var group in _agentToolGroups)
                foreach (var item in group.Items)
                    if (!item.IsEnabled)
                        disabled.Add(item.Name);
            return disabled;
        }

        public string KnowledgeBasePath { get => _knowledgeBasePath; set => SetProperty(ref _knowledgeBasePath, value); }
        public string RobotsRootPath { get => _robotsRootPath; set => SetProperty(ref _robotsRootPath, value); }
        public string RobotTemplateName { get => _robotTemplateName; set => SetProperty(ref _robotTemplateName, value); }
        public bool ShowRobotCreator { get => _showRobotCreator; set => SetProperty(ref _showRobotCreator, value); }
        public string PackagesPath { get => _packagesPath; set => SetProperty(ref _packagesPath, value); }
        public string RulesPath { get => _rulesPath; set => SetProperty(ref _rulesPath, value); }
        public string SecurityAuditMode { get => _securityAuditMode; set => SetProperty(ref _securityAuditMode, value); }

        /// <summary>Combo index of <see cref="SecurityAuditMode"/>: 0 = Full, 1 = CredentialsOnly.</summary>
        public int SecurityAuditModeIndex
        {
            get => string.Equals(_securityAuditMode, "CredentialsOnly", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            set => SecurityAuditMode = value == 1 ? "CredentialsOnly" : "Full";
        }
        public string DeployProjectName { get => _deployProjectName; set => SetProperty(ref _deployProjectName, value); }
        public string LogProvidersPath { get => _logProvidersPath; set => SetProperty(ref _logProvidersPath, value); }
        public IReadOnlyList<string> RobotNameHistory => _robotNameHistory;
    }
}
