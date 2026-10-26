using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Rebuilds the Workflow Foundation activity library from the selected activities folder.
    /// It reflects over every <c>Activities.*.dll</c> plus the built-in <c>System.Activities.dll</c>
    /// using <see cref="MetadataLoadContext"/>, extracts concrete public activities that derive from
    /// <c>System.Activities.Activity</c> together with their property signatures, and writes one JSON
    /// descriptor per activity into the library's "mwf" folder.
    /// </summary>
    public class MwfActivityLibraryImporter
    {
        private const string ActivityBaseFullName = "System.Activities.Activity";
        private const string ActivitiesSearchPattern = "Activities.*.dll";
        private const string CoreAssemblyName = "mscorlib";
        private const string FrameworkAssemblyName = "System.Activities";

        /// <summary>Base type of Flowchart's structural nodes (FlowStep, FlowDecision, FlowSwitch&lt;T&gt;).</summary>
        private const string FlowNodeBaseFullName = "System.Activities.Statements.FlowNode";

        /// <summary>Base type of TryCatch handlers (Catch&lt;TException&gt;).</summary>
        private const string CatchBaseFullName = "System.Activities.Statements.Catch";

        /// <summary>
        /// Scans <paramref name="activitiesDirectory"/>, extracts every WF activity and rewrites
        /// the descriptor library in <paramref name="targetDirectory"/>. Existing display names and
        /// descriptions are preserved where an activity already exists; the parameter list itself is
        /// always refreshed from the DLLs.
        /// </summary>
        public WfActivityLibraryImportResult Import(string activitiesDirectory, string targetDirectory, Action<string> log = null)
        {
            var result = new WfActivityLibraryImportResult();
            log = log ?? (message => { });

            if (string.IsNullOrWhiteSpace(activitiesDirectory) || !Directory.Exists(activitiesDirectory))
            {
                result.Errors.Add("Activities directory does not exist: " + activitiesDirectory);
                return result;
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                result.Errors.Add("Target library directory is empty.");
                return result;
            }

            try
            {
                Directory.CreateDirectory(targetDirectory);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Cannot create target directory " + targetDirectory + ": " + ex.Message);
                return result;
            }

            var resolverPaths = CollectResolverPaths(activitiesDirectory);
            var assemblies = GetActivityAssemblies(activitiesDirectory);
            if (assemblies.Count == 0)
            {
                result.Errors.Add("No WF activity assemblies found in " + activitiesDirectory);
                return result;
            }

            // The built-in System.Activities.dll ships with the .NET Framework and contains the
            // standard WF activities (Sequence, Flowchart, Assign, WriteLine, Delay, etc.). Scan it
            // alongside the user-provided Activities.*.dll assemblies so the imported library also
            // covers the framework activities. The path is taken from the already-collected resolver
            // paths, which guarantees it is the same file the resolver returns for the
            // "System.Activities" assembly name (avoiding an "already been loaded" error).
            var systemActivitiesPath = FindSystemActivitiesPath(resolverPaths);
            if (!string.IsNullOrWhiteSpace(systemActivitiesPath))
                assemblies.Add(systemActivitiesPath);

            ImportAssemblyPaths(assemblies, resolverPaths, targetDirectory, log, result);
            return result;
        }

        /// <summary>
        /// Discovers the built-in Workflow Foundation activities shipped inside
        /// <c>System.Activities.dll</c> and returns their descriptors without writing anything to the
        /// activity library. This is the read-only counterpart of <see cref="ImportFrameworkActivities"/>:
        /// the Converter tool window caches the result so the user can pick which framework activities
        /// to import.
        /// </summary>
        public IReadOnlyList<WfActivityDescriptor> DiscoverFrameworkActivities(Action<string> log = null)
        {
            var result = new List<WfActivityDescriptor>();
            log = log ?? (message => { });

            var resolverPaths = CollectFrameworkResolverPaths();
            var systemActivitiesPath = FindSystemActivitiesPath(resolverPaths);
            if (string.IsNullOrWhiteSpace(systemActivitiesPath))
            {
                log(FrameworkAssemblyName + ".dll was not found on this machine.");
                return result;
            }

            try
            {
                using (var loadContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths.ToList()), CoreAssemblyName))
                {
                    var activityBase = ResolveActivityBase(loadContext);
                    if (activityBase == null)
                    {
                        log("Activity base type \"" + ActivityBaseFullName + "\" was not found in " + FrameworkAssemblyName + ".dll.");
                        return result;
                    }

                    var assembly = loadContext.LoadFromAssemblyPath(systemActivitiesPath);
                    var existing = new Dictionary<string, WfActivityDescriptor>(StringComparer.OrdinalIgnoreCase);
                    foreach (var type in GetConcreteActivities(assembly, activityBase, includeWorkflowElements: true))
                        result.Add(BuildDescriptor(type, Path.GetFileName(systemActivitiesPath), existing));

                    log("Discovered " + result.Count + " framework activities in " + Path.GetFileName(systemActivitiesPath) + ".");
                }
            }
            catch (Exception ex)
            {
                log("Failed to discover framework activities: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Imports the built-in Workflow Foundation activities from <c>System.Activities.dll</c>.
        /// When <paramref name="selectedFullNames"/> is null every framework activity is imported
        /// (backwards-compatible behaviour); otherwise only the activities whose full names are in the
        /// set are written, and stale <c>System.*</c> descriptors outside that set are removed. Used
        /// together with <see cref="MwfActivitySourceImporter"/> when the selected folder contains C#
        /// sources instead of compiled <c>Activities.*.dll</c> assemblies.
        /// </summary>
        public WfActivityLibraryImportResult ImportFrameworkActivities(string targetDirectory, ISet<string> selectedFullNames = null, Action<string> log = null)
        {
            var result = new WfActivityLibraryImportResult();
            log = log ?? (message => { });

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                result.Errors.Add("Target library directory is empty.");
                return result;
            }

            try
            {
                Directory.CreateDirectory(targetDirectory);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Cannot create target directory " + targetDirectory + ": " + ex.Message);
                return result;
            }

            var resolverPaths = CollectFrameworkResolverPaths();
            var systemActivitiesPath = FindSystemActivitiesPath(resolverPaths);
            if (string.IsNullOrWhiteSpace(systemActivitiesPath))
            {
                result.Errors.Add(FrameworkAssemblyName + ".dll was not found on this machine.");
                return result;
            }

            Func<Type, bool> activityFilter = selectedFullNames == null
                ? null
                : type => selectedFullNames.Contains(type.FullName);

            ImportAssemblyPaths(
                new[] { systemActivitiesPath },
                resolverPaths,
                targetDirectory,
                log,
                result,
                name => name.StartsWith("System.", StringComparison.OrdinalIgnoreCase),
                activityFilter,
                includeWorkflowElements: true);
            return result;
        }

        /// <summary>
        /// Reflects over the given assemblies in a single <see cref="MetadataLoadContext"/>, writes one
        /// descriptor per concrete WF activity and removes stale descriptors afterwards. Obsolete-file
        /// cleanup is skipped when any assembly failed to load, so a partial import never deletes
        /// descriptors it could not refresh. <paramref name="obsoleteFilter"/> restricts cleanup to the
        /// descriptors it returns true for; null removes every stale descriptor.
        /// </summary>
        private static void ImportAssemblyPaths(
            IEnumerable<string> assemblyPaths,
            IEnumerable<string> resolverPaths,
            string targetDirectory,
            Action<string> log,
            WfActivityLibraryImportResult result,
            Func<string, bool> obsoleteFilter = null,
            Func<Type, bool> activityFilter = null,
            bool includeWorkflowElements = false)
        {
            var existing = LoadExistingDescriptors(targetDirectory);
            var serializer = CreateSerializerSettings();
            var importedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var loadContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths.ToList()), CoreAssemblyName))
                {
                    var activityBase = ResolveActivityBase(loadContext);
                    if (activityBase == null)
                    {
                        result.Errors.Add("Activity base type \"" + ActivityBaseFullName + "\" was not found in " + FrameworkAssemblyName + ".dll.");
                        return;
                    }

                    foreach (var dll in assemblyPaths)
                    {
                        var fileName = Path.GetFileName(dll);
                        result.DllsScanned++;

                        try
                        {
                            var assembly = loadContext.LoadFromAssemblyPath(dll);
                            var count = 0;
                            foreach (var type in GetConcreteActivities(assembly, activityBase, includeWorkflowElements))
                            {
                                if (activityFilter != null && !activityFilter(type))
                                    continue;

                                var descriptor = BuildDescriptor(type, fileName, existing);
                                WriteDescriptor(targetDirectory, serializer, descriptor, result);
                                importedNames.Add(descriptor.ActivityFullName);
                                result.ActivitiesImported++;
                                count++;
                            }

                            log("Scanned " + fileName + " — " + count + " activities.");
                        }
                        catch (Exception ex)
                        {
                            result.FailedAssemblies.Add(fileName);
                            result.Errors.Add("Failed to scan " + fileName + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add("Reflection failed: " + ex.Message);
                return;
            }

            // A partial import must not delete descriptors it failed to refresh.
            if (result.FailedAssemblies.Count == 0)
                RemoveObsoleteDescriptors(targetDirectory, importedNames, obsoleteFilter ?? (name => true), result);
        }

        private static Type ResolveActivityBase(MetadataLoadContext loadContext)
        {
            var assembly = loadContext.LoadFromAssemblyName(FrameworkAssemblyName);
            return assembly.GetType(ActivityBaseFullName);
        }

        private static List<string> GetActivityAssemblies(string activitiesDirectory)
        {
            return Directory.GetFiles(activitiesDirectory, ActivitiesSearchPattern)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the path to <c>System.Activities.dll</c> from the resolver paths. The first match
        /// (case-insensitive by file name) is the same copy that <see cref="MetadataLoadContext"/>
        /// resolves for <see cref="FrameworkAssemblyName"/>, so loading it by path afterwards is safe.
        /// </summary>
        private static string FindSystemActivitiesPath(IEnumerable<string> resolverPaths)
        {
            return resolverPaths.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), FrameworkAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> CollectResolverPaths(string activitiesDirectory)
        {
            var paths = new List<string>();
            paths.AddRange(Directory.GetFiles(activitiesDirectory, "*.dll"));
            AddFrameworkAndReferencePaths(paths);
            return DeduplicateByAssemblyName(paths);
        }

        /// <summary>
        /// Collects resolver paths for the framework-only import (<see cref="FrameworkAssemblyName"/>.dll):
        /// the .NET Framework runtime directory, its WPF subfolder and the reference-assemblies folder.
        /// </summary>
        private static List<string> CollectFrameworkResolverPaths()
        {
            var paths = new List<string>();
            AddFrameworkAndReferencePaths(paths);
            return DeduplicateByAssemblyName(paths);
        }

        private static void AddFrameworkAndReferencePaths(List<string> paths)
        {
            var frameworkDir = FindFrameworkDirectory();
            if (!string.IsNullOrWhiteSpace(frameworkDir))
            {
                paths.AddRange(Directory.GetFiles(frameworkDir, "*.dll"));

                // WPF assemblies (PresentationFramework.dll, PresentationCore.dll) live in a
                // nested "WPF" folder of the .NET Framework runtime directory.
                var wpfDir = Path.Combine(frameworkDir, "WPF");
                if (Directory.Exists(wpfDir))
                    paths.AddRange(Directory.GetFiles(wpfDir, "*.dll"));
            }

            AddReferenceAssemblies(paths);
        }

        /// <summary>
        /// Keeps only the first path for each assembly simple name. The framework runtime
        /// directory and the reference-assemblies folder both contain e.g. mscorlib.dll and
        /// System.Activities.dll with identical identities but different Mvids; feeding two
        /// copies into <see cref="PathAssemblyResolver"/> makes <see cref="MetadataLoadContext"/>
        /// throw "already been loaded into this MetadataLoadContext" while resolving the core
        /// assembly. Deduplicating by file name (the resolver's own lookup key) leaves exactly
        /// one candidate per assembly.
        /// </summary>
        private static List<string> DeduplicateByAssemblyName(IEnumerable<string> paths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length == 0 || !seen.Add(name))
                    continue;

                result.Add(path);
            }

            return result;
        }

        private static void AddReferenceAssemblies(List<string> paths)
        {
            // Reference assemblies keep PresentationFramework, System.Xaml, WindowsBase and
            // System.Activities together in one folder; prefer them when they are installed.
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrWhiteSpace(programFilesX86))
                return;

            var referenceRoot = Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework");
            if (!Directory.Exists(referenceRoot))
                return;

            foreach (var versionDir in Directory.GetDirectories(referenceRoot, "v4*"))
                paths.AddRange(Directory.GetFiles(versionDir, "*.dll"));
        }

        private static string FindFrameworkDirectory()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows))
                return null;

            var candidates = new[]
            {
                Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319"),
                Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319")
            };

            foreach (var candidate in candidates)
                if (File.Exists(Path.Combine(candidate, CoreAssemblyName + ".dll")))
                    return candidate;

            return null;
        }

        private static IEnumerable<Type> GetConcreteActivities(Assembly assembly, Type activityBase, bool includeWorkflowElements = false)
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }
            catch (FileNotFoundException)
            {
                // A referenced assembly (e.g. PresentationFramework) could not be resolved while
                // enumerating the exported types; fall back to the defined types so the remaining
                // activities are still imported.
                try
                {
                    types = assembly.DefinedTypes.Select(t => t.AsType()).ToArray();
                }
                catch
                {
                    yield break;
                }
            }

            foreach (var type in types)
            {
                if (type.IsAbstract)
                    continue;
                if (DerivesFromActivity(type, activityBase))
                {
                    yield return type;
                    continue;
                }
                if (includeWorkflowElements && IsWorkflowElement(type))
                    yield return type;
            }
        }

        /// <summary>
        /// True for non-abstract structural workflow elements that are not themselves
        /// <c>System.Activities.Activity</c> but still appear in a workflow definition:
        /// Flowchart nodes (FlowStep/FlowDecision/FlowSwitch&lt;T&gt;), StateMachine elements
        /// (State/Transition), Pick's PickBranch and TryCatch's Catch&lt;TException&gt;.
        /// </summary>
        private static bool IsWorkflowElement(Type type)
        {
            if (type == null)
                return false;

            if (DerivesFrom(type, FlowNodeBaseFullName))
                return true;
            if (DerivesFrom(type, CatchBaseFullName))
                return true;

            switch (type.FullName)
            {
                case "System.Activities.Statements.State":
                case "System.Activities.Statements.Transition":
                case "System.Activities.Statements.PickBranch":
                    return true;
                default:
                    return false;
            }
        }

        private static bool DerivesFromActivity(Type type, Type activityBase)
        {
            try
            {
                if (activityBase != null && type == activityBase)
                    return false;

                return DerivesFrom(type, ActivityBaseFullName);
            }
            catch
            {
                // Base type resolution can fail when a dependency is missing; treat the type as
                // a non-activity rather than aborting the whole import.
            }

            return false;
        }

        private static bool DerivesFrom(Type type, string baseFullName)
        {
            if (type == null || string.IsNullOrEmpty(baseFullName))
                return false;

            try
            {
                for (var current = type.BaseType; current != null; current = current.BaseType)
                    if (string.Equals(current.FullName, baseFullName, StringComparison.Ordinal))
                        return true;
            }
            catch
            {
                // Base type resolution can fail when a dependency is missing; treat the type as
                // not deriving from the requested base type.
            }

            return false;
        }

        private static WfActivityDescriptor BuildDescriptor(Type type, string assemblyName, IDictionary<string, WfActivityDescriptor> existing)
        {
            var fullName = type.FullName ?? type.Name;
            existing.TryGetValue(fullName, out var previous);

            var attributes = ActivityMetadata.ReadAttributes(type);
            var reflectedDisplayName = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DisplayNameAttribute");
            var reflectedDescription = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DescriptionAttribute");

            return new WfActivityDescriptor
            {
                ActivityFullName = fullName,
                DefaultDisplayName = !string.IsNullOrEmpty(reflectedDisplayName)
                    ? reflectedDisplayName
                    : previous != null && !string.IsNullOrEmpty(previous.DefaultDisplayName)
                        ? previous.DefaultDisplayName
                        : Humanize(ShortTypeName(fullName)),
                Description = !string.IsNullOrEmpty(reflectedDescription)
                    ? reflectedDescription
                    : previous?.Description,
                BaseType = type.BaseType?.FullName,
                AssemblyName = assemblyName,
                Parameters = ExtractParameters(type)
            };
        }

        private static List<WfActivityParameter> ExtractParameters(Type type)
        {
            var result = new List<WfActivityParameter>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;
                if (!IsActivityParameterDeclaringType(property.DeclaringType))
                    continue;

                var attributes = ActivityMetadata.ReadAttributes(property);
                result.Add(new WfActivityParameter
                {
                    Name = property.Name,
                    Type = SimplifyTypeName(ActivityMetadata.ExtractValueType(property.PropertyType)),
                    DisplayName = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DisplayNameAttribute"),
                    Description = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DescriptionAttribute"),
                    Direction = ActivityMetadata.ExtractDirection(property.PropertyType),
                    IsRequired = ActivityMetadata.HasAttribute(attributes, "System.ComponentModel.DataAnnotations.RequiredAttribute"),
                    DefaultValue = ActivityMetadata.ReadDefaultValue(attributes, "System.ComponentModel.DefaultValueAttribute")
                });
            }

            return result;
        }

        private static bool IsActivityParameterDeclaringType(Type declaringType)
        {
            if (declaringType == null)
                return false;
            if (string.Equals(declaringType.FullName, ActivityBaseFullName, StringComparison.Ordinal))
                return false;
            if (string.Equals(declaringType.FullName, "System.Object", StringComparison.Ordinal))
                return false;

            for (var current = declaringType.BaseType; current != null; current = current.BaseType)
                if (string.Equals(current.FullName, ActivityBaseFullName, StringComparison.Ordinal))
                    return true;

            return false;
        }

        private static string SimplifyTypeName(Type type)
        {
            if (type.IsArray)
                return SimplifyTypeName(type.GetElementType()) + "[]";

            if (type.IsGenericType)
            {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                var args = string.Join(", ", type.GetGenericArguments().Select(SimplifyTypeName));
                return name + "<" + args + ">";
            }

            return type.FullName ?? type.Name;
        }

        private static void WriteDescriptor(string directory, JsonSerializerSettings settings, WfActivityDescriptor descriptor, WfActivityLibraryImportResult result)
        {
            var path = Path.Combine(directory, descriptor.ActivityFullName + ".json");
            try
            {
                var json = JsonConvert.SerializeObject(descriptor, settings);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                result.FilesWritten++;
            }
            catch (Exception ex)
            {
                result.Errors.Add("Failed to write " + path + ": " + ex.Message);
            }
        }

        private static Dictionary<string, WfActivityDescriptor> LoadExistingDescriptors(string directory)
        {
            var result = new Dictionary<string, WfActivityDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var descriptor = JsonConvert.DeserializeObject<WfActivityDescriptor>(File.ReadAllText(file, Encoding.UTF8));
                    if (descriptor != null && !string.IsNullOrWhiteSpace(descriptor.ActivityFullName))
                        result[descriptor.ActivityFullName] = descriptor;
                }
                catch
                {
                    // Unreadable descriptor — skip; it is regenerated if the activity still exists.
                }
            }

            return result;
        }

        private static void RemoveObsoleteDescriptors(string directory, ISet<string> keepNames, WfActivityLibraryImportResult result)
            => RemoveObsoleteDescriptors(directory, keepNames, name => true, result);

        private static void RemoveObsoleteDescriptors(string directory, ISet<string> keepNames, Func<string, bool> include, WfActivityLibraryImportResult result)
        {
            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (keepNames.Contains(name))
                    continue;
                if (!include(name))
                    continue;

                try
                {
                    File.Delete(file);
                    result.FilesDeleted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Failed to delete obsolete descriptor " + file + ": " + ex.Message);
                }
            }
        }

        private static JsonSerializerSettings CreateSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include
            };
        }

        private static string ShortTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;
            var index = fullName.LastIndexOf('.');
            return index < 0 ? fullName : fullName.Substring(index + 1);
        }

        private static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var text = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
            text = Regex.Replace(text, "([A-Z])([A-Z][a-z])", "$1 $2");
            return text;
        }
    }
}

