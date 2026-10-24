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
            public string DisplayName;
            public string Parent;
            public List<string> Warnings = new List<string>();
        }

        private sealed class LocalizedInfo
        {
            public string DisplayName;
            public string Description;
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
                    new StructuralMapping { Role = "container", WfSnippet = "Sequence", DisplayName = "Container" }
                },
                {
                    "BR.Core.Base.If",
                    new StructuralMapping { Role = "if", WfSnippet = "If", DisplayName = "If" }
                },
                {
                    "BR.Core.Base.IfTrue",
                    new StructuralMapping { Role = "then", DisplayName = "Then", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.IfFalse",
                    new StructuralMapping { Role = "else", DisplayName = "Else", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.ElseIf",
                    new StructuralMapping { Role = "elseIf", DisplayName = "Else If", Parent = "BR.Core.Base.If" }
                },
                {
                    "BR.Core.Base.TryCatch",
                    new StructuralMapping { Role = "tryCatch", WfSnippet = "TryCatch", DisplayName = "Try Catch" }
                },
                {
                    "BR.Core.Base.Try",
                    new StructuralMapping { Role = "try", DisplayName = "Try", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Catch",
                    new StructuralMapping { Role = "catch", DisplayName = "Catch", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Finally",
                    new StructuralMapping { Role = "finally", DisplayName = "Finally", Parent = "BR.Core.Base.TryCatch" }
                },
                {
                    "BR.Core.Base.Assign",
                    new StructuralMapping { Role = "statement", WfSnippet = "Assign", DisplayName = "Assign" }
                },
                {
                    "BR.Core.Base.Sleep",
                    new StructuralMapping { Role = "statement", WfSnippet = "Delay", DisplayName = "Sleep" }
                },
                {
                    "BR.Core.Base.ThrowException",
                    new StructuralMapping { Role = "statement", WfSnippet = "Throw", DisplayName = "Throw exception" }
                },
                {
                    "BR.Core.Base.Return",
                    new StructuralMapping
                    {
                        Role = "statement",
                        WfSnippet = "Return",
                        DisplayName = "Return",
                        Warnings = new List<string> { "Return terminates the workflow; result value is not carried over." }
                    }
                }
            };

        /// <summary>
        /// Russian display names and descriptions for the known PIX activities. The importer writes
        /// these into every generated descriptor; unknown activities keep their previous values or
        /// fall back to a humanized type name.
        /// </summary>
        private static readonly Dictionary<string, LocalizedInfo> RussianActivityInfo =
            new Dictionary<string, LocalizedInfo>(StringComparer.OrdinalIgnoreCase)
            {
                { "BR.Core.Base.Container", new LocalizedInfo { DisplayName = "Контейнер", Description = "Контейнер шагов: выполняет вложенные действия последовательно." } },
                { "BR.Core.Base.If", new LocalizedInfo { DisplayName = "Если", Description = "Условный оператор: выполняет ветку по заданному условию." } },
                { "BR.Core.Base.IfTrue", new LocalizedInfo { DisplayName = "Тогда", Description = "Ветка «тогда» условного оператора." } },
                { "BR.Core.Base.IfFalse", new LocalizedInfo { DisplayName = "Иначе", Description = "Ветка «иначе» условного оператора." } },
                { "BR.Core.Base.ElseIf", new LocalizedInfo { DisplayName = "Иначе если", Description = "Дополнительное условие «иначе если»." } },
                { "BR.Core.Base.TryCatch", new LocalizedInfo { DisplayName = "Попытка/перехват", Description = "Обработка исключений: блоки try, catch и finally." } },
                { "BR.Core.Base.Try", new LocalizedInfo { DisplayName = "Попытка", Description = "Начало блока try." } },
                { "BR.Core.Base.Catch", new LocalizedInfo { DisplayName = "Перехват", Description = "Обработчик исключения (блок catch)." } },
                { "BR.Core.Base.Finally", new LocalizedInfo { DisplayName = "Наконец", Description = "Блок finally, выполняемый в любом случае." } },
                { "BR.Core.Base.Assign", new LocalizedInfo { DisplayName = "Присвоить", Description = "Присваивает переменной значение выражения." } },
                { "BR.Core.Base.Sleep", new LocalizedInfo { DisplayName = "Пауза", Description = "Приостанавливает выполнение на заданное время." } },
                { "BR.Core.Base.ThrowException", new LocalizedInfo { DisplayName = "Выбросить исключение", Description = "Выбрасывает исключение и прерывает выполнение." } },
                { "BR.Core.Base.Return", new LocalizedInfo { DisplayName = "Возврат", Description = "Завершает workflow (значение результата не переносится)." } },
                { "BR.Core.Base.ChangeDateTime", new LocalizedInfo { DisplayName = "Изменить дату/время", Description = "Изменяет дату и время на заданную величину." } },
                { "BR.Core.Base.CompareDateTimes", new LocalizedInfo { DisplayName = "Сравнить даты", Description = "Сравнивает две даты/времени." } },
                { "BR.Core.Base.DateTimeToString", new LocalizedInfo { DisplayName = "Дата в строку", Description = "Преобразует дату/время в строку по формату." } },
                { "BR.Core.Base.ExecuteScript", new LocalizedInfo { DisplayName = "Выполнить скрипт", Description = "Выполняет скрипт." } },
                { "BR.Core.Base.GetDateTime", new LocalizedInfo { DisplayName = "Получить дату/время", Description = "Возвращает текущую дату и время." } },
                { "BR.BaseActivities.ConditionalGoTo", new LocalizedInfo { DisplayName = "Условный переход", Description = "Переходит к шагу по условию." } },
                { "BR.BaseActivities.GoToStep", new LocalizedInfo { DisplayName = "Перейти к шагу", Description = "Переходит к указанному шагу." } },
                { "Activities.BaseCollections.List.Add", new LocalizedInfo { DisplayName = "Добавить в список", Description = "Добавляет элемент в список." } },
                { "Activities.BaseCollections.List.Clear", new LocalizedInfo { DisplayName = "Очистить список", Description = "Удаляет все элементы из списка." } },
                { "Activities.BaseCollections.List.Contains", new LocalizedInfo { DisplayName = "Список содержит", Description = "Проверяет, содержит ли список элемент." } },
                { "Activities.BaseCollections.List.CreateList", new LocalizedInfo { DisplayName = "Создать список", Description = "Создаёт новый список." } },
                { "Activities.BaseCollections.List.OutputListAsString", new LocalizedInfo { DisplayName = "Список в строку", Description = "Преобразует список в строку." } },
                { "Activities.BaseCollections.List.Remove", new LocalizedInfo { DisplayName = "Удалить из списка", Description = "Удаляет элемент из списка." } },
                { "Activities.VBS.ExecuteVBS", new LocalizedInfo { DisplayName = "Выполнить VBS", Description = "Выполняет скрипт VBS." } }
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
                            var descriptor = BuildDescriptor(type, existing);
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

        private static PixActivityDescriptor BuildDescriptor(Type type, IDictionary<string, PixActivityDescriptor> existing)
        {
            var fullName = type.FullName ?? type.Name;
            existing.TryGetValue(fullName, out var previous);
            RussianActivityInfo.TryGetValue(fullName, out var localized);

            var parameters = ExtractParameters(type);
            ApplyTargets(fullName, parameters, previous);

            if (BuiltInMappings.TryGetValue(fullName, out var mapping))
            {
                return new PixActivityDescriptor
                {
                    ActivityFullName = fullName,
                    WfSnippet = mapping.WfSnippet,
                    DefaultDisplayName = localized?.DisplayName ?? mapping.DisplayName,
                    Description = localized?.Description,
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
                DefaultDisplayName = localized?.DisplayName
                    ?? (previous != null && !string.IsNullOrEmpty(previous.DefaultDisplayName)
                        ? previous.DefaultDisplayName
                        : Humanize(ShortTypeName(fullName))),
                Description = localized?.Description ?? previous?.Description,
                Role = previous != null && !string.IsNullOrEmpty(previous.Role)
                    ? previous.Role
                    : "statement",
                Parent = previous?.Parent,
                Parameters = parameters,
                Warnings = previous?.Warnings ?? new List<string>()
            };
        }

        private static List<PixActivityParameter> ExtractParameters(Type type)
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
                    DisplayName = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DisplayNameAttribute"),
                    Description = ActivityMetadata.ReadStringAttribute(attributes, "System.ComponentModel.DescriptionAttribute"),
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



