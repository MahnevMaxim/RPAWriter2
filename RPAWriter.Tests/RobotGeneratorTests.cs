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

        [Test]
        public void EmitWorkflowXaml_NullPlan_Throws()
        {
            Assert.That(() => new RobotGenerator().EmitWorkflowXaml(null), Throws.ArgumentNullException);
        }

        [Test]
        public void EmitWorkflowXaml_NullRoot_EmitsEmptySequence()
        {
            var xaml = new RobotGenerator().EmitWorkflowXaml(new RobotPlan { Name = "Empty" });

            Assert.That(xaml, Does.Contain("<Sequence"));
            Assert.That(xaml, Does.Contain("</Sequence>"));
        }

        [Test]
        public void EmitWorkflowXaml_EmitsControlFlowKinds()
        {
            var plan = new RobotPlan
            {
                Name = "ControlFlow",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = new List<ActivityNode>
                    {
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.If,
                            Expression = "x > 0",
                            Children = new List<ActivityNode> { new ActivityNode { Kind = ActivityNodeKind.WriteLine, Expression = "positive" } }
                        },
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.While,
                            Expression = "true",
                            Children = new List<ActivityNode> { new ActivityNode { Kind = ActivityNodeKind.Delay, Expression = "00:00:01" } }
                        },
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.ForEach,
                            Expression = "items",
                            Children = new List<ActivityNode> { new ActivityNode { Kind = ActivityNodeKind.WriteLine, Expression = "item" } }
                        }
                    }
                }
            };

            var xaml = new RobotGenerator().EmitWorkflowXaml(plan);

            Assert.That(xaml, Does.Contain("<If"));
            Assert.That(xaml, Does.Contain("<While"));
            Assert.That(xaml, Does.Contain("<ForEach"));
            Assert.That(xaml, Does.Contain("<Delay"));
            Assert.That(xaml, Does.Contain("<WriteLine"));
        }

        [Test]
        public void EmitWorkflowXaml_InvokeMethod_EmitsArguments()
        {
            var plan = new RobotPlan
            {
                Name = "Args",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = new List<ActivityNode>
                    {
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.InvokeMethod,
                            ClassName = "RpaActions",
                            MethodName = "TypeInto",
                            Arguments = new List<ArgumentAssignment>
                            {
                                new ArgumentAssignment { Parameter = "Selector", Value = "input[name=q]" },
                                new ArgumentAssignment { Parameter = "Text", Value = "hello" }
                            }
                        }
                    }
                }
            };

            var xaml = new RobotGenerator().EmitWorkflowXaml(plan);

            Assert.That(xaml, Does.Contain("<InvokeMethod"));
            Assert.That(xaml, Does.Contain("<InvokeMethod.Arguments>"));
            Assert.That(xaml, Does.Contain("input[name=q]"));
            Assert.That(xaml, Does.Contain("hello"));
        }

        [Test]
        public void EmitHelperCode_NullPlan_Throws()
        {
            Assert.That(() => new RobotGenerator().EmitHelperCode(null), Throws.ArgumentNullException);
        }

        [Test]
        public void EmitHelperCode_SanitizesMethodNames()
        {
            var plan = new RobotPlan
            {
                Name = "Sanitize",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Children = new List<ActivityNode>
                    {
                        new ActivityNode { Kind = ActivityNodeKind.InvokeMethod, MethodName = "Open Browser!" },
                        new ActivityNode { Kind = ActivityNodeKind.InvokeMethod, MethodName = "1Start" }
                    }
                }
            };

            var code = new RobotGenerator().EmitHelperCode(plan);

            Assert.That(code, Does.Contain("OpenBrowser"));
            Assert.That(code, Does.Contain("_1Start"));
            Assert.That(code, Does.Not.Contain("Open Browser!"));
        }
    }
}
