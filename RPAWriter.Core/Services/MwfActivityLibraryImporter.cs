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
    /// It reflects over every <c>Activities.*.dll</c> using <see cref="MetadataLoadContext"/>,
    /// extracts concrete public activities that derive from <c>System.Activities.Activity</c>
    /// together with their property signatures, and writes one JSON descriptor per activity
    /// into the library's "mwf" folder.
    /// </summary>
    public class MwfActivityLibraryImporter
    {
        private const string ActivityBaseFullName = "System.Activities.Activity";
        private const string ActivitiesSearchPattern = "Activities.*.dll";
        private const string CoreAssemblyName = "mscorlib";
        private const string FrameworkAssemblyName = "System.Activities";

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

            var existing = LoadExistingDescriptors(targetDirectory);
            var serializer = CreateSerializerSettings();
            var importedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var loadContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths), CoreAssemblyName))
                {
                    var activityBase = ResolveActivityBase(loadContext);
                    if (activityBase == null)
                    {
                        result.Errors.Add("Activity base type \"" + ActivityBaseFullName + "\" was not found in " + FrameworkAssemblyName + ".dll.");
                        return result;
                    }

                    foreach (var dll in assemblies)
                    {
                        var fileName = Path.GetFileName(dll);
                        result.DllsScanned++;

                        try
                        {
                            var assembly = loadContext.LoadFromAssemblyPath(dll);
                            var count = 0;
                            foreach (var type in GetConcreteActivities(assembly, activityBase))
                            {
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
                return result;
            }

            // A partial import must not delete descriptors it failed to refresh.
            if (result.FailedAssemblies.Count == 0)
                RemoveObsoleteDescriptors(targetDirectory, importedNames, result);

            return result;
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

        private static List<string> CollectResolverPaths(string activitiesDirectory)
        {
            var paths = new List<string>();
            paths.AddRange(Directory.GetFiles(activitiesDirectory, "*.dll"));

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

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

        private static IEnumerable<Type> GetConcreteActivities(Assembly assembly, Type activityBase)
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
                if (!DerivesFromActivity(type, activityBase))
                    continue;
                yield return type;
            }
        }

        private static bool DerivesFromActivity(Type type, Type activityBase)
        {
            try
            {
                if (activityBase != null && type == activityBase)
                    return false;

                for (var current = type.BaseType; current != null; current = current.BaseType)
                    if (string.Equals(current.FullName, ActivityBaseFullName, StringComparison.Ordinal))
                        return true;
            }
            catch
            {
                // Base type resolution can fail when a dependency is missing; treat the type as
                // a non-activity rather than aborting the whole import.
            }

            return false;
        }

        private static WfActivityDescriptor BuildDescriptor(Type type, string assemblyName, IDictionary<string, WfActivityDescriptor> existing)
        {
            var fullName = type.FullName ?? type.Name;
            existing.TryGetValue(fullName, out var previous);

            return new WfActivityDescriptor
            {
                ActivityFullName = fullName,
                DefaultDisplayName = previous != null && !string.IsNullOrEmpty(previous.DefaultDisplayName)
                    ? previous.DefaultDisplayName
                    : Humanize(ShortTypeName(fullName)),
                Description = previous?.Description,
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

                result.Add(new WfActivityParameter
                {
                    Name = property.Name,
                    Type = SimplifyTypeName(property.PropertyType)
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
        {
            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (keepNames.Contains(name))
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

