using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Persisted document describing the discovered built-in <c>System.Activities</c> activities and
    /// the user-selected subset that should be imported into the MWF activity library.
    /// </summary>
    public class SystemActivitiesState
    {
        /// <summary>Descriptors discovered by reflecting over <c>System.Activities.dll</c>.</summary>
        public List<WfActivityDescriptor> Activities { get; set; } = new List<WfActivityDescriptor>();

        /// <summary>Full names of the activities selected for import (the filter).</summary>
        public List<string> SelectedFullNames { get; set; } = new List<string>();
    }

    /// <summary>
    /// Loads/saves the System.Activities discovery and selection at
    /// <c>%AppData%\RPAWriter2\system-activities.json</c> so both survive Visual Studio sessions.
    /// </summary>
    public class SystemActivitiesStore
    {
        /// <summary>Default on-disk location of the discovery/selection document.</summary>
        public static string DefaultFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RPAWriter2",
            "system-activities.json");

        private readonly string _filePath;

        public SystemActivitiesStore() : this(DefaultFilePath)
        {
        }

        public SystemActivitiesStore(string filePath)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
        }

        /// <summary>Full path of the file this store reads and writes.</summary>
        public string FilePath => _filePath;

        /// <summary>Reads the persisted state, or an empty state when none exists yet.</summary>
        public SystemActivitiesState Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath, Encoding.UTF8);
                    var state = JsonConvert.DeserializeObject<SystemActivitiesState>(json) ?? new SystemActivitiesState();
                    EnsureDefaults(state);
                    return state;
                }
            }
            catch
            {
                // ignore read errors — fall back to an empty state
            }

            var defaults = new SystemActivitiesState();
            EnsureDefaults(defaults);
            return defaults;
        }

        /// <summary>Returns the cached discovery, or an empty list when none was saved yet.</summary>
        public IReadOnlyList<WfActivityDescriptor> GetActivities()
        {
            return Load().Activities;
        }

        /// <summary>Returns the cached filter as a case-insensitive set of activity full names.</summary>
        public ISet<string> GetSelectedNames()
        {
            return new HashSet<string>(Load().SelectedFullNames, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Persists the discovered activity list. The selection is preserved for activities that still
        /// exist; on the very first discovery (no previously saved activities) every activity is selected.
        /// </summary>
        public void SaveDiscovery(IReadOnlyList<WfActivityDescriptor> activities)
        {
            var list = (activities ?? new List<WfActivityDescriptor>()).ToList();
            var known = new HashSet<string>(
                list.Select(a => a.ActivityFullName).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            var state = Load();
            var firstDiscovery = state.Activities.Count == 0;
            state.Activities = list;

            if (firstDiscovery)
                state.SelectedFullNames = known.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            else
                state.SelectedFullNames = state.SelectedFullNames
                    .Where(n => known.Contains(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            Save(state);
        }

        /// <summary>Persists only the user's selection (the filter).</summary>
        public void SaveFilter(IEnumerable<string> selectedFullNames)
        {
            var state = Load();
            state.SelectedFullNames = (selectedFullNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save(state);
        }

        private void Save(SystemActivitiesState state)
        {
            if (state == null)
                return;

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(state, Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
                // ignore write errors
            }
        }

        private static void EnsureDefaults(SystemActivitiesState state)
        {
            if (state.Activities == null)
                state.Activities = new List<WfActivityDescriptor>();
            if (state.SelectedFullNames == null)
                state.SelectedFullNames = new List<string>();
        }
    }
}
