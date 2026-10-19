using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class UiPathToWfConverterTests
    {
        private const string SampleXaml = """
<Activity x:Class="Main" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities" xmlns:web="clr-namespace:UiPath.Web.Activities;assembly=UiPath.Web.Activities">
  <Sequence DisplayName="Main Sequence">
    <Sequence.Variables>
      <Variable x:TypeArguments="x:String" Name="url" />
    </Sequence.Variables>
    <ui:Assign DisplayName="Set URL" To="[url]" Value="https://example.com" />
    <web:OpenBrowser DisplayName="Open" Url="[url]" />
    <ui:TypeInto DisplayName="Type" Selector="input[name=q]" Text="hello" />
    <ui:WriteLine DisplayName="Log" Text="done" />
  </Sequence>
</Activity>
""";

        private static UiPathToWfConverter CreateConverter() => new UiPathToWfConverter(new RobotGenerator());

        [Test]
        public void ConvertToPlan_MapsStandardActivities()
        {
            var plan = CreateConverter().ConvertToPlan(SampleXaml);

            Assert.That(plan.Name, Is.EqualTo("Main"));
            Assert.That(plan.Root.Kind, Is.EqualTo(ActivityNodeKind.Sequence));
            Assert.That(plan.Root.Children.Count, Is.EqualTo(4));

            var assign = plan.Root.Children[0];
            Assert.That(assign.Kind, Is.EqualTo(ActivityNodeKind.Assign));
            Assert.That(assign.Variable, Is.EqualTo("[url]"));
            Assert.That(assign.Expression, Is.EqualTo("https://example.com"));

            var open = plan.Root.Children[1];
            Assert.That(open.Kind, Is.EqualTo(ActivityNodeKind.InvokeMethod));
            Assert.That(open.MethodName, Is.EqualTo("OpenBrowser"));
            Assert.That(open.ClassName, Is.EqualTo("RpaActions"));

            var type = plan.Root.Children[2];
            Assert.That(type.Kind, Is.EqualTo(ActivityNodeKind.InvokeMethod));
            Assert.That(type.MethodName, Is.EqualTo("TypeInto"));
            Assert.That(type.Arguments, Has.Count.EqualTo(2)); // Selector, Text

            var write = plan.Root.Children[3];
            Assert.That(write.Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(write.Expression, Is.EqualTo("done"));
        }

        [Test]
        public void ConvertToPlan_SkipsVariablesAndDisplayName()
        {
            var plan = CreateConverter().ConvertToPlan(SampleXaml);

            // Variables must not end up in the tree as activities.
            Assert.That(plan.Root.Children.Count, Is.EqualTo(4));

            // DisplayName must not become an InvokeMethod argument.
            var open = plan.Root.Children[1];
            Assert.That(open.Arguments.Exists(a => a.Parameter == "DisplayName"), Is.False);
        }

        [Test]
        public void Convert_GeneratesXamlAndCode()
        {
            var result = CreateConverter().Convert(SampleXaml);

            Assert.That(result.Xaml, Does.Contain("InvokeMethod"));
            Assert.That(result.Xaml, Does.Contain("Assign"));
            Assert.That(result.CSharpCode, Does.Contain("OpenBrowser"));
            Assert.That(result.CSharpCode, Does.Contain("TypeInto"));
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void ConvertToPlan_MapsNestedControlFlow()
        {
            var xaml = """
<Activity x:Class="Nested" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <Sequence>
    <ui:If DisplayName="Check" Condition="[x &gt; 0]">
      <ui:If.Then>
        <ui:WriteLine Text="positive" />
      </ui:If.Then>
      <ui:If.Else>
        <ui:WriteLine Text="non-positive" />
      </ui:If.Else>
    </ui:If>
  </Sequence>
</Activity>
""";

            var plan = CreateConverter().ConvertToPlan(xaml);

            Assert.That(plan.Root.Kind, Is.EqualTo(ActivityNodeKind.Sequence));
            var ifNode = plan.Root.Children[0];
            Assert.That(ifNode.Kind, Is.EqualTo(ActivityNodeKind.If));
            Assert.That(ifNode.Expression, Is.EqualTo("[x > 0]"));
            Assert.That(ifNode.Children.Count, Is.EqualTo(2));
            Assert.That(ifNode.Children[0].Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(ifNode.Children[1].Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
        }

        [Test]
        public void ConvertToPlan_Flowchart_DowngradesToSequenceWithWarning()
        {
            var xaml = """
<Activity x:Class="Flow" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <Flowchart DisplayName="Main Flow">
    <Flowchart.Nodes>
      <ui:WriteLine Text="step" />
    </Flowchart.Nodes>
  </Flowchart>
</Activity>
""";
            var warnings = new List<string>();

            var plan = CreateConverter().ConvertToPlan(xaml, warnings);

            Assert.That(plan.Root.Kind, Is.EqualTo(ActivityNodeKind.Sequence));
            Assert.That(plan.Root.Children.Count, Is.EqualTo(1));
            Assert.That(plan.Root.Children[0].Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(warnings, Does.Contain("Flowchart converted to Sequence (simplification)."));
        }

        [Test]
        public void ConvertToPlan_DoWhile_MapsToWhile()
        {
            var xaml = """
<Activity x:Class="Loop" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <Sequence>
    <ui:DoWhile Condition="[x &lt; 10]">
      <ui:DoWhile.Body>
        <ui:WriteLine Text="loop" />
      </ui:DoWhile.Body>
    </ui:DoWhile>
  </Sequence>
</Activity>
""";

            var plan = CreateConverter().ConvertToPlan(xaml);
            var loop = plan.Root.Children[0];

            Assert.That(loop.Kind, Is.EqualTo(ActivityNodeKind.While));
            Assert.That(loop.Expression, Is.EqualTo("[x < 10]"));
        }

        [Test]
        public void ConvertToPlan_TryCatch_DowngradesToSequenceWithWarning()
        {
            var xaml = """
<Activity x:Class="Try" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <ui:TryCatch DisplayName="Guarded">
    <ui:TryCatch.Try>
      <ui:WriteLine Text="body" />
    </ui:TryCatch.Try>
  </ui:TryCatch>
</Activity>
""";
            var warnings = new List<string>();

            var plan = CreateConverter().ConvertToPlan(xaml, warnings);

            Assert.That(plan.Root.Kind, Is.EqualTo(ActivityNodeKind.Sequence));
            Assert.That(warnings, Does.Contain("TryCatch converted to Sequence (simplification)."));
        }

        [Test]
        public void ConvertToPlan_LogMessage_MapsToWriteLine()
        {
            var xaml = """
<Activity x:Class="Log" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <Sequence>
    <ui:LogMessage DisplayName="Log" Message="done" />
  </Sequence>
</Activity>
""";

            var plan = CreateConverter().ConvertToPlan(xaml);
            var node = plan.Root.Children[0];

            Assert.That(node.Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(node.Expression, Is.EqualTo("done"));
        }

        [Test]
        public void ConvertToPlan_UnknownActivity_MapsToInvokeMethodWithWarning()
        {
            var xaml = """
<Activity x:Class="Unknown" xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:UiPath.Core.Activities;assembly=UiPath.Activities">
  <Sequence>
    <ui:Click DisplayName="Click" Selector="#btn" />
  </Sequence>
</Activity>
""";
            var warnings = new List<string>();

            var plan = CreateConverter().ConvertToPlan(xaml, warnings);
            var node = plan.Root.Children[0];

            Assert.That(node.Kind, Is.EqualTo(ActivityNodeKind.InvokeMethod));
            Assert.That(node.MethodName, Is.EqualTo("Click"));
            Assert.That(node.ClassName, Is.EqualTo("RpaActions"));
            Assert.That(node.Arguments, Has.Count.EqualTo(1));
            Assert.That(node.Arguments[0].Parameter, Is.EqualTo("Selector"));
            Assert.That(warnings, Does.Contain("Activity Click mapped to InvokeMethod Click."));
        }
    }
}
