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
    /// Converts a UiPath robot (XAML) into plain Workflow Foundation.
    /// Standard activities (Sequence/If/While/ForEach/Assign/Delay/WriteLine)
    /// are carried over as-is; UiPath-specific activities become InvokeMethod
    /// calls on C# methods (per the accepted output format).
    /// </summary>
    public class UiPathToWfConverter : IUiPathConverter
    {
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static readonly HashSet<string> ActivitySuffixes = new HashSet<string>(StringComparer.Ordinal)
        {
            ".Activities", ".Body", ".Then", ".Else", ".Try", ".Finally", ".Catches",
            ".Nodes", ".Branches", ".Action", ".Handler", ".Default", ".Steps"
        };

        private readonly IRobotGenerator _generator;

        public UiPathToWfConverter(IRobotGenerator generator)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public UiPathConversionResult Convert(string uipathXaml)
        {
            var warnings = new List<string>();
            var plan = ConvertToPlan(uipathXaml, warnings);
            return new UiPathConversionResult
            {
                Plan = plan,
                Xaml = _generator.EmitWorkflowXaml(plan),
                CSharpCode = _generator.EmitHelperCode(plan),
                Warnings = warnings
            };
        }

        public RobotPlan ConvertToPlan(string uipathXaml)
        {
            return ConvertToPlan(uipathXaml, new List<string>());
        }

        public RobotPlan ConvertToPlan(string uipathXaml, IList<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(uipathXaml))
                throw new ArgumentNullException(nameof(uipathXaml));

            var doc = XDocument.Parse(uipathXaml);
            var root = doc.Root;

            var plan = new RobotPlan
            {
                Name = root?.Attribute(XName.Get("Class", XamlNamespace))?.Value ?? "ConvertedRobot"
            };

            var body = root?.Elements().FirstOrDefault(e => IsActivity(e));
            plan.Root = body != null
                ? ConvertElement(body, warnings)
                : new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };

            return plan;
        }

        private ActivityNode ConvertElement(XElement e, IList<string> warnings)
        {
            var node = CreateNode(e, warnings);

            foreach (var child in e.Elements())
            {
                if (IsXamlDirective(child)) continue;
                var name = child.Name.LocalName;

                if (name.Contains('.'))
                {
                    if (IsActivityCollection(name))
                    {
                        foreach (var inner in child.Elements())
                        {
                            if (IsXamlDirective(inner) || IsDeclaration(inner)) continue;
                            node.Children.Add(ConvertElement(inner, warnings));
                        }
                    }
                }
                else if (!IsDeclaration(child))
                {
                    node.Children.Add(ConvertElement(child, warnings));
                }
            }

            return node;
        }

        private static ActivityNode CreateNode(XElement e, IList<string> warnings)
        {
            var name = e.Name.LocalName;
            var node = new ActivityNode { Name = ReadAttr(e, "DisplayName") };

            switch (name)
            {
                case "Sequence":
                    node.Kind = ActivityNodeKind.Sequence;
                    break;
                case "Flowchart":
                    node.Kind = ActivityNodeKind.Sequence;
                    warnings.Add("Flowchart converted to Sequence (simplification).");
                    break;
                case "If":
                    node.Kind = ActivityNodeKind.If;
                    node.Expression = ReadAttr(e, "Condition") ?? ReadText(e, name + ".Condition");
                    break;
                case "While":
                case "DoWhile":
                    node.Kind = ActivityNodeKind.While;
                    node.Expression = ReadAttr(e, "Condition") ?? ReadText(e, name + ".Condition");
                    break;
                case "ForEach":
                case "ForEach`1":
                    node.Kind = ActivityNodeKind.ForEach;
                    node.Expression = ReadAttr(e, "Values");
                    break;
                case "Assign":
                    node.Kind = ActivityNodeKind.Assign;
                    node.Variable = ReadAttr(e, "To") ?? ReadText(e, "Assign.To");
                    node.Expression = ReadAttr(e, "Value") ?? ReadText(e, "Assign.Value");
                    break;
                case "Delay":
                    node.Kind = ActivityNodeKind.Delay;
                    node.Expression = ReadAttr(e, "Duration") ?? ReadText(e, "Delay.Duration");
                    break;
                case "WriteLine":
                    node.Kind = ActivityNodeKind.WriteLine;
                    node.Expression = ReadAttr(e, "Text") ?? ReadText(e, "WriteLine.Text");
                    break;
                case "LogMessage":
                    node.Kind = ActivityNodeKind.WriteLine;
                    node.Expression = ReadAttr(e, "Message");
                    break;
                case "TryCatch":
                case "Parallel":
                case "Pick":
                case "Switch":
                case "Catch":
                    node.Kind = ActivityNodeKind.Sequence;
                    warnings.Add(name + " converted to Sequence (simplification).");
                    break;
                default:
                    node.Kind = ActivityNodeKind.InvokeMethod;
                    node.ClassName = "RpaActions";
                    node.MethodName = MapMethodName(name);
                    node.Arguments = ReadArguments(e);
                    warnings.Add("Activity " + name + " mapped to InvokeMethod " + node.MethodName + ".");
                    break;
            }

            return node;
        }

        private static List<ArgumentAssignment> ReadArguments(XElement e)
        {
            var args = new List<ArgumentAssignment>();
            foreach (var a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration) continue;
                if (a.Name.NamespaceName == XamlNamespace) continue;
                if (a.Name.LocalName == "DisplayName") continue;
                args.Add(new ArgumentAssignment { Parameter = a.Name.LocalName, Value = a.Value });
            }
            return args;
        }

        private static string MapMethodName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            var id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0])) id = "_" + id;
            return id;
        }

        private static string ReadAttr(XElement e, string name)
        {
            return e.Attribute(name)?.Value;
        }

        private static string ReadText(XElement e, string containerName)
        {
            var container = e.Elements().FirstOrDefault(c => c.Name.LocalName == containerName);
            return container?.Value;
        }

        private static bool IsXamlDirective(XElement e)
        {
            return e.Name.NamespaceName == XamlNamespace;
        }

        private static bool IsDeclaration(XElement e)
        {
            var name = e.Name.LocalName;
            return name == "Variable" || name == "Argument" || name == "Members" || name == "Properties";
        }

        private static bool IsActivity(XElement e)
        {
            if (IsXamlDirective(e) || IsDeclaration(e)) return false;
            return !e.Name.LocalName.Contains('.');
        }

        private static bool IsActivityCollection(string localName)
        {
            foreach (var suffix in ActivitySuffixes)
                if (localName.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

    }
}
