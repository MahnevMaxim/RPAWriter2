using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Converts a PIX robot (.pix XML) into plain Workflow Foundation using the
    /// activity library: PIX descriptors drive the mapping, WF snippets produce XAML.
    /// </summary>
    public class PixToWfConverter : IPixConverter
    {
        private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
        private const string ExecuteScriptActivityFullName = "BR.Core.Base.ExecuteScript";

        private readonly PixActivityLibrary _pixLibrary;
        private readonly SnippetWorkflowEmitter _emitter;
        private readonly RobotGenerator _generator;
        private readonly IReadOnlyDictionary<string, string> _mappings;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PixMwfArgumentMapping>> _argumentMappings;
        private readonly List<string> _appliedMappings = new List<string>();
        private int _logDepth;

        /// <summary>
        /// When true, conversion stops with a <see cref="PixConversionException"/> as soon as an
        /// activity is encountered that has neither a PIX → MWF mapping entry nor a built-in WF
        /// snippet (Assign/Delay/WriteLine/Throw/Return). Defaults to false for backwards compatibility.
        /// </summary>
        public bool FailOnUnmapped { get; set; }

        /// <summary>
        /// Optional sink for step-by-step conversion tracing. Each message describes a single
        /// phase or activity decision so the whole conversion can be followed in the UI/log.
        /// </summary>
        public Action<string> Log { get; set; }

        private void LogInfo(string message)
            => Log?.Invoke(new string(' ', _logDepth * 2) + message);

        public PixToWfConverter() : this(new PixActivityLibrary(), new WfSnippetLibrary(), null, null)
        {
        }

        public PixToWfConverter(PixActivityLibrary pixLibrary, WfSnippetLibrary snippets)
            : this(pixLibrary, snippets, null, null)
        {
        }

        /// <summary>
        /// Creates a converter that also honors a user/AI-managed PIX → MWF activity mapping.
        /// Each entry maps a PIX activity full name to a Workflow Foundation activity full name.
        /// A null or empty collection keeps the built-in snippet mapping only.
        /// </summary>
        public PixToWfConverter(
            PixActivityLibrary pixLibrary,
            WfSnippetLibrary snippets,
            IReadOnlyDictionary<string, string> pixToMwfMappings)
            : this(pixLibrary, snippets, pixToMwfMappings, null)
        {
        }

        /// <summary>
        /// Creates a converter that also honors argument-level mappings (PIX argument → MWF
        /// argument with an optional conversion), keyed by PIX activity full name.
        /// </summary>
        public PixToWfConverter(
            PixActivityLibrary pixLibrary,
            WfSnippetLibrary snippets,
            IReadOnlyDictionary<string, string> pixToMwfMappings,
            IReadOnlyDictionary<string, IReadOnlyList<PixMwfArgumentMapping>> argumentMappings)
        {
            _pixLibrary = pixLibrary ?? throw new ArgumentNullException(nameof(pixLibrary));
            _emitter = new SnippetWorkflowEmitter(snippets ?? throw new ArgumentNullException(nameof(snippets)));
            _generator = new RobotGenerator();
            _mappings = pixToMwfMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _argumentMappings = argumentMappings ?? new Dictionary<string, IReadOnlyList<PixMwfArgumentMapping>>(StringComparer.OrdinalIgnoreCase);
        }

        public PixConversionResult Convert(string pixXml)
        {
            var warnings = new List<string>();

            LogInfo("Parsing PIX script and building the activity tree...");
            var plan = ConvertToPlan(pixXml, warnings);

            LogInfo("Activity tree:\n```mermaid\n" + RobotPlanMermaidRenderer.Render(plan) + "\n```");

            LogInfo("Emitting Workflow Foundation XAML (" + CountActivities(plan.Root) + " activities)...");
            var xaml = _emitter.EmitWorkflowXaml(plan);
            LogInfo("Raw generated XAML:\n```xml\n" + xaml + "\n```");

            LogInfo("Emitting C# helper code...");
            var csharp = _generator.EmitHelperCode(plan);

            LogInfo("Script converted: " + _appliedMappings.Count + " mapping(s) applied, " + warnings.Count + " warning(s).");
            return new PixConversionResult
            {
                Plan = plan,
                Xaml = xaml,
                CSharpCode = csharp,
                Warnings = warnings,
                Mappings = new List<string>(_appliedMappings)
            };
        }

        public RobotPlan ConvertToPlan(string pixXml, IList<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(pixXml))
                throw new ArgumentNullException(nameof(pixXml));

            _appliedMappings.Clear();
            _logDepth = 0;

            var doc = XDocument.Parse(pixXml);
            var root = doc.Root;
            var plan = new RobotPlan { Name = SafeClassName(ReadChildValue(root, "Name")) ?? "ConvertedPixRobot" };

            var logic = Child(root, "Logic");
            if (logic != null)
            {
                var segment = LastSegment(ReadChildValue(logic, "_activityFullName"));
                if (string.Equals(segment, "Container", StringComparison.Ordinal) ||
                    string.Equals(segment, "Sequence", StringComparison.Ordinal))
                {
                    plan.Root = new ActivityNode
                    {
                        Kind = ActivityNodeKind.Sequence,
                        Name = ReadText(logic) ?? "Root",
                        Children = BuildChildren(logic, warnings)
                    };
                }
                else
                {
                    plan.Root = BuildStep(logic, warnings) ?? new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };
                }
            }
            else
            {
                plan.Root = new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };
            }

            return plan;
        }

        private ActivityNode BuildStep(XElement step, IList<string> warnings)
        {
            if (string.Equals(GetInstanceType(step), "CommentStep", StringComparison.Ordinal))
            {
                LogInfo("Comment step → WriteLine.");
                return BuildComment(step);
            }

            var fullName = ReadChildValue(step, "_activityFullName") ?? "";
            var displayName = ReadText(step);
            LogInfo("Step " + (string.IsNullOrWhiteSpace(fullName) ? "(no activity full name)" : fullName) +
                    (string.IsNullOrWhiteSpace(displayName) ? string.Empty : "  [\"" + displayName + "\"]"));

            // Cross-script calls are handled structurally: the referenced script becomes a
            // reusable workflow activity instead of an InvokeMethod stub.
            if (string.Equals(fullName, ExecuteScriptActivityFullName, StringComparison.OrdinalIgnoreCase))
            {
                LogInfo("ExecuteScript → cross-script workflow reference.");
                return BuildExecuteScript(step, warnings);
            }

            var descriptor = _pixLibrary.Load(fullName);
            var node = new ActivityNode();

            if (descriptor == null)
            {
                LogInfo("Activity is not present in the PIX library.");
                // An activity unknown to the PIX library can still be converted when a PIX → MWF
                // mapping assigns it a WF activity. Otherwise, in strict mode, it aborts.
                if (TryGetMapping(fullName, out var mappedActivity))
                {
                    var mappedKind = MwfActivityTypeMapper.Resolve(mappedActivity);
                    if (mappedKind.HasValue)
                    {
                        node.Kind = mappedKind.Value;
                        node.MappedActivity = mappedActivity;
                        node.Name = ReadText(step) ?? LastSegment(fullName);
                        _appliedMappings.Add(fullName + " → " + mappedActivity);
                        LogInfo("Mapped via PIX → MWF mapping → " + mappedActivity + " (" + mappedKind + ").");
                        FillMappedFallback(node, step, fullName);
                        ApplyArgumentMappings(node, fullName, warnings);
                        LogNodeDetails(node);
                        return node;
                    }
                }

                if (FailOnUnmapped)
                {
                    LogInfo("Not mapped; aborting (FailOnUnmapped=true).");
                    throw BuildUnmappedException(fullName);
                }

                node.Kind = ActivityNodeKind.InvokeMethod;
                node.ClassName = "RpaActions";
                node.MethodName = MapMethodName(LastSegment(fullName));
                node.Name = ReadText(step) ?? "Unknown activity";
                node.Arguments = ReadArguments(step);
                ApplyArgumentMappings(node, fullName, warnings);
                warnings.Add("Unknown PIX activity \"" + fullName + "\" mapped to InvokeMethod " + node.MethodName + ".");
                LogInfo("Unknown activity → InvokeMethod RpaActions." + node.MethodName + " (fallback).");
                LogNodeDetails(node);
                return node;
            }

            node.Name = ReadText(step) ?? descriptor.DefaultDisplayName;
            LogInfo("Library role: " + (descriptor.Role ?? "statement") + ".");

            switch (descriptor.Role)
            {
                case "container":
                    node.Kind = ActivityNodeKind.Sequence;
                    LogInfo("Container → Sequence; building children...");
                    node.Children = BuildChildren(step, warnings);
                    break;
                case "if":
                    node.Kind = ActivityNodeKind.If;
                    node.Expression = ReadProperty(step, "Condition");
                    LogInfo("If → building then/elseIf/else branches...");
                    BuildIfBranches(step, node, warnings);
                    break;
                case "tryCatch":
                    node.Kind = ActivityNodeKind.TryCatch;
                    LogInfo("TryCatch → building try/catch/finally sections...");
                    BuildTryCatchSections(step, node, warnings);
                    break;
                case "then":
                case "elseIf":
                case "else":
                case "try":
                case "catch":
                case "finally":
                    node.Kind = ActivityNodeKind.Sequence;
                    node.Children = BuildChildren(step, warnings);
                    break;
                case "statement":
                default:
                    BuildStatement(node, descriptor, step, fullName, warnings);
                    break;
            }

            foreach (var w in descriptor.Warnings ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(w))
                    warnings.Add(w);

            LogNodeDetails(node);
            return node;
        }

        private void BuildStatement(ActivityNode node, PixActivityDescriptor descriptor, XElement step, string fullName, IList<string> warnings)
        {
            // A user/AI-managed PIX → MWF mapping takes precedence over the built-in snippet.
            string mappedActivity;
            if (TryGetMapping(fullName, out mappedActivity))
            {
                var mappedKind = MwfActivityTypeMapper.Resolve(mappedActivity);
                if (mappedKind.HasValue)
                {
                    node.Kind = mappedKind.Value;
                    node.MappedActivity = mappedActivity;
                    _appliedMappings.Add(fullName + " → " + mappedActivity);
                    LogInfo("Mapped via PIX → MWF mapping → " + mappedActivity + " (" + mappedKind + ").");
                    ApplyParameters(node, descriptor, step);
                    FillMappedFallback(node, step, fullName);
                    ApplyArgumentMappings(node, fullName, warnings);
                    return;
                }

                warnings.Add("Mapped MWF activity \"" + mappedActivity + "\" for \"" + fullName +
                             "\" is not a supported WF activity; kept the built-in mapping.");
            }

            LogInfo("Built-in snippet: " + (string.IsNullOrWhiteSpace(descriptor.WfSnippet) ? "InvokeMethod" : descriptor.WfSnippet) + ".");
            switch (descriptor.WfSnippet)
            {
                case "Assign":
                    node.Kind = ActivityNodeKind.Assign;
                    ApplyParameters(node, descriptor, step);
                    break;
                case "Delay":
                    node.Kind = ActivityNodeKind.Delay;
                    ApplyParameters(node, descriptor, step);
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Duration");
                    break;
                case "WriteLine":
                    node.Kind = ActivityNodeKind.WriteLine;
                    ApplyParameters(node, descriptor, step);
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Text") ?? ReadText(step);
                    break;
                case "Throw":
                    node.Kind = ActivityNodeKind.Throw;
                    ApplyParameters(node, descriptor, step);
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Exception") ?? "new System.Exception()";
                    break;
                case "Return":
                    node.Kind = ActivityNodeKind.Return;
                    break;
                case "InvokeMethod":
                default:
                    if (FailOnUnmapped)
                    {
                        LogInfo("No built-in WF snippet; aborting (FailOnUnmapped=true).");
                        throw BuildUnmappedException(fullName);
                    }

                    node.Kind = ActivityNodeKind.InvokeMethod;
                    node.ClassName = "RpaActions";
                    node.MethodName = MapMethodName(LastSegment(fullName));
                    node.Arguments = ReadArguments(step);
                    ApplyArgumentMappings(node, fullName, warnings);
                    LogInfo("No built-in snippet → InvokeMethod RpaActions." + node.MethodName + " (fallback).");
                    break;
            }
        }

        private bool TryGetMapping(string fullName, out string mwfActivity)
        {
            mwfActivity = null;
            if (_mappings == null || _mappings.Count == 0 || string.IsNullOrWhiteSpace(fullName))
                return false;

            return _mappings.TryGetValue(fullName, out mwfActivity) && !string.IsNullOrWhiteSpace(mwfActivity);
        }

        private static PixConversionException BuildUnmappedException(string fullName)
            => new PixConversionException(
                "Activity \"" + fullName + "\" is not mapped: no PIX → MWF mapping entry and no built-in WF snippet (Assign/Delay/WriteLine/Throw/Return).");

        /// <summary>
        /// Fills the mapped node the same way the built-in snippet mapping does, so a mapped
        /// kind (e.g. WriteLine for a previously-InvokeMethod activity) still receives its
        /// expression/argument values from the PIX step.
        /// </summary>
        private void FillMappedFallback(ActivityNode node, XElement step, string fullName)
        {
            switch (node.Kind)
            {
                case ActivityNodeKind.Delay:
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Duration");
                    break;
                case ActivityNodeKind.WriteLine:
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Text") ?? ReadText(step);
                    break;
                case ActivityNodeKind.Throw:
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Exception") ?? "new System.Exception()";
                    break;
                case ActivityNodeKind.If:
                case ActivityNodeKind.While:
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Condition") ?? "false";
                    break;
                case ActivityNodeKind.ForEach:
                    if (string.IsNullOrEmpty(node.Expression))
                        node.Expression = ReadProperty(step, "Values");
                    break;
                case ActivityNodeKind.InvokeMethod:
                    node.ClassName = "RpaActions";
                    node.MethodName = MapMethodName(LastSegment(fullName));
                    node.Arguments = ReadArguments(step);
                    break;
            }
        }

        private void ApplyParameters(ActivityNode node, PixActivityDescriptor descriptor, XElement step)
        {
            foreach (var p in descriptor.Parameters ?? new List<PixActivityParameter>())
            {
                var value = ReadProperty(step, p.Pix);
                switch (p.Target)
                {
                    case "Variable":
                        node.Variable = value;
                        break;
                    case "Expression":
                        node.Expression = value;
                        break;
                    case "Argument":
                        node.Arguments.Add(new ArgumentAssignment { Parameter = p.Pix, Value = value });
                        break;
                }
            }
        }

        /// <summary>
        /// Applies argument-level mappings to an activity node's <see cref="ArgumentAssignment"/>
        /// list: renames the PIX parameter to the mapped MWF argument and wraps the value in the
        /// configured conversion expression when one is set.
        /// </summary>
        private void ApplyArgumentMappings(ActivityNode node, string pixFullName, IList<string> warnings)
        {
            if (node?.Arguments == null || node.Arguments.Count == 0)
                return;

            if (!_argumentMappings.TryGetValue(pixFullName, out var mappings) || mappings == null)
                return;

            foreach (var assignment in node.Arguments)
            {
                if (string.IsNullOrWhiteSpace(assignment.Parameter))
                    continue;

                var mapping = mappings.FirstOrDefault(m =>
                    m != null && string.Equals(m.PixArgument, assignment.Parameter, StringComparison.OrdinalIgnoreCase));
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.MwfArgument))
                    continue;

                var pixArgument = assignment.Parameter;
                assignment.Parameter = mapping.MwfArgument;
                assignment.Value = BuildConversionExpression(assignment.Value, mapping);

                LogInfo("Argument mapping: " + pixArgument + " → " + mapping.MwfArgument +
                        (mapping.Conversion == ConversionKind.None
                            ? string.Empty
                            : " (conversion " + mapping.Conversion + ")") +
                        " = " + (assignment.Value ?? "(null)"));

                if (mapping.Conversion != ConversionKind.None)
                    warnings.Add("Applied conversion " + mapping.Conversion + " to argument " +
                                 mapping.PixArgument + " → " + mapping.MwfArgument + " of " + pixFullName + ".");
            }
        }

        private static string BuildConversionExpression(string value, PixMwfArgumentMapping mapping)
        {
            if (mapping == null || mapping.Conversion == ConversionKind.None || string.IsNullOrWhiteSpace(value))
                return value;

            var expr = mapping.ConversionExpression ?? string.Empty;
            switch (mapping.Conversion)
            {
                case ConversionKind.ToString:
                    return "(" + value + ").ToString()";
                case ConversionKind.DateTimeFormat:
                    return "System.DateTime.ParseExact(" + value + ", \"" +
                           (string.IsNullOrWhiteSpace(expr) ? "dd.MM.yyyy" : expr) +
                           "\", System.Globalization.CultureInfo.InvariantCulture)";
                case ConversionKind.Parse:
                    return "System.Convert.ChangeType(" + value + ", typeof(" +
                           (string.IsNullOrWhiteSpace(expr) ? "string" : expr) + "))";
                case ConversionKind.Custom:
                    return string.IsNullOrWhiteSpace(expr) ? value : expr.Replace("{0}", value);
                default:
                    return value;
            }
        }

        private void BuildIfBranches(XElement step, ActivityNode node, IList<string> warnings)
        {
            var thenStep = FindChild(step, "BR.Core.Base.IfTrue");
            var elseIfSteps = FindChildren(step, "BR.Core.Base.ElseIf");
            var elseStep = FindChild(step, "BR.Core.Base.IfFalse");

            node.Children = thenStep != null ? BuildChildren(thenStep, warnings) : new List<ActivityNode>();

            var elseTail = elseStep != null ? BuildChildren(elseStep, warnings) : new List<ActivityNode>();
            for (var i = elseIfSteps.Count - 1; i >= 0; i--)
            {
                var elseifNode = new ActivityNode
                {
                    Kind = ActivityNodeKind.If,
                    Name = ReadText(elseIfSteps[i]) ?? "Else If",
                    Expression = ReadProperty(elseIfSteps[i], "Condition"),
                    Children = BuildChildren(elseIfSteps[i], warnings)
                };
                if (elseTail.Count > 0)
                    elseifNode.ElseChildren = elseTail;
                elseTail = new List<ActivityNode> { elseifNode };
            }

            node.ElseChildren = elseTail;
        }

        private void BuildTryCatchSections(XElement step, ActivityNode node, IList<string> warnings)
        {
            var tryStep = FindChild(step, "BR.Core.Base.Try");
            var catchSteps = FindChildren(step, "BR.Core.Base.Catch");
            var finallyStep = FindChild(step, "BR.Core.Base.Finally");

            node.Children = tryStep != null ? BuildChildren(tryStep, warnings) : new List<ActivityNode>();

            foreach (var c in catchSteps)
                node.CatchChildren.AddRange(BuildChildren(c, warnings));

            node.FinallyChildren = finallyStep != null ? BuildChildren(finallyStep, warnings) : new List<ActivityNode>();
        }

        private List<ActivityNode> BuildChildren(XElement step, IList<string> warnings)
        {
            var result = new List<ActivityNode>();
            var childs = Child(step, "Childs");
            if (childs == null)
                return result;

            foreach (var child in childs.Elements())
            {
                if (child.Name.LocalName != "AbstractStep")
                    continue;

                _logDepth++;
                try
                {
                    var node = BuildStep(child, warnings);
                    if (node != null)
                        result.Add(node);
                }
                finally
                {
                    _logDepth--;
                }
            }

            return result;
        }

        private static int CountActivities(ActivityNode node)
        {
            if (node == null)
                return 0;

            var count = 1;
            foreach (var child in node.Children ?? new List<ActivityNode>())
                count += CountActivities(child);
            foreach (var child in node.ElseChildren ?? new List<ActivityNode>())
                count += CountActivities(child);
            foreach (var child in node.CatchChildren ?? new List<ActivityNode>())
                count += CountActivities(child);
            foreach (var child in node.FinallyChildren ?? new List<ActivityNode>())
                count += CountActivities(child);
            return count;
        }

        private void LogNodeDetails(ActivityNode node)
        {
            if (node == null)
                return;

            if (!string.IsNullOrWhiteSpace(node.ScriptRef))
                LogInfo("ScriptRef: " + node.ScriptRef);
            if (!string.IsNullOrWhiteSpace(node.Variable))
                LogInfo("Variable: " + node.Variable);
            if (!string.IsNullOrWhiteSpace(node.Expression))
                LogInfo("Expression: " + node.Expression);

            foreach (var a in node.Arguments ?? new List<ArgumentAssignment>())
                LogInfo("Argument " + (a.Parameter ?? "(unnamed)") + " = " + (a.Value ?? "(null)"));
        }

        private ActivityNode BuildComment(XElement step)
        {
            var text = ReadText(step);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return new ActivityNode
            {
                Kind = ActivityNodeKind.WriteLine,
                Name = "Comment",
                Expression = text
            };
        }

        /// <summary>
        /// Converts a BR.Core.Base.ExecuteScript step into a reference to the generated child
        /// workflow, or into an InvokeMethod fallback when the referenced script cannot be
        /// resolved statically.
        /// </summary>
        private ActivityNode BuildExecuteScript(XElement step, IList<string> warnings)
        {
            var filePath = ReadProperty(step, "FilePath");
            var scriptRef = ResolveScriptName(filePath);

            if (!string.IsNullOrWhiteSpace(scriptRef))
            {
                var node = new ActivityNode
                {
                    Kind = ActivityNodeKind.InvokeWorkflow,
                    Name = ReadText(step) ?? "Execute script",
                    ScriptRef = scriptRef
                };
                LogNodeDetails(node);
                return node;
            }

            warnings.Add("ExecuteScript has no resolvable FilePath; kept InvokeMethod RpaActions.ExecuteScript.");
            var fallback = new ActivityNode
            {
                Kind = ActivityNodeKind.InvokeMethod,
                ClassName = "RpaActions",
                MethodName = "ExecuteScript",
                Name = ReadText(step) ?? "Execute script",
                Arguments = ReadArguments(step)
            };
            LogNodeDetails(fallback);
            return fallback;
        }

        /// <summary>
        /// Extracts a workflow class name from a C# string literal that names a .pix script,
        /// e.g. "@\"newScript.pix\"" -> "newScript". Returns null for non-literal expressions.
        /// </summary>
        internal static string ResolveScriptName(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            var value = expression.Trim();
            string inner = null;

            if (value.Length >= 3 && value.StartsWith("@\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                inner = value.Substring(2, value.Length - 3);
            else if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                inner = value.Substring(1, value.Length - 2);

            if (string.IsNullOrWhiteSpace(inner))
                return null;

            inner = inner.Replace("\"\"", "\"").Trim();

            // Strip a path and extension: "folder/NewScript.pix" -> "NewScript".
            var slash = Math.Max(inner.LastIndexOf('/'), inner.LastIndexOf('\\'));
            var baseName = slash >= 0 ? inner.Substring(slash + 1) : inner;
            var dot = baseName.LastIndexOf('.');
            if (dot >= 0)
                baseName = baseName.Substring(0, dot);

            return SafeClassName(baseName);
        }

        private static string ReadProperty(XElement step, string propertyName)
        {
            var pvs = Child(step, "PropertyValues");
            if (pvs == null)
                return null;

            foreach (var pv in pvs.Elements())
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

        private static List<ArgumentAssignment> ReadArguments(XElement step)
        {
            var result = new List<ArgumentAssignment>();
            var pvs = Child(step, "PropertyValues");
            if (pvs == null)
                return result;

            foreach (var pv in pvs.Elements())
            {
                if (pv.Name.LocalName != "PropertyValue")
                    continue;

                var name = ReadChildValue(pv, "PropertyName");
                var expr = ReadChildValue(pv, "_expression");
                var value = ReadChildValue(pv, "_value");
                result.Add(new ArgumentAssignment
                {
                    Parameter = name,
                    Value = string.IsNullOrWhiteSpace(expr)
                        ? (string.IsNullOrWhiteSpace(value) ? null : value)
                        : expr
                });
            }

            return result;
        }

        private static string GetInstanceType(XElement step)
            => step?.Attribute(XName.Get("type", XsiNamespace))?.Value;

        private static string ReadText(XElement step)
        {
            var text = Child(step, "Text")?.Value;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static XElement Child(XElement parent, string localName)
            => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

        private static string ReadChildValue(XElement parent, string localName)
            => Child(parent, localName)?.Value;

        private static IEnumerable<XElement> AbstractSteps(XElement step)
        {
            var childs = Child(step, "Childs");
            return childs?.Elements().Where(e => e.Name.LocalName == "AbstractStep")
                ?? Enumerable.Empty<XElement>();
        }

        private static XElement FindChild(XElement step, string activityFullName)
            => AbstractSteps(step).FirstOrDefault(s =>
                string.Equals(ReadChildValue(s, "_activityFullName"), activityFullName, StringComparison.Ordinal));

        private static List<XElement> FindChildren(XElement step, string activityFullName)
            => AbstractSteps(step).Where(s =>
                string.Equals(ReadChildValue(s, "_activityFullName"), activityFullName, StringComparison.Ordinal)).ToList();

        private static string LastSegment(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "";
            var idx = fullName.LastIndexOf('.');
            return idx < 0 ? fullName : fullName.Substring(idx + 1);
        }

        private static string MapMethodName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name ?? "")
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            var id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0]))
                id = "_" + id;
            return id;
        }

        internal static string SafeClassName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length > 0)
                        break;
                }
            }

            var id = sb.ToString();
            if (id.Length == 0)
                return null;
            if (char.IsDigit(id[0]))
                id = "_" + id;
            return id;
        }
    }
}



