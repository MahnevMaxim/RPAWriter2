using System;
using System.Collections.Generic;
using System.Text;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Generates XAML (standard WF activities, including InvokeMethod/Assign)
    /// and C# code (plain classes invoked via InvokeMethod).
    /// </summary>
    public class RobotGenerator : IRobotGenerator
    {
        public string EmitWorkflowXaml(RobotPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var root = plan.Root ?? new ActivityNode { Kind = ActivityNodeKind.Sequence };
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Workflow: " + (plan.Name ?? "Untitled") + " (RPAWriter) -->");
            sb.AppendLine("<Activity x:Class=\"" + XmlEscape(plan.Name ?? "Untitled") + "\"");
            sb.AppendLine("    xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\"");
            sb.AppendLine("    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
            sb.AppendLine("    xmlns:local=\"clr-namespace:Robots\">");
            EmitNode(sb, root, "  ");
            sb.AppendLine("</Activity>");
            return sb.ToString();
        }

        private void EmitNode(StringBuilder sb, ActivityNode node, string indent)
        {
            switch (node.Kind)
            {
                case ActivityNodeKind.Sequence:
                    sb.AppendLine(indent + "<Sequence DisplayName=\"" + XmlEscape(node.Name ?? "Sequence") + "\">");
                    foreach (var child in node.Children ?? new List<ActivityNode>())
                        EmitNode(sb, child, indent + "  ");
                    sb.AppendLine(indent + "</Sequence>");
                    break;
                case ActivityNodeKind.If:
                    sb.AppendLine(indent + "<If DisplayName=\"" + XmlEscape(node.Name ?? "If") + "\" Condition=\"[" + XmlEscape(node.Expression ?? "false") + "]\">");
                    if (node.Children != null && node.Children.Count > 0)
                        EmitNode(sb, node.Children[0], indent + "  ");
                    sb.AppendLine(indent + "</If>");
                    break;
                case ActivityNodeKind.While:
                    sb.AppendLine(indent + "<While DisplayName=\"" + XmlEscape(node.Name ?? "While") + "\" Condition=\"[" + XmlEscape(node.Expression ?? "false") + "]\">");
                    foreach (var child in node.Children ?? new List<ActivityNode>())
                        EmitNode(sb, child, indent + "  ");
                    sb.AppendLine(indent + "</While>");
                    break;
                case ActivityNodeKind.ForEach:
                    sb.AppendLine(indent + "<ForEach DisplayName=\"" + XmlEscape(node.Name ?? "ForEach") + "\" Values=\"" + XmlEscape(node.Expression ?? "") + "\">");
                    foreach (var child in node.Children ?? new List<ActivityNode>())
                        EmitNode(sb, child, indent + "  ");
                    sb.AppendLine(indent + "</ForEach>");
                    break;
                case ActivityNodeKind.Assign:
                    sb.AppendLine(indent + "<Assign DisplayName=\"" + XmlEscape(node.Name ?? "Assign") + "\">");
                    sb.AppendLine(indent + "  <Assign.To><OutArgument x:TypeArguments=\"x:String\">" + XmlEscape(node.Variable ?? "") + "</OutArgument></Assign.To>");
                    sb.AppendLine(indent + "  <Assign.Value><InArgument x:TypeArguments=\"x:String\">" + XmlEscape(node.Expression ?? "") + "</InArgument></Assign.Value>");
                    sb.AppendLine(indent + "</Assign>");
                    break;
                case ActivityNodeKind.InvokeMethod:
                    EmitInvokeMethod(sb, node, indent);
                    break;
                case ActivityNodeKind.WriteLine:
                    sb.AppendLine(indent + "<WriteLine DisplayName=\"" + XmlEscape(node.Name ?? "WriteLine") + "\" Text=\"" + XmlEscape(node.Expression ?? "") + "\" />");
                    break;
                case ActivityNodeKind.Delay:
                    sb.AppendLine(indent + "<Delay DisplayName=\"" + XmlEscape(node.Name ?? "Delay") + "\" Duration=\"" + XmlEscape(node.Expression ?? "00:00:00") + "\" />");
                    break;
            }
        }

        private void EmitInvokeMethod(StringBuilder sb, ActivityNode node, string indent)
        {
            var targetType = "local:" + (node.ClassName ?? "RpaActions");
            sb.AppendLine(indent + "<InvokeMethod DisplayName=\"" + XmlEscape(node.Name ?? node.MethodName ?? "InvokeMethod") + "\"");
            sb.AppendLine(indent + "    MethodName=\"" + XmlEscape(node.MethodName ?? "Run") + "\"");
            sb.AppendLine(indent + "    TargetType=\"{x:Type " + targetType + "}\">");
            if (node.Arguments != null && node.Arguments.Count > 0)
            {
                sb.AppendLine(indent + "  <InvokeMethod.Arguments>");
                foreach (var arg in node.Arguments)
                    sb.AppendLine(indent + "    <InArgument x:TypeArguments=\"x:String\">" + XmlEscape(arg.Value ?? "") + "</InArgument>");
                sb.AppendLine(indent + "  </InvokeMethod.Arguments>");
            }
            sb.AppendLine(indent + "</InvokeMethod>");
        }

        public string EmitHelperCode(RobotPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var methods = CollectInvokeMethods(plan.Root);
            var sb = new StringBuilder();
            sb.AppendLine("// Generated by RPAWriter. Methods invoked from XAML via InvokeMethod.");
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace Robots");
            sb.AppendLine("{");
            sb.AppendLine("    public static class RpaActions");
            sb.AppendLine("    {");
            foreach (var m in methods)
            {
                sb.AppendLine("        public static void " + SafeIdentifier(m.MethodName) + "()");
                sb.AppendLine("        {");
                sb.AppendLine("            // TODO: implement " + SafeIdentifier(m.MethodName));
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private List<ActivityNode> CollectInvokeMethods(ActivityNode node)
        {
            var result = new List<ActivityNode>();
            if (node == null) return result;
            if (node.Kind == ActivityNodeKind.InvokeMethod && !string.IsNullOrWhiteSpace(node.MethodName))
                result.Add(node);
            if (node.Children != null)
                foreach (var child in node.Children)
                    result.AddRange(CollectInvokeMethods(child));
            return result;
        }

        private static string SafeIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Run";
            var sb = new StringBuilder();
            foreach (var c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            var id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0])) id = "_" + id;
            return id;
        }

        private static string XmlEscape(string value)
        {
            if (value == null) return "";
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

    }
}
