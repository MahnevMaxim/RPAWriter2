using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Rebuilds the Workflow Foundation activity library from C# source files instead of
    /// compiled assemblies. It understands two source styles:
    /// - "airpa": classes annotated with <c>LocalizedDisplayName</c>/<c>LocalizedDescription</c>
    ///   attributes whose strings are resolved from a sibling <c>.ru-RU.resx</c> file;
    /// - "mwf": classes that declare a <c>DispName</c> property (the display name) and a
    ///   <c>[Description("...")]</c> attribute (the description).
    /// </summary>
    public class MwfActivitySourceImporter
    {
        private static readonly Regex NamespaceRegex =
            new Regex(@"namespace\s+([A-Za-z_][\w.]*)", RegexOptions.Compiled);

        private static readonly Regex ClassRegex =
            new Regex(@"\b(?<mods>(?:public|internal|private|protected|sealed|static|abstract|partial|readonly)\s+)*class\s+(?<name>\w+)\s*(?::\s*(?<base>[^{]+?))?\s*\{", RegexOptions.Compiled);

        private static readonly Regex AttributeRegex =
            new Regex(@"\[(?<name>[A-Za-z_]\w*)\s*(?:\((?<args>[^\[\]]*)\))?\s*\]", RegexOptions.Compiled);

        private static readonly Regex ArgumentPropertyRegex =
            new Regex(@"\b(?<wrapper>InArgument|OutArgument|InOutArgument)\s*<\s*(?<type>[^>]+?)\s*>\s+(?<name>\w+)\s*\{[^}]*\}", RegexOptions.Compiled);

        private static readonly Regex DispNameArrowRegex =
            new Regex(@"DispName\s*=>\s*""([^""]*)""", RegexOptions.Compiled);

        private static readonly Regex DispNamePropertyRegex =
            new Regex(@"DispName\s*\{[^}]*\}\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        private static readonly Regex DispNameFieldRegex =
            new Regex(@"DispName\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// True when the directory contains C# sources (or a project file), i.e. it should be
        /// imported from source rather than reflected over compiled assemblies.
        /// </summary>
        public static bool IsSourceDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return false;

            try
            {
                return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Any(p => !IsExcludedPath(p))
                    || Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Any(p => !IsExcludedPath(p));
            }
            catch
            {
                return false;
            }
        }

        private static List<string> GetSourceFiles(string root)
        {
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !IsExcludedPath(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsExcludedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var objMarker = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
            var binMarker = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
            return path.IndexOf(objMarker, StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf(binMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scans <paramref name="sourceDirectory"/> for activity classes and rewrites the
        /// descriptor library in <paramref name="targetDirectory"/>.
        /// </summary>
        public WfActivityLibraryImportResult Import(string sourceDirectory, string targetDirectory, Action<string> log = null)
        {
            var result = new WfActivityLibraryImportResult();
            log = log ?? (message => { });

            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                result.Errors.Add("Source directory does not exist: " + sourceDirectory);
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

            var files = GetSourceFiles(sourceDirectory);
            if (files.Count == 0)
            {
                result.Errors.Add("No C# source files found in " + sourceDirectory);
                return result;
            }

            var resxIndex = IndexResxFiles(sourceDirectory);
            var existing = LoadExistingDescriptors(targetDirectory);
            var serializer = CreateSerializerSettings();
            var importedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                result.SourceFilesScanned++;

                string text;
                try
                {
                    text = File.ReadAllText(file, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Failed to read " + file + ": " + ex.Message);
                    continue;
                }

                foreach (var activity in ParseActivities(text, file))
                {
                    var descriptor = BuildDescriptor(activity, resxIndex, existing);
                    WriteDescriptor(targetDirectory, serializer, descriptor, result);
                    importedNames.Add(descriptor.ActivityFullName);
                    result.ActivitiesImported++;
                }
            }

            // A partial import must not delete descriptors it failed to refresh.
            if (result.Errors.Count == 0 && result.ActivitiesImported > 0)
                RemoveObsoleteDescriptors(targetDirectory, importedNames, result);

            return result;
        }

        private static Dictionary<string, string> IndexResxFiles(string root)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.resx", SearchOption.AllDirectories))
                {
                    if (IsExcludedPath(file))
                        continue;
                    index[Path.GetFileName(file)] = file;
                }
            }
            catch
            {
                // Unreadable directory — fall back to the empty index.
            }

            return index;
        }

        private static string ResolveLocalized(string key, string resourceType, Dictionary<string, string> resxIndex)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var shortName = ShortResourceName(resourceType);
            if (string.IsNullOrWhiteSpace(shortName))
                return null;

            string file;
            if (resxIndex.TryGetValue(shortName + ".ru-RU.resx", out file)
                || resxIndex.TryGetValue(shortName + ".resx", out file))
            {
                return ReadResxValue(file, key);
            }

            return null;
        }

        private static string ShortResourceName(string resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
                return null;

            var name = resourceType.Trim();
            var dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);
            return name;
        }

        private static string ReadResxValue(string path, string key)
        {
            try
            {
                var document = XDocument.Load(path);
                if (document.Root == null)
                    return null;

                foreach (var data in document.Root.Elements("data"))
                {
                    if (!string.Equals((string)data.Attribute("name"), key, StringComparison.Ordinal))
                        continue;

                    var value = data.Element("value");
                    return value?.Value;
                }
            }
            catch
            {
                // Unreadable/malformed resource file — treat as "not found".
            }

            return null;
        }

        private static List<ParsedActivity> ParseActivities(string text, string filePath)
        {
            var result = new List<ParsedActivity>();

            var namespaceMatch = NamespaceRegex.Match(text);
            var ns = namespaceMatch.Success ? namespaceMatch.Groups[1].Value : string.Empty;

            foreach (Match classMatch in ClassRegex.Matches(text))
            {
                var mods = classMatch.Groups["mods"].Value;
                if (mods.IndexOf("abstract", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var baseType = (classMatch.Groups["base"].Value ?? string.Empty).Split(',')[0].Trim();
                if (!IsActivityBaseType(baseType))
                    continue;

                var className = classMatch.Groups["name"].Value;
                var classPrefix = text.Substring(0, classMatch.Index);
                var body = text.Substring(classMatch.Index + classMatch.Length);

                var classAttributes = ParseAttributes(classPrefix);
                var localizedDisplay = classAttributes.FirstOrDefault(a => a.Name == "LocalizedDisplayName");
                var localizedDescription = classAttributes.FirstOrDefault(a => a.Name == "LocalizedDescription");
                var descriptionAttribute = classAttributes.FirstOrDefault(a => a.Name == "Description");

                var activity = new ParsedActivity
                {
                    ClassName = className,
                    FullName = string.IsNullOrEmpty(ns) ? className : ns + "." + className,
                    BaseType = baseType,
                    FilePath = filePath,
                    IsAirpa = localizedDisplay != null || localizedDescription != null
                };

                if (activity.IsAirpa)
                {
                    activity.DisplayNameKey = localizedDisplay?.StringValue;
                    activity.DisplayNameResourceType = localizedDisplay?.ResourceType;
                    activity.DescriptionKey = localizedDescription?.StringValue;
                    activity.DescriptionResourceType = localizedDescription?.ResourceType;
                }
                else
                {
                    activity.DispName = ExtractDispName(body);
                    activity.Description = descriptionAttribute?.StringValue;
                }

                activity.Parameters = ExtractParameters(body, activity.IsAirpa);
                result.Add(activity);
            }

            return result;
        }

        private static List<ParsedAttribute> ParseAttributes(string text)
        {
            var result = new List<ParsedAttribute>();
            foreach (Match match in AttributeRegex.Matches(text))
                result.Add(ParseAttribute(match));
            return result;
        }

        private static ParsedAttribute ParseAttribute(Match match)
        {
            var attribute = new ParsedAttribute { Name = match.Groups["name"].Value };
            var args = match.Groups["args"].Value;
            if (string.IsNullOrWhiteSpace(args))
                return attribute;

            var stringMatch = Regex.Match(args, "\"([^\"]*)\"");
            if (stringMatch.Success)
                attribute.StringValue = stringMatch.Groups[1].Value;

            var typeMatch = Regex.Match(args, @"typeof\s*\(\s*([^)]+?)\s*\)");
            if (typeMatch.Success)
                attribute.ResourceType = typeMatch.Groups[1].Value.Trim();

            return attribute;
        }

        private static bool IsActivityBaseType(string baseType)
        {
            if (string.IsNullOrWhiteSpace(baseType))
                return false;

            var name = baseType;
            var generic = name.IndexOf('<');
            if (generic >= 0)
                name = name.Substring(0, generic);

            var dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);

            return name.EndsWith("Activity", StringComparison.Ordinal);
        }

        private static string ExtractDispName(string body)
        {
            var match = DispNameArrowRegex.Match(body);
            if (match.Success)
                return match.Groups[1].Value;

            match = DispNamePropertyRegex.Match(body);
            if (match.Success)
                return match.Groups[1].Value;

            match = DispNameFieldRegex.Match(body);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static List<ParsedParameter> ExtractParameters(string body, bool isAirpa)
        {
            var result = new List<ParsedParameter>();
            var matches = ArgumentPropertyRegex.Matches(body);
            var previousEnd = 0;
            foreach (Match match in matches)
            {
                var segment = body.Substring(previousEnd, match.Index - previousEnd);
                var attributes = ParseAttributes(segment);

                var displayNameAttribute = attributes.FirstOrDefault(a => a.Name == "DisplayName");
                var descriptionAttribute = attributes.FirstOrDefault(a => a.Name == "Description");
                var localizedDisplay = attributes.FirstOrDefault(a => a.Name == "LocalizedDisplayName");
                var localizedDescription = attributes.FirstOrDefault(a => a.Name == "LocalizedDescription");
                var defaultAttribute = attributes.FirstOrDefault(a => a.Name == "DefaultValue");

                result.Add(new ParsedParameter
                {
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value.Trim(),
                    Direction = DirectionFromWrapper(match.Groups["wrapper"].Value),
                    IsRequired = attributes.Any(a => a.Name == "RequiredArgument" || a.Name == "Required"),
                    DefaultValue = defaultAttribute?.StringValue,
                    DisplayName = isAirpa ? null : displayNameAttribute?.StringValue,
                    Description = isAirpa ? null : descriptionAttribute?.StringValue,
                    DisplayNameKey = isAirpa ? localizedDisplay?.StringValue : null,
                    DisplayNameResourceType = isAirpa ? localizedDisplay?.ResourceType : null,
                    DescriptionKey = isAirpa ? localizedDescription?.StringValue : null,
                    DescriptionResourceType = isAirpa ? localizedDescription?.ResourceType : null
                });

                previousEnd = match.Index + match.Length;
            }

            return result;
        }

        private static string DirectionFromWrapper(string wrapper)
        {
            switch (wrapper)
            {
                case "InArgument": return "In";
                case "OutArgument": return "Out";
                case "InOutArgument": return "InOut";
                default: return string.Empty;
            }
        }

        private static WfActivityDescriptor BuildDescriptor(ParsedActivity activity, Dictionary<string, string> resxIndex, IDictionary<string, WfActivityDescriptor> existing)
        {
            existing.TryGetValue(activity.FullName, out var previous);

            string displayName;
            string description;

            if (activity.IsAirpa)
            {
                displayName = ResolveLocalized(activity.DisplayNameKey, activity.DisplayNameResourceType, resxIndex)
                              ?? previous?.DefaultDisplayName
                              ?? Humanize(activity.ClassName);
                description = ResolveLocalized(activity.DescriptionKey, activity.DescriptionResourceType, resxIndex)
                              ?? previous?.Description
                              ?? string.Empty;
            }
            else
            {
                displayName = string.IsNullOrWhiteSpace(activity.DispName) ? Humanize(activity.ClassName) : activity.DispName;
                description = activity.Description ?? string.Empty;
            }

            var parameters = new List<WfActivityParameter>();
            foreach (var parameter in activity.Parameters)
            {
                string parameterDisplayName;
                string parameterDescription;
                if (activity.IsAirpa)
                {
                    parameterDisplayName = ResolveLocalized(parameter.DisplayNameKey, parameter.DisplayNameResourceType, resxIndex) ?? parameter.Name;
                    parameterDescription = ResolveLocalized(parameter.DescriptionKey, parameter.DescriptionResourceType, resxIndex) ?? string.Empty;
                }
                else
                {
                    parameterDisplayName = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName;
                    parameterDescription = parameter.Description ?? string.Empty;
                }

                parameters.Add(new WfActivityParameter
                {
                    Name = parameter.Name,
                    Type = parameter.Type,
                    DisplayName = parameterDisplayName,
                    Description = parameterDescription,
                    Direction = parameter.Direction,
                    IsRequired = parameter.IsRequired,
                    DefaultValue = parameter.DefaultValue
                });
            }

            return new WfActivityDescriptor
            {
                ActivityFullName = activity.FullName,
                DefaultDisplayName = displayName,
                Description = description,
                BaseType = activity.BaseType,
                AssemblyName = FindProjectName(activity.FilePath),
                Parameters = parameters
            };
        }

        private static string FindProjectName(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                try
                {
                    var project = Directory.GetFiles(directory, "*.csproj").FirstOrDefault();
                    if (project != null)
                        return Path.GetFileNameWithoutExtension(project);
                }
                catch
                {
                    break;
                }

                var parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                    break;
                directory = parent;
            }

            return Path.GetFileName(Path.GetDirectoryName(filePath));
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

                // Preserve framework activities (System.Activities.*) that may have been
                // imported earlier by the DLL importer; the source importer only manages
                // activities discovered in the user's C# sources.
                if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
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

        private static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var text = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
            text = Regex.Replace(text, "([A-Z])([A-Z][a-z])", "$1 $2");
            return text;
        }

        private sealed class ParsedActivity
        {
            public string ClassName;
            public string FullName;
            public string BaseType;
            public string FilePath;
            public bool IsAirpa;
            public string DispName;
            public string Description;
            public string DisplayNameKey;
            public string DisplayNameResourceType;
            public string DescriptionKey;
            public string DescriptionResourceType;
            public List<ParsedParameter> Parameters = new List<ParsedParameter>();
        }

        private sealed class ParsedParameter
        {
            public string Name;
            public string Type;
            public string Direction;
            public bool IsRequired;
            public string DefaultValue;
            public string DisplayName;
            public string Description;
            public string DisplayNameKey;
            public string DisplayNameResourceType;
            public string DescriptionKey;
            public string DescriptionResourceType;
        }

        private sealed class ParsedAttribute
        {
            public string Name;
            public string StringValue;
            public string ResourceType;
        }
    }
}
