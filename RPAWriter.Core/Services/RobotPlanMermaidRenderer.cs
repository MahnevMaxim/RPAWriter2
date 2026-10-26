using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Renders a <see cref="RobotPlan"/> activity tree as a Mermaid <c>flowchart TD</c> diagram,
    /// so the intermediate plan can be visualized in the converter chat output.
    /// </summary>
    public static class RobotPlanMermaidRenderer
    {
        public static string Render(RobotPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("flowchart TD");

            if (plan?.Root == null)
            {
                sb.AppendLine("    n0[\"Empty plan\"]");
                return sb.ToString();
            }

            EmitNode(plan.Root, sb, new RenderState());
            return sb.ToString();
        }

        private sealed class RenderState
        {
            public int NextId { get; set; }
        }

        private static string EmitNode(ActivityNode node, StringBuilder sb, RenderState state)
        {
            var id = "n" + state.NextId++;
            sb.Append("    ").Append(id).Append("[\"").Append(EscapeLabel(Label(node))).AppendLine("\"]");

            switch (node.Kind)
            {
                case ActivityNodeKind.If:
                    EmitChildren(node.Children, id, "then", sb, state);
                    EmitChildren(node.ElseChildren, id, "else", sb, state);
                    break;
                case ActivityNodeKind.TryCatch:
                    EmitChildren(node.Children, id, "try", sb, state);
                    EmitChildren(node.CatchChildren, id, "catch", sb, state);
                    EmitChildren(node.FinallyChildren, id, "finally", sb, state);
                    break;
                default:
                    EmitChildren(node.Children, id, null, sb, state);
                    break;
            }

            return id;
        }

        private static void EmitChildren(System.Collections.Generic.List<ActivityNode> children, string parentId, string edgeLabel, StringBuilder sb, RenderState state)
        {
            if (children == null)
                return;

            foreach (var child in children)
            {
                if (child == null)
                    continue;

                var childId = EmitNode(child, sb, state);
                sb.Append("    ").Append(parentId);
                if (string.IsNullOrEmpty(edgeLabel))
                    sb.Append(" --> ");
                else
                    sb.Append(" -->|").Append(edgeLabel).Append("| ");
                sb.Append(childId).AppendLine();
            }
        }

        private static string Label(ActivityNode node)
        {
            var name = node.Name ?? "";
            switch (node.Kind)
            {
                case ActivityNodeKind.Sequence:
                    return "Sequence: " + FirstNonEmpty(name, "Sequence");
                case ActivityNodeKind.If:
                    return "If: " + FirstNonEmpty(name, "If") + Suffix(node.Expression);
                case ActivityNodeKind.While:
                    return "While: " + FirstNonEmpty(name, "While") + Suffix(node.Expression);
                case ActivityNodeKind.ForEach:
                    return "ForEach: " + FirstNonEmpty(name, "ForEach") + Suffix(node.Expression);
                case ActivityNodeKind.Assign:
                    return "Assign: " + (node.Variable ?? "") + " = " + (node.Expression ?? "");
                case ActivityNodeKind.InvokeMethod:
                    return "InvokeMethod: " + (node.ClassName ?? "") + "." + (node.MethodName ?? "") + "()";
                case ActivityNodeKind.WriteLine:
                    return "WriteLine: " + FirstNonEmpty(node.Expression, name);
                case ActivityNodeKind.Delay:
                    return "Delay: " + (node.Expression ?? "");
                case ActivityNodeKind.TryCatch:
                    return "TryCatch: " + FirstNonEmpty(name, "TryCatch");
                case ActivityNodeKind.Throw:
                    return "Throw: " + (node.Expression ?? "");
                case ActivityNodeKind.Return:
                    return "Return";
                case ActivityNodeKind.InvokeWorkflow:
                    return "InvokeWorkflow: " + FirstNonEmpty(node.ScriptRef, name);
                default:
                    return FirstNonEmpty(name, node.Kind.ToString());
            }
        }

        private static string Suffix(string value)
            => string.IsNullOrWhiteSpace(value) ? "" : "<br/>" + value;

        private static string FirstNonEmpty(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string EscapeLabel(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value
                .Replace("\"", "#quot;")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
