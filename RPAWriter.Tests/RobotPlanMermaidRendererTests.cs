using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class RobotPlanMermaidRendererTests
    {
        [Test]
        public void Render_EmitsFlowchartWithTreeStructure()
        {
            var plan = new RobotPlan
            {
                Name = "Demo",
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.Sequence,
                    Name = "Root",
                    Children =
                    {
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.If,
                            Name = "Check",
                            Expression = "x > 0",
                            Children =
                            {
                                new ActivityNode
                                {
                                    Kind = ActivityNodeKind.Assign,
                                    Variable = "result",
                                    Expression = "\"ok\""
                                }
                            },
                            ElseChildren =
                            {
                                new ActivityNode
                                {
                                    Kind = ActivityNodeKind.WriteLine,
                                    Expression = "negative"
                                }
                            }
                        },
                        new ActivityNode
                        {
                            Kind = ActivityNodeKind.InvokeMethod,
                            ClassName = "RpaActions",
                            MethodName = "Run"
                        }
                    }
                }
            };

            var result = RobotPlanMermaidRenderer.Render(plan);

            Assert.That(result, Does.StartWith("flowchart TD"));
            Assert.That(result, Does.Contain("Sequence: Root"));
            Assert.That(result, Does.Contain("If: Check"));
            Assert.That(result, Does.Contain("-->|then|"));
            Assert.That(result, Does.Contain("-->|else|"));
            Assert.That(result, Does.Contain("InvokeMethod: RpaActions.Run()"));
        }

        [Test]
        public void Render_EscapesQuotesInLabels()
        {
            var plan = new RobotPlan
            {
                Root = new ActivityNode
                {
                    Kind = ActivityNodeKind.WriteLine,
                    Name = "Say",
                    Expression = "He said \"hi\""
                }
            };

            var result = RobotPlanMermaidRenderer.Render(plan);

            Assert.That(result, Does.Contain("He said #quot;hi#quot;"));
            Assert.That(result, Does.Not.Contain("He said \"hi\""));
        }

        [Test]
        public void Render_NullPlan_DoesNotThrow()
        {
            Assert.That(RobotPlanMermaidRenderer.Render(null), Does.Contain("flowchart TD"));
        }
    }
}
