using System;
using System.Collections.Generic;
using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Renders a <see cref="RobotPlan"/> into Workflow Foundation XAML by substituting
    /// values into empty activity snippets loaded from the activity library.
    /// </summary>
    public class SnippetWorkflowEmitter
    {
        private readonly WfSnippetLibrary _snippets;

        public SnippetWorkflowEmitter(WfSnippetLibrary snippets)
        {
            _snippets = snippets ?? new WfSnippetLibrary();
        }

        public string EmitWorkflowXaml(RobotPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var root = plan.Root ?? new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };
            var sb = new StringBuilder();
            sb.AppendLine("<!-- Workflow: " + (plan.Name ?? "Untitled") + " (RPAWriter PIX converter) -->");
            sb.AppendLine("<Activity x:Class=\"Robots." + XmlEscape(plan.Name ?? "Untitled") + "\"");
            sb.AppendLine("    xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\"");
            sb.AppendLine("    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
            sb.AppendLine("    xmlns:local=\"clr-namespace:Robots\"");
            sb.AppendLine("    xmlns:s=\"clr-namespace:System;assembly=mscorlib\">");
            sb.Append(EmitNode(root));
            sb.AppendLine();
            sb.AppendLine("</Activity>");
            return sb.ToString();
        }

        private string EmitNode(ActivityNode node)
        {
            if (node == null)
                return "<Sequence />";

            var kind = node.Kind;
            var snippet = _snippets.Load(SnippetName(kind), DefaultSnippet(kind));
            var result = Replace(snippet, "DisplayName", XmlEscape(node.Name ?? DefaultName(kind)));

            switch (kind)
            {
                case ActivityNodeKind.Sequence:
                    result = Replace(result, "Children", EmitChildren(node.Children));
                    break;
                case ActivityNodeKind.If:
                    result = Replace(result, "Condition", XmlEscape(node.Expression ?? "false"));
                    result = Replace(result, "Then", EmitChildren(node.Children));
                    result = Replace(result, "Else", EmitChildren(node.ElseChildren));
                    break;
                case ActivityNodeKind.While:
                    result = Replace(result, "Condition", XmlEscape(node.Expression ?? "false"));
                    result = Replace(result, "Children", EmitChildren(node.Children));
                    break;
                case ActivityNodeKind.ForEach:
                    result = Replace(result, "Values", XmlEscape(node.Expression ?? ""));
                    result = Replace(result, "Children", EmitChildren(node.Children));
                    break;
                case ActivityNodeKind.Assign:
                    result = Replace(result, "To", XmlEscape(node.Variable ?? ""));
                    result = Replace(result, "Value", XmlEscape(node.Expression ?? ""));
                    break;
                case ActivityNodeKind.InvokeMethod:
                    result = Replace(result, "MethodName", XmlEscape(node.MethodName ?? "Run"));
                    result = Replace(result, "ClassName", XmlEscape(node.ClassName ?? "RpaActions"));
                    result = Replace(result, "Arguments", EmitArguments(node.Arguments));
                    break;
                case ActivityNodeKind.WriteLine:
                    result = Replace(result, "Text", XmlEscape(node.Expression ?? ""));
                    break;
                case ActivityNodeKind.Delay:
                    result = Replace(result, "Duration", XmlEscape(node.Expression ?? "00:00:00"));
                    break;
                case ActivityNodeKind.TryCatch:
                    result = Replace(result, "Try", EmitChildren(node.Children));
                    result = Replace(result, "Catches", EmitChildren(node.CatchChildren));
                    result = Replace(result, "Finally", EmitChildren(node.FinallyChildren));
                    break;
                case ActivityNodeKind.Throw:
                    result = Replace(result, "Exception", XmlEscape(node.Expression ?? "new System.Exception()"));
                    break;
                case ActivityNodeKind.Return:
                    break;
                case ActivityNodeKind.InvokeWorkflow:
                    result = Replace(result, "TargetClass", XmlEscape(node.ScriptRef ?? "Sequence"));
                    break;
            }

            return result;
        }

        private string EmitChildren(List<ActivityNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return "<Sequence />";

            var sb = new StringBuilder();
            foreach (var n in nodes)
            {
                sb.AppendLine();
                sb.Append(EmitNode(n));
            }
            return sb.ToString();
        }

        private string EmitArguments(List<ArgumentAssignment> args)
        {
            if (args == null || args.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("  <InvokeMethod.Arguments>");
            foreach (var a in args)
                sb.AppendLine("    <InArgument x:TypeArguments=\"x:String\">" + XmlEscape(a.Value ?? "") + "</InArgument>");
            sb.AppendLine("  </InvokeMethod.Arguments>");
            return sb.ToString();
        }

        private static string SnippetName(ActivityNodeKind kind)
        {
            switch (kind)
            {
                case ActivityNodeKind.If: return "If";
                case ActivityNodeKind.While: return "While";
                case ActivityNodeKind.ForEach: return "ForEach";
                case ActivityNodeKind.Assign: return "Assign";
                case ActivityNodeKind.InvokeMethod: return "InvokeMethod";
                case ActivityNodeKind.WriteLine: return "WriteLine";
                case ActivityNodeKind.Delay: return "Delay";
                case ActivityNodeKind.TryCatch: return "TryCatch";
                case ActivityNodeKind.Throw: return "Throw";
                case ActivityNodeKind.Return: return "Return";
                case ActivityNodeKind.InvokeWorkflow: return "InvokeWorkflow";
                case ActivityNodeKind.Sequence:
                default: return "Sequence";
            }
        }

        private static string DefaultName(ActivityNodeKind kind) => SnippetName(kind);

        private static string DefaultSnippet(ActivityNodeKind kind)
        {
            switch (kind)
            {
                case ActivityNodeKind.Sequence:
                    return "<Sequence DisplayName=\"{{DisplayName}}\">{{Children}}</Sequence>";
                case ActivityNodeKind.If:
                    return """
                        <If DisplayName="{{DisplayName}}" Condition="[{{Condition}}]">{{Then}}
                          <If.Else>{{Else}}</If.Else>
                        </If>
                        """;
                case ActivityNodeKind.While:
                    return "<While DisplayName=\"{{DisplayName}}\" Condition=\"[{{Condition}}]\">{{Children}}</While>";
                case ActivityNodeKind.ForEach:
                    return """
                        <ForEach x:TypeArguments="x:String" DisplayName="{{DisplayName}}" Values="[{{Values}}]">
                          <ActivityAction x:TypeArguments="x:String">
                            <ActivityAction.Argument>
                              <DelegateInArgument x:TypeArguments="x:String" Name="item" />
                            </ActivityAction.Argument>
                            {{Children}}
                          </ActivityAction>
                        </ForEach>
                        """;
                case ActivityNodeKind.Assign:
                    return """
                        <Assign DisplayName="{{DisplayName}}">
                          <Assign.To>
                            <OutArgument x:TypeArguments="x:String">{{To}}</OutArgument>
                          </Assign.To>
                          <Assign.Value>
                            <InArgument x:TypeArguments="x:String">{{Value}}</InArgument>
                          </Assign.Value>
                        </Assign>
                        """;
                case ActivityNodeKind.InvokeMethod:
                    return """
                        <InvokeMethod DisplayName="{{DisplayName}}" MethodName="{{MethodName}}" TargetType="{x:Type local:{{ClassName}}}">
                        {{Arguments}}
                        </InvokeMethod>
                        """;
                case ActivityNodeKind.WriteLine:
                    return "<WriteLine DisplayName=\"{{DisplayName}}\" Text=\"{{Text}}\" />";
                case ActivityNodeKind.Delay:
                    return "<Delay DisplayName=\"{{DisplayName}}\" Duration=\"{{Duration}}\" />";
                case ActivityNodeKind.TryCatch:
                    return """
                        <TryCatch DisplayName="{{DisplayName}}">
                          <TryCatch.Try>{{Try}}</TryCatch.Try>
                          <TryCatch.Catches>
                            <Catch x:TypeArguments="s:Exception">
                              <ActivityAction x:TypeArguments="s:Exception">
                                <ActivityAction.Argument>
                                  <DelegateInArgument x:TypeArguments="s:Exception" Name="exception" />
                                </ActivityAction.Argument>
                                {{Catches}}
                              </ActivityAction>
                            </Catch>
                          </TryCatch.Catches>
                          <TryCatch.Finally>{{Finally}}</TryCatch.Finally>
                        </TryCatch>
                        """;
                case ActivityNodeKind.Throw:
                    return "<Throw DisplayName=\"{{DisplayName}}\" Exception=\"[{{Exception}}]\" />";
                case ActivityNodeKind.Return:
                    return "<Return DisplayName=\"{{DisplayName}}\" />";
                case ActivityNodeKind.InvokeWorkflow:
                    return "<local:{{TargetClass}} DisplayName=\"{{DisplayName}}\" />";
                default:
                    return "<Sequence DisplayName=\"{{DisplayName}}\" />";
            }
        }

        private static string Replace(string template, string key, string value)
            => template.Replace("{{" + key + "}}", value ?? "");

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

