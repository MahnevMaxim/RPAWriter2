using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class RobotGeneratorTests
    {
        [Test]
        public void EmitWorkflowXaml_ContainsInvokeMethodAndAssign()
        {
            var plan = new RobotPlan
            {
                Name = "TestWorkflow",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = new List<ActivityNode>
                    {
                        new ActivityNode { Kind = ActivityNodeKind.Assign, Variable = "url", Expression = "http://example.com" },
                        new ActivityNode { Kind = ActivityNodeKind.InvokeMethod, ClassName = "RpaActions", MethodName = "OpenBrowser" }
                    }
                }
            };

            var xaml = new RobotGenerator().EmitWorkflowXaml(plan);

            Assert.That(xaml, Does.Contain("InvokeMethod"));
            Assert.That(xaml, Does.Contain("Assign"));
            Assert.That(xaml, Does.Contain("OpenBrowser"));
            Assert.That(xaml, Does.Contain("RpaActions"));
        }

        [Test]
        public void EmitHelperCode_ContainsMethods()
        {
            var plan = new RobotPlan
            {
                Name = "TestWorkflow",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = new List<ActivityNode>
                    {
                        new ActivityNode { Kind = ActivityNodeKind.InvokeMethod, ClassName = "RpaActions", MethodName = "OpenBrowser" },
                        new ActivityNode { Kind = ActivityNodeKind.InvokeMethod, ClassName = "RpaActions", MethodName = "ReadTable" }
                    }
                }
            };

            var code = new RobotGenerator().EmitHelperCode(plan);

            Assert.That(code, Does.Contain("OpenBrowser"));
            Assert.That(code, Does.Contain("ReadTable"));
            Assert.That(code, Does.Contain("class RpaActions"));
        }

        [Test]
        public void EmitWorkflowXaml_EscapesXmlCharacters()
        {
            var plan = new RobotPlan
            {
                Name = "Test",
                Root = new ActivityNode { Kind = ActivityNodeKind.WriteLine, Expression = "a < b & c" }
            };

            var xaml = new RobotGenerator().EmitWorkflowXaml(plan);

            Assert.That(xaml, Does.Contain("a &lt; b &amp; c"));
        }
    }
}
