using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using EnvDTE;
using EnvDTE80;

namespace RPAWriter2.Services
{
    /// <summary>Metadata for a single NuGet package file (.nupkg) found in the local packages folder.</summary>
    public class PackageInfo
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string Authors { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public bool IsInstalled { get; set; }
        public string InstalledVersion { get; set; } = "";
    }

    /// <summary>List row for the Packages tab (one row per package, versions selected via a combo box).</summary>
    public class PackageListItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _selectedVersion = "";

        public PackageListItem(PackageInfo info, IReadOnlyList<string> versions)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Versions = versions ?? new List<string>();
        }

        public PackageInfo Info { get; }

        /// <summary>Available versions of this package, sorted ascending.</summary>
        public IReadOnlyList<string> Versions { get; }

        public string Id => Info.Id;
        public string Description => Info.Description;
        public string Authors => Info.Authors;
        public bool IsInstalled => Info.IsInstalled;

        public string InstalledBadge => Info.IsInstalled
            ? "installed v" + Info.InstalledVersion
            : "";

        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (_selectedVersion == value) return;
                _selectedVersion = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Installed packages and robot project names collected from the open solution.</summary>
    public class RobotProjectState
    {
        public List<string> Names { get; } = new List<string>();

        public Dictionary<string, string> Installed { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Scans a local NuGet source folder and matches packages against the open RPA- robot project.</summary>
    public class PackageCatalogService
    {
        // Case-sensitive prefix: only projects named exactly "RPA-..." are managed.
        private const string RobotProjectPrefix = "RPA-";

        /// <summary>Reads all *.nupkg files from the folder and extracts their metadata.</summary>
        public Task<List<PackageInfo>> ScanAsync(string folder)
            => Task.Run(() => ScanCore(folder));

        private static List<PackageInfo> ScanCore(string folder)
        {
            var result = new List<PackageInfo>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return result;

            foreach (var file in SafeEnumerateFiles(folder, "*.nupkg", SearchOption.AllDirectories))
            {
                var info = ReadNupkg(file);
                if (info != null)
                    result.Add(info);
            }

            return result
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PackageInfo? ReadNupkg(string file)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(file))
                {
                    var nuspec = archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                    if (nuspec == null)
                        return null;

                    using (var stream = nuspec.Open())
                    {
                        var doc = XDocument.Load(stream);
                        var metadata = doc.Root?.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "metadata");
                        if (metadata == null)
                            return null;

                        return new PackageInfo
                        {
                            Id = ReadChildValue(metadata, "id"),
                            Version = ReadChildValue(metadata, "version"),
                            Description = ReadChildValue(metadata, "description"),
                            Authors = ReadChildValue(metadata, "authors"),
                            SourceFile = file
                        };
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ReadChildValue(XElement parent, string localName)
            => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? "";

        /// <summary>
        /// Collects the RPA- robot project names and their installed packages from the open solution.
        /// Must be called on the UI thread because it accesses EnvDTE.
        /// </summary>
        public RobotProjectState GetRobotProjectState(DTE2 dte)
        {
            var state = new RobotProjectState();
            if (dte?.Solution == null)
                return state;

            try
            {
                if (!dte.Solution.IsOpen)
                    return state;
            }
            catch
            {
                return state;
            }

            try
            {
                foreach (Project project in dte.Solution.Projects)
                    CollectProject(project, state);
            }
            catch
            {
                // The projects collection may be unavailable while the solution is still loading.
            }

            return state;
        }

        private static void CollectProject(Project project, RobotProjectState state)
        {
            if (project == null)
                return;

            if (!string.IsNullOrEmpty(project.Name) &&
                project.Name.StartsWith(RobotProjectPrefix, StringComparison.Ordinal))
            {
                state.Names.Add(project.Name);

                string? dir = null;
                try { dir = Path.GetDirectoryName(project.FullName); }
                catch { dir = null; }

                if (!string.IsNullOrEmpty(dir))
                {
                    ReadPackagesConfig(Path.Combine(dir, "packages.config"), state.Installed);
                    var csproj = SafeEnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (!string.IsNullOrEmpty(csproj))
                        ReadPackageReferences(csproj, state.Installed);
                }
            }

            if (project.ProjectItems == null)
                return;

            foreach (ProjectItem item in project.ProjectItems)
            {
                Project? sub = null;
                try { sub = item.SubProject; }
                catch { sub = null; }
                if (sub != null)
                    CollectProject(sub, state);
            }
        }

        private static void ReadPackagesConfig(string path, Dictionary<string, string> installed)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var doc = XDocument.Load(path);
                foreach (var package in doc.Descendants().Where(e => e.Name.LocalName == "package"))
                {
                    var id = package.Attribute("id")?.Value;
                    var version = package.Attribute("version")?.Value;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                        installed[id] = version;
                }
            }
            catch
            {
                // Ignore an unreadable packages.config.
            }
        }

        private static void ReadPackageReferences(string csprojPath, Dictionary<string, string> installed)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
                {
                    var id = reference.Attribute("Include")?.Value ?? reference.Attribute("Update")?.Value;
                    var version = reference.Attribute("Version")?.Value;
                    if (string.IsNullOrWhiteSpace(version))
                        version = reference.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                        installed[id] = version;
                }
            }
            catch
            {
                // Ignore an unreadable project file.
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option)
        {
            try { return Directory.EnumerateFiles(path, pattern, option); }
            catch { return new string[0]; }
        }
    }
}
