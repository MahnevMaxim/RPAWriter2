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

        private readonly PixActivityLibrary _pixLibrary;
        private readonly SnippetWorkflowEmitter _emitter;
        private readonly RobotGenerator _generator;
        private readonly IReadOnlyDictionary<string, string> _mappings;
        private readonly List<string> _appliedMappings = new List<string>();

        public PixToWfConverter() : this(new PixActivityLibrary(), new WfSnippetLibrary(), null)
        {
        }

        public PixToWfConverter(PixActivityLibrary pixLibrary, WfSnippetLibrary snippets)
            : this(pixLibrary, snippets, null)
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
        {
            _pixLibrary = pixLibrary ?? throw new ArgumentNullException(nameof(pixLibrary));
            _emitter = new SnippetWorkflowEmitter(snippets ?? throw new ArgumentNullException(nameof(snippets)));
            _generator = new RobotGenerator();
            _mappings = pixToMwfMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public PixConversionResult Convert(string pixXml)
        {
            var warnings = new List<string>();
            var plan = ConvertToPlan(pixXml, warnings);
            return new PixConversionResult
            {
                Plan = plan,
                Xaml = _emitter.EmitWorkflowXaml(plan),
                CSharpCode = _generator.EmitHelperCode(plan),
                Warnings = warnings,
                Mappings = new List<string>(_appliedMappings)
            };
        }

        public RobotPlan ConvertToPlan(string pixXml, IList<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(pixXml))
                throw new ArgumentNullException(nameof(pixXml));

            _appliedMappings.Clear();

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
                return BuildComment(step);

            var fullName = ReadChildValue(step, "_activityFullName") ?? "";
            var descriptor = _pixLibrary.Load(fullName);
            var node = new ActivityNode();

            if (descriptor == null)
            {
                node.Kind = ActivityNodeKind.InvokeMethod;
                node.ClassName = "RpaActions";
                node.MethodName = MapMethodName(LastSegment(fullName));
                node.Name = ReadText(step) ?? "Unknown activity";
                warnings.Add("Unknown PIX activity \"" + fullName + "\" mapped to InvokeMethod " + node.MethodName + ".");
                return node;
            }

            node.Name = ReadText(step) ?? descriptor.DefaultDisplayName;

            switch (descriptor.Role)
            {
                case "container":
                    node.Kind = ActivityNodeKind.Sequence;
                    node.Children = BuildChildren(step, warnings);
                    break;
                case "if":
                    node.Kind = ActivityNodeKind.If;
                    node.Expression = ReadProperty(step, "Condition");
                    BuildIfBranches(step, node, warnings);
                    break;
                case "tryCatch":
                    node.Kind = ActivityNodeKind.TryCatch;
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
                    ApplyParameters(node, descriptor, step);
                    FillMappedFallback(node, step, fullName);
                    return;
                }

                warnings.Add("Mapped MWF activity \"" + mappedActivity + "\" for \"" + fullName +
                             "\" is not a supported WF activity; kept the built-in mapping.");
            }

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
                    node.Kind = ActivityNodeKind.InvokeMethod;
                    node.ClassName = "RpaActions";
                    node.MethodName = MapMethodName(LastSegment(fullName));
                    node.Arguments = ReadArguments(step);
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
                var node = BuildStep(child, warnings);
                if (node != null)
                    result.Add(node);
            }

            return result;
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

        private static string SafeClassName(string name)
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



