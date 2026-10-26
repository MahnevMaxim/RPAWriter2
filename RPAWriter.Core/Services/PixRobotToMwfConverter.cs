using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Converts a whole PIX robot (a folder of .pix scripts plus an optional .pixproj) into a
    /// Workflow Foundation (MWF) robot: one .xaml workflow per involved script (Main.xaml first),
    /// plus a single aggregated RpaActions.cs helper and a text report.
    /// </summary>
    public class PixRobotToMwfConverter
    {
        private const string ExecuteScriptActivityFullName = "BR.Core.Base.ExecuteScript";
        private const string MainPixFileName = "main.pix";
        private const string MainXamlFileName = "Main.xaml";

        private readonly PixActivityLibrary _pixLibrary;
        private readonly WfSnippetLibrary _snippets;
        private readonly IReadOnlyDictionary<string, string> _mappings;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PixMwfArgumentMapping>> _argumentMappings;

        /// <summary>
        /// When true, a script aborts (and the whole conversion fails) as soon as an activity has
        /// neither a PIX → MWF mapping nor a built-in WF snippet. Defaults to false.
        /// </summary>
        public bool FailOnUnmapped { get; set; }

        /// <summary>
        /// Optional sink for step-by-step conversion tracing (discovery, per-script conversion,
        /// and output writing).
        /// </summary>
        public Action<string> Log { get; set; }

        private void LogInfo(string message)
            => Log?.Invoke(message);

        public PixRobotToMwfConverter()
            : this(new PixActivityLibrary(), new WfSnippetLibrary(), null, null)
        {
        }

        public PixRobotToMwfConverter(PixActivityLibrary pixLibrary, WfSnippetLibrary snippets)
            : this(pixLibrary, snippets, null, null)
        {
        }

        public PixRobotToMwfConverter(
            PixActivityLibrary pixLibrary,
            WfSnippetLibrary snippets,
            IReadOnlyDictionary<string, string> pixToMwfMappings,
            IReadOnlyDictionary<string, IReadOnlyList<PixMwfArgumentMapping>> argumentMappings)
        {
            _pixLibrary = pixLibrary ?? throw new ArgumentNullException(nameof(pixLibrary));
            _snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));
            _mappings = pixToMwfMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _argumentMappings = argumentMappings ??
                new Dictionary<string, IReadOnlyList<PixMwfArgumentMapping>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts every involved PIX script into a Workflow Foundation result without writing
        /// any files. Use <see cref="ConvertAndWrite"/> to also emit the MWF robot files.
        /// </summary>
        public PixRobotConversionResult Convert(string pixFolder)
        {
            if (string.IsNullOrWhiteSpace(pixFolder))
                throw new ArgumentNullException(nameof(pixFolder));

            var warnings = new List<string>();
            var result = new PixRobotConversionResult
            {
                RobotName = ResolveRobotName(pixFolder)
            };

            LogInfo("PIX robot → MWF conversion started.");
            LogInfo("PIX folder: " + pixFolder);
            LogInfo("Robot name: " + (result.RobotName ?? "(unknown)"));

            var scripts = DiscoverScripts(pixFolder, warnings);
            LogInfo("Scripts to convert: " + scripts.Count);
            foreach (var s in scripts)
                LogInfo("- " + s.FileName + (s.IsMain ? "  (main → Main.xaml)" : "  → " + ResolveOutputFileName(s)) + "  [" + s.FullPath + "]");

            var converter = new PixToWfConverter(_pixLibrary, _snippets, _mappings, _argumentMappings)
            {
                FailOnUnmapped = FailOnUnmapped,
                Log = Log
            };
            var plans = new List<RobotPlan>();

            foreach (var script in scripts)
            {
                LogInfo("=== Converting script " + script.FileName + " → " + ResolveOutputFileName(script) + " ===");
                PixConversionResult converted;
                try
                {
                    converted = converter.Convert(File.ReadAllText(script.FullPath));
                }
                catch (PixConversionException ex)
                {
                    throw new PixConversionException(
                        "Conversion aborted in script \"" + script.FileName + "\": " + ex.Message, ex);
                }

                plans.Add(converted.Plan);

                result.Scripts.Add(new PixScriptConversionResult
                {
                    SourceFileName = script.FileName,
                    OutputFileName = ResolveOutputFileName(script),
                    Xaml = converted.Xaml,
                    CSharpCode = converted.CSharpCode,
                    Warnings = converted.Warnings,
                    Mappings = converted.Mappings
                });

                foreach (var w in converted.Warnings)
                    result.Warnings.Add(script.FileName + ": " + w);
                foreach (var m in converted.Mappings)
                    result.AppliedMappings.Add(script.FileName + ": " + m);

                LogInfo("Script " + script.FileName + " converted: " + converted.Mappings.Count +
                        " mapping(s), " + converted.Warnings.Count + " warning(s).");
            }

            foreach (var w in warnings)
                result.Warnings.Add(w);

            LogInfo("Building aggregate C# helper code (RpaActions.cs)...");
            result.AggregateCSharpCode = BuildAggregateHelperCode(plans, result.RobotName);
            LogInfo("Conversion finished: " + result.Scripts.Count + " script(s).");
            return result;
        }

        /// <summary>
        /// Converts the PIX robot and writes the generated .xaml workflows, RpaActions.cs and
        /// conversion-report.txt into <paramref name="mwfFolder"/>.
        /// </summary>
        public PixRobotConversionResult ConvertAndWrite(string pixFolder, string mwfFolder)
        {
            var result = Convert(pixFolder);
            WriteOutput(result, mwfFolder);
            return result;
        }

        /// <summary>Writes the generated artifacts of a conversion result into the MWF folder.</summary>
        public void WriteOutput(PixRobotConversionResult result, string mwfFolder)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(mwfFolder))
                throw new ArgumentNullException(nameof(mwfFolder));

            Directory.CreateDirectory(mwfFolder);
            LogInfo("Writing output to: " + mwfFolder);

            foreach (var script in result.Scripts)
            {
                var path = Path.Combine(mwfFolder, script.OutputFileName);
                File.WriteAllText(path, script.Xaml ?? string.Empty, new UTF8Encoding(false));
                LogInfo("- Wrote " + script.OutputFileName + " (" + (script.Xaml?.Length ?? 0) + " chars).");
            }

            if (!string.IsNullOrWhiteSpace(result.AggregateCSharpCode))
            {
                File.WriteAllText(Path.Combine(mwfFolder, "RpaActions.cs"), result.AggregateCSharpCode, new UTF8Encoding(false));
                LogInfo("- Wrote RpaActions.cs (" + result.AggregateCSharpCode.Length + " chars).");
            }

            File.WriteAllText(Path.Combine(mwfFolder, "conversion-report.txt"), BuildReport(result, mwfFolder), new UTF8Encoding(false));
            LogInfo("- Wrote conversion-report.txt.");
        }

        private List<PixScriptFile> DiscoverScripts(string pixFolder, IList<string> warnings)
        {
            var fromProject = DiscoverFromPixproj(pixFolder, warnings);
            if (fromProject != null)
            {
                LogInfo("Script discovery: using .pixproj file list.");
                return fromProject;
            }

            LogInfo("Script discovery: no .pixproj found; following ExecuteScript references from main.pix.");
            return DiscoverByReferences(pixFolder, warnings);
        }

        /// <summary>Reads the .pixproj file list (and IsMain flags); returns null when there is no .pixproj.</summary>
        private List<PixScriptFile> DiscoverFromPixproj(string pixFolder, IList<string> warnings)
        {
            string projPath;
            try
            {
                projPath = Directory.GetFiles(pixFolder, "*.pixproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            catch
            {
                projPath = null;
            }

            if (string.IsNullOrEmpty(projPath))
                return null;

            var result = new List<PixScriptFile>();
            try
            {
                var filesElement = XDocument.Load(projPath).Root?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Files");

                foreach (var item in filesElement?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    if (item.Name.LocalName != "ProjectFileItem")
                        continue;

                    var path = ReadChildValue(item, "Path");
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(pixFolder, path);
                    if (!File.Exists(fullPath))
                    {
                        warnings.Add("Project file \"" + path + "\" referenced by .pixproj does not exist; skipped.");
                        continue;
                    }

                    result.Add(new PixScriptFile
                    {
                        FileName = Path.GetFileName(fullPath),
                        FullPath = fullPath,
                        IsMain = string.Equals(ReadChildValue(item, "IsMain"), "true", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read .pixproj file: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Fallback discovery: start from main.pix and follow ExecuteScript references. If there is
        /// no main.pix, all .pix files are converted in name order.
        /// </summary>
        private List<PixScriptFile> DiscoverByReferences(string pixFolder, IList<string> warnings)
        {
            List<PixScriptFile> all;
            try
            {
                all = Directory.GetFiles(pixFolder, "*.pix", SearchOption.AllDirectories)
                    .Select(f => new PixScriptFile
                    {
                        FileName = Path.GetFileName(f),
                        FullPath = f,
                        IsMain = string.Equals(Path.GetFileName(f), MainPixFileName, StringComparison.OrdinalIgnoreCase)
                    })
                    .ToList();
            }
            catch
            {
                all = new List<PixScriptFile>();
            }

            if (all.Count == 0)
                return all;

            var main = all.FirstOrDefault(f => f.IsMain);
            if (main == null)
            {
                warnings.Add("No main.pix and no .pixproj found; converting all .pix files in name order.");
                return all.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var byClass = new Dictionary<string, PixScriptFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in all)
            {
                var cls = PixToWfConverter.SafeClassName(Path.GetFileNameWithoutExtension(file.FileName));
                if (!string.IsNullOrEmpty(cls) && !byClass.ContainsKey(cls))
                    byClass[cls] = file;
            }

            var result = new List<PixScriptFile>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<PixScriptFile>();
            queue.Enqueue(main);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current.FullPath))
                    continue;

                result.Add(current);

                foreach (var reference in ExtractScriptReferences(File.ReadAllText(current.FullPath)))
                {
                    var cls = PixToWfConverter.ResolveScriptName(reference);
                    if (cls == null)
                        continue;

                    if (byClass.TryGetValue(cls, out var target))
                        queue.Enqueue(target);
                    else
                        warnings.Add("ExecuteScript references unknown script \"" + cls + "\" in " + current.FileName + ".");
                }
            }

            return result;
        }

        private static IEnumerable<string> ExtractScriptReferences(string pixXml)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(pixXml))
                return result;

            try
            {
                var doc = XDocument.Parse(pixXml);
                foreach (var step in doc.Descendants().Where(e => e.Name.LocalName == "AbstractStep"))
                {
                    if (!string.Equals(ReadChildValue(step, "_activityFullName"), ExecuteScriptActivityFullName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var filePath = ReadPropertyValue(step, "FilePath");
                    if (!string.IsNullOrWhiteSpace(filePath))
                        result.Add(filePath);
                }
            }
            catch
            {
                // Unparsable script - no references collected.
            }

            return result;
        }

        private static string ResolveOutputFileName(PixScriptFile script)
        {
            if (script.IsMain)
                return MainXamlFileName;

            var cls = PixToWfConverter.SafeClassName(Path.GetFileNameWithoutExtension(script.FileName));
            return (string.IsNullOrEmpty(cls) ? "Script" : cls) + ".xaml";
        }

        private static string ResolveRobotName(string pixFolder)
        {
            try
            {
                var projPath = Directory.GetFiles(pixFolder, "*.pixproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrEmpty(projPath))
                {
                    var name = ReadChildValue(XDocument.Load(projPath).Root, "Name");
                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
            catch
            {
                // Fall through to the folder name.
            }

            return new DirectoryInfo(pixFolder).Name;
        }

        private static string BuildAggregateHelperCode(IEnumerable<RobotPlan> plans, string robotName)
        {
            var methods = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
            foreach (var plan in plans ?? Enumerable.Empty<RobotPlan>())
            {
                foreach (var node in CollectInvokeMethodNodes(plan?.Root))
                {
                    if (!string.IsNullOrWhiteSpace(node.MethodName) && !methods.ContainsKey(node.MethodName))
                        methods[node.MethodName] = node;
                }
            }

            var combined = new RobotPlan
            {
                Name = robotName ?? "Robot",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = methods.Values.ToList()
                }
            };

            return new RobotGenerator().EmitHelperCode(combined);
        }

        private static IEnumerable<ActivityNode> CollectInvokeMethodNodes(ActivityNode node)
        {
            var result = new List<ActivityNode>();
            CollectInvokeMethodNodes(node, result);
            return result;
        }

        private static void CollectInvokeMethodNodes(ActivityNode node, List<ActivityNode> result)
        {
            if (node == null)
                return;

            if (node.Kind == ActivityNodeKind.InvokeMethod)
                result.Add(node);

            foreach (var child in node.Children ?? new List<ActivityNode>())
                CollectInvokeMethodNodes(child, result);
            foreach (var child in node.ElseChildren ?? new List<ActivityNode>())
                CollectInvokeMethodNodes(child, result);
            foreach (var child in node.CatchChildren ?? new List<ActivityNode>())
                CollectInvokeMethodNodes(child, result);
            foreach (var child in node.FinallyChildren ?? new List<ActivityNode>())
                CollectInvokeMethodNodes(child, result);
        }

        private static string BuildReport(PixRobotConversionResult result, string outputFolder)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PIX robot to MWF conversion report");
            sb.AppendLine("Robot: " + (result.RobotName ?? string.Empty));
            sb.AppendLine("Output folder: " + outputFolder);
            sb.AppendLine("Scripts generated: " + result.Scripts.Count);
            sb.AppendLine();
            foreach (var s in result.Scripts)
                sb.AppendLine("- " + s.SourceFileName + " -> " + s.OutputFileName);

            if (result.AppliedMappings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Applied mappings:");
                foreach (var m in result.AppliedMappings)
                    sb.AppendLine("- " + m);
            }

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in result.Warnings)
                    sb.AppendLine("- " + w);
            }

            return sb.ToString();
        }

        private static string ReadPropertyValue(XElement step, string propertyName)
        {
            var propertyValues = step?.Elements().FirstOrDefault(e => e.Name.LocalName == "PropertyValues");
            foreach (var pv in propertyValues?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (pv.Name.LocalName != "PropertyValue")
                    continue;
                if (!string.Equals(ReadChildValue(pv, "PropertyName"), propertyName, StringComparison.Ordinal))
                    continue;

                var expr = ReadChildValue(pv, "_expression");
                if (!string.IsNullOrWhiteSpace(expr))
                    return expr;

                var value = ReadChildValue(pv, "_value");
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        private static string ReadChildValue(XElement parent, string localName)
            => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

        private sealed class PixScriptFile
        {
            public string FileName;
            public string FullPath;
            public bool IsMain;
        }
    }
}

