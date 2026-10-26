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
    /// Rebuilds the PIX activity library from the installed PIX assemblies. It reflects over
    /// <c>Core.dll</c> (base control-flow activities) and every <c>Activities.*.dll</c> using
    /// <see cref="MetadataLoadContext"/>, extracts concrete activities that derive from
    /// <c>BR.Core.Activity</c> together with their parameter signatures, and writes one JSON
    /// descriptor per activity into the library's "pix" folder.
    /// </summary>
    public class PixActivityLibraryImporter
    {
        private const string ActivityBaseFullName = "BR.Core.Activity";
        private const string CoreAssemblyFileName = "Core.dll";
        private const string ActivitiesSearchPattern = "Activities.*.dll";

        private sealed class StructuralMapping
        {
            public string Role;
            public string WfSnippet;
            public string Parent;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// Canonical WF mappings for the core PIX control-flow activities. These do not depend
        /// on the previous library state, so the converter keeps working even when the target
        /// directory is empty (the importer fully rewrites the dictionary).
        /// </summary>
        private static readonly Dictionary<string, StructuralMapping> BuiltInMappings =
            new Dictionary<string, StructuralMapping>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "BR.Core.Base.Container",
                    new StructuralMapping { Role = "container", WfSnippet = "Sequence" }
                },
                {
                    "BR.Core.Base.If",
                    new StructuralMapping { Role = "if", WfSnippet = "If" }
                },
                {
                    "BR.Core.Base.IfTrue",
                    new StructuralMapping { Role = "then", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.IfFalse",
                    new StructuralMapping { Role = "else", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.ElseIf",
                    new StructuralMapping { Role = "elseIf", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.TryCatch",
                    new StructuralMapping { Role = "tryCatch", WfSnippet = "TryCatch" }
                },
                {
                    "BR.Core.Base.Try",
                    new StructuralMapping { Role = "try", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Catch",
                    new StructuralMapping { Role = "catch", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Finally",
                    new StructuralMapping { Role = "finally", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Assign",
                    new StructuralMapping { Role = "statement", WfSnippet = "Assign" }
                },
                {
                    "BR.Core.Base.Sleep",
                    new StructuralMapping { Role = "statement", WfSnippet = "Delay" }
                },
                {
                    "BR.Core.Base.ThrowException",
                    new StructuralMapping { Role = "statement", WfSnippet = "Throw" }
                },
                {
                    "BR.Core.Base.Return",
                    new StructuralMapping
                    {
                        Role = "statement",
                        WfSnippet = "Return",
                        Warnings = new List<string> { "Return terminates the workflow; result value is not carried over." }
                    }
                }
            };

        /// <summary>
        /// Canonical parameter targets for the structural activities. These override the default
        /// "Argument" target so the converter maps expressions/variables correctly.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> BuiltInParameterTargets =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "BR.Core.Base.If",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Condition", "Expression" } }
                },
                {
                    "BR.Core.Base.ElseIf",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Condition", "Expression" } }
                },
                {
                    "BR.Core.Base.Assign",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "To", "Variable" }, { "Value", "Expression" } }
                },
                {
                    "BR.Core.Base.Sleep",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Count", "Expression" } }
                },
                {
                    "BR.Core.Base.ThrowException",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Exception", "Expression" } }
                }
            };

        /// <summary>
        /// Scans <paramref name="pixDirectory"/>, extracts every PIX activity and rewrites the
        /// descriptor library in <paramref name="targetDirectory"/>. Existing semantic mappings
        /// (role, snippet, display name, warnings and parameter targets) are preserved where an
        /// activity already exists; the parameter list itself is always refreshed from the DLLs.
        /// </summary>
        public PixActivityLibraryImportResult Import(string pixDirectory, string targetDirectory, Action<string> log = null)
        {
            var result = new PixActivityLibraryImportResult();
            log = log ?? (message => { });

            if (string.IsNullOrWhiteSpace(pixDirectory) || !Directory.Exists(pixDirectory))
            {
                result.Errors.Add("PIX directory does not exist: " + pixDirectory);
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

            var resolverPaths = CollectResolverPaths(pixDirectory);
            var assemblies = GetActivityAssemblies(pixDirectory);
            if (assemblies.Count == 0)
            {
                result.Errors.Add("No PIX activity assemblies found in " + pixDirectory);
                return result;
            }

            var existing = LoadExistingDescriptors(targetDirectory);
            var serializer = CreateSerializerSettings();
            var importedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var loadContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths), "System.Private.CoreLib"))
                {
                    var activityBase = ResolveActivityBase(loadContext, pixDirectory);
                    if (activityBase == null)
                    {
                        result.Errors.Add("Activity base type \"" + ActivityBaseFullName + "\" was not found in " + CoreAssemblyFileName + ".");
                        return result;
                    }

                    var defaultResourceType = activityBase.Assembly.GetType("BR.Properties.LocalizedStrings");

                    foreach (var dll in assemblies)
                    {
                        var fileName = Path.GetFileName(dll);
                        result.DllsScanned++;

                        Assembly assembly;
                        try
                        {
                            assembly = loadContext.LoadFromAssemblyPath(dll);
                        }
                        catch (Exception ex)
                        {
                            result.FailedAssemblies.Add(fileName);
                            result.Errors.Add("Failed to load " + fileName + ": " + ex.Message);
                            continue;
                        }

                        var count = 0;
                        foreach (var type in GetConcreteActivities(assembly, activityBase))
                        {
                            var descriptor = BuildDescriptor(type, existing, defaultResourceType);
                            WriteDescriptor(targetDirectory, serializer, descriptor, result);
                            importedNames.Add(descriptor.ActivityFullName);
                            result.ActivitiesImported++;
                            count++;
                        }

                        log("Scanned " + fileName + " — " + count + " activities.");
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


        private static Type ResolveActivityBase(MetadataLoadContext loadContext, string pixDirectory)
        {
            var corePath = Path.Combine(pixDirectory, CoreAssemblyFileName);
            if (!File.Exists(corePath))
                return null;

            var core = loadContext.LoadFromAssemblyPath(corePath);
            return core.GetType(ActivityBaseFullName);
        }

        private static List<string> GetActivityAssemblies(string pixDirectory)
        {
            var result = new List<string>();
            var corePath = Path.Combine(pixDirectory, CoreAssemblyFileName);
            if (File.Exists(corePath))
                result.Add(corePath);

            foreach (var file in Directory.GetFiles(pixDirectory, ActivitiesSearchPattern))
                result.Add(file);

            return result.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> CollectResolverPaths(string pixDirectory)
        {
            var paths = new List<string>();
            paths.AddRange(Directory.GetFiles(pixDirectory, "*.dll"));

            var runtimeDir = FindRuntimeDirectory();
            if (!string.IsNullOrWhiteSpace(runtimeDir))
                paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FindRuntimeDirectory()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.NETCore.App");

            if (!Directory.Exists(root))
                return null;

            var candidates = new List<string>();
            foreach (var dir in Directory.GetDirectories(root))
                if (File.Exists(Path.Combine(dir, "System.Private.CoreLib.dll")))
                    candidates.Add(dir);

            if (candidates.Count == 0)
                return null;

            var preferred = candidates.Where(d => Path.GetFileName(d).StartsWith("7.", StringComparison.Ordinal)).ToList();
            var pool = preferred.Count > 0 ? preferred : candidates;
            return pool.OrderByDescending(d => ParseVersion(Path.GetFileName(d))).First();
        }

        private static Version ParseVersion(string text)
        {
            return Version.TryParse(text, out var version) ? version : new Version(0, 0);
        }

        private static IEnumerable<Type> GetConcreteActivities(Assembly assembly, Type activityBase)
        {
            foreach (var type in assembly.GetExportedTypes())
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
            if (activityBase != null && type == activityBase)
                return false;

            for (var current = type.BaseType; current != null; current = current.BaseType)
                if (string.Equals(current.FullName, ActivityBaseFullName, StringComparison.Ordinal))
                    return true;

            return false;
        }

        private PixActivityDescriptor BuildDescriptor(Type type, IDictionary<string, PixActivityDescriptor> existing, Type defaultResourceType)
        {
            var fullName = type.FullName ?? type.Name;
            existing.TryGetValue(fullName, out var previous);

            var attributes = ActivityMetadata.ReadAttributes(type);
            var displayName = ReadLocalizedAttribute(attributes, "BR.Core.Attributes.LocalizableScreenName", defaultResourceType);
            var description = ReadLocalizedAttribute(attributes, "BR.Core.Attributes.LocalizableDescription", defaultResourceType);

            var parameters = ExtractParameters(type, defaultResourceType);
            ApplyTargets(fullName, parameters, previous);

            if (BuiltInMappings.TryGetValue(fullName, out var mapping))
            {
                return new PixActivityDescriptor
                {
                    ActivityFullName = fullName,
                    WfSnippet = mapping.WfSnippet,
                    DefaultDisplayName = displayName ?? FallbackDisplayName(previous, fullName),
                    Description = description ?? previous?.Description,
                    Role = mapping.Role ?? "statement",
                    Parent = mapping.Parent,
                    Parameters = parameters,
                    Warnings = mapping.Warnings ?? new List<string>()
                };
            }

            return new PixActivityDescriptor
            {
                ActivityFullName = fullName,
                WfSnippet = previous != null && !string.IsNullOrEmpty(previous.WfSnippet)
                    ? previous.WfSnippet
                    : "InvokeMethod",
                DefaultDisplayName = displayName ?? FallbackDisplayName(previous, fullName),
                Description = description ?? previous?.Description,
                Role = previous != null && !string.IsNullOrEmpty(previous.Role)
                    ? previous.Role
                    : "statement",
                Parent = previous?.Parent,
                Parameters = parameters,
                Warnings = previous?.Warnings ?? new List<string>()
            };
        }

        private static string FallbackDisplayName(PixActivityDescriptor previous, string fullName)
        {
            if (previous != null && !string.IsNullOrEmpty(previous.DefaultDisplayName))
                return previous.DefaultDisplayName;
            return Humanize(ShortTypeName(fullName));
        }

        private List<PixActivityParameter> ExtractParameters(Type type, Type defaultResourceType)
        {
            var result = new List<PixActivityParameter>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;
                if (!IsActivityParameterDeclaringType(property.DeclaringType))
                    continue;

                var attributes = ActivityMetadata.ReadAttributes(property);
                result.Add(new PixActivityParameter
                {
                    Pix = property.Name,
                    Target = "Argument",
                    Type = SimplifyTypeName(property.PropertyType),
                    DisplayName = ReadLocalizedAttribute(attributes, "BR.Core.Attributes.LocalizableScreenName", defaultResourceType),
                    Description = ReadLocalizedAttribute(attributes, "BR.Core.Attributes.LocalizableDescription", defaultResourceType),
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

        private static void ApplyTargets(string fullName, List<PixActivityParameter> parameters, PixActivityDescriptor previous)
        {
            var targetsByPix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Preserve custom target assignments from the previous library.
            if (previous?.Parameters != null)
                foreach (var p in previous.Parameters)
                    if (!string.IsNullOrEmpty(p.Pix) && !string.IsNullOrEmpty(p.Target))
                        targetsByPix[p.Pix] = p.Target;

            // Built-in canonical targets override previous values for structural activities.
            if (BuiltInParameterTargets.TryGetValue(fullName, out var overrides))
                foreach (var kv in overrides)
                    targetsByPix[kv.Key] = kv.Value;

            foreach (var parameter in parameters)
                if (targetsByPix.TryGetValue(parameter.Pix, out var target))
                    parameter.Target = target;
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


        private static void WriteDescriptor(string directory, JsonSerializerSettings settings, PixActivityDescriptor descriptor, PixActivityLibraryImportResult result)
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

        private static Dictionary<string, PixActivityDescriptor> LoadExistingDescriptors(string directory)
        {
            var result = new Dictionary<string, PixActivityDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var descriptor = JsonConvert.DeserializeObject<PixActivityDescriptor>(File.ReadAllText(file, Encoding.UTF8));
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

        private static void RemoveObsoleteDescriptors(string directory, ISet<string> keepNames, PixActivityLibraryImportResult result)
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

        private readonly Dictionary<string, Dictionary<string, string>> _resourceCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        private string ReadLocalizedAttribute(IList<CustomAttributeData> attributes, string attributeFullName, Type defaultResourceType)
        {
            foreach (var attribute in attributes ?? new List<CustomAttributeData>())
            {
                if (!string.Equals(attribute.AttributeType?.FullName, attributeFullName, StringComparison.Ordinal))
                    continue;

                var args = attribute.ConstructorArguments;
                if (args.Count == 0)
                    return null;

                var key = args[0].Value?.ToString();
                if (string.IsNullOrEmpty(key))
                    return null;

                Type resourceType = null;
                if (args.Count > 1)
                    resourceType = args[1].Value as Type;

                return ResolveResourceString(resourceType, key, defaultResourceType);
            }

            return null;
        }

        private string ResolveResourceString(Type resourceType, string key, Type defaultResourceType)
        {
            var effectiveType = resourceType ?? defaultResourceType;
            if (effectiveType == null || string.IsNullOrEmpty(key))
                return null;

            var resourceName = effectiveType.FullName + ".resources";
            if (string.IsNullOrEmpty(resourceName))
                return null;

            var values = GetResourceValues(effectiveType.Assembly, resourceName);
            return values != null && values.TryGetValue(key, out var value) ? value : null;
        }

        private Dictionary<string, string> GetResourceValues(Assembly assembly, string resourceName)
        {
            var cacheKey = (assembly?.FullName ?? string.Empty) + "|" + resourceName;
            if (_resourceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (var stream = assembly?.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new System.Resources.ResourceReader(stream))
                        {
                            foreach (System.Collections.DictionaryEntry entry in reader)
                            {
                                var entryKey = entry.Key as string;
                                if (entryKey == null)
                                    continue;
                                result[entryKey] = entry.Value as string;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Unreadable resource stream — fall back to the empty map.
            }

            _resourceCache[cacheKey] = result;
            return result;
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



