using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PixToWfConverterTests
    {
        private const string SamplePix = """
            <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns:z="http://schemas.microsoft.com/2003/10/Serialization/" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
              <Name>Test.pix</Name>
              <Logic z:Id="i1" i:type="ExecutedStep">
                <Childs>
                  <AbstractStep z:Id="i2" i:type="ExecutedStep">
                    <Childs />
                    <Parent z:Ref="i1" />
                    <PropertyValues>
                      <PropertyValue><PropertyName>To</PropertyName><_dataType>Expression</_dataType><_expression>url</_expression></PropertyValue>
                      <PropertyValue><PropertyName>Value</PropertyName><_dataType>Expression</_dataType><_expression>https://example.com</_expression></PropertyValue>
                    </PropertyValues>
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Assign</_activityFullName>
                  </AbstractStep>
                  <AbstractStep z:Id="i3" i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep z:Id="i4" i:type="ExecutedStep">
                        <Childs>
                          <AbstractStep z:Id="i5" i:type="ExecutedStep">
                            <Childs />
                            <Parent z:Ref="i4" />
                            <PropertyValues />
                            <Text></Text>
                            <_activityFullName>BR.Core.Base.Sleep</_activityFullName>
                          </AbstractStep>
                        </Childs>
                        <Parent z:Ref="i3" />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>BR.Core.Base.IfTrue</_activityFullName>
                      </AbstractStep>
                      <AbstractStep z:Id="i6" i:type="ExecutedStep">
                        <Childs />
                        <Parent z:Ref="i3" />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>BR.Core.Base.IfFalse</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <Parent z:Ref="i1" />
                    <PropertyValues>
                      <PropertyValue><PropertyName>Condition</PropertyName><_dataType>Expression</_dataType><_expression>true</_expression></PropertyValue>
                    </PropertyValues>
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.If</_activityFullName>
                  </AbstractStep>
                  <AbstractStep z:Id="i7" i:type="CommentStep">
                    <Childs />
                    <Parent z:Ref="i1" />
                    <PropertyValues />
                    <IsCollapsed>false</IsCollapsed>
                    <Text>Комментарий</Text>
                  </AbstractStep>
                </Childs>
                <Parent i:nil="true" />
                <PropertyValues />
                <Text></Text>
                <_activityFullName>BR.Core.Base.Container</_activityFullName>
              </Logic>
            </Script>
            """;

        private static PixToWfConverter CreateConverter(IReadOnlyDictionary<string, string> mappings = null)
        {
            var root = FindActivityLibrary();
            return new PixToWfConverter(
                new PixActivityLibrary(Path.Combine(root, "pix")),
                new WfSnippetLibrary(Path.Combine(root, "wf")),
                mappings);
        }

        private static string FindActivityLibrary()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "RPAWriter2", "ActivityLibrary");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("ActivityLibrary not found above " + AppContext.BaseDirectory);
        }

        [Test]
        public void ConvertToPlan_MapsContainerAssignIfAndComment()
        {
            var warnings = new List<string>();
            var plan = CreateConverter().ConvertToPlan(SamplePix, warnings);

            Assert.That(plan.Name, Is.EqualTo("Test"));
            Assert.That(plan.Root.Kind, Is.EqualTo(ActivityNodeKind.Sequence));
            Assert.That(plan.Root.Children.Count, Is.EqualTo(3));

            var assign = plan.Root.Children[0];
            Assert.That(assign.Kind, Is.EqualTo(ActivityNodeKind.Assign));
            Assert.That(assign.Variable, Is.EqualTo("url"));
            Assert.That(assign.Expression, Is.EqualTo("https://example.com"));

            var ifNode = plan.Root.Children[1];
            Assert.That(ifNode.Kind, Is.EqualTo(ActivityNodeKind.If));
            Assert.That(ifNode.Expression, Is.EqualTo("true"));
            Assert.That(ifNode.Children, Has.Count.EqualTo(1));
            Assert.That(ifNode.Children[0].Kind, Is.EqualTo(ActivityNodeKind.Delay));
            Assert.That(ifNode.ElseChildren, Is.Empty);

            var comment = plan.Root.Children[2];
            Assert.That(comment.Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(comment.Expression, Is.EqualTo("Комментарий"));
        }

        [Test]
        public void Convert_GeneratesXamlAndCode()
        {
            var result = CreateConverter().Convert(SamplePix);

            Assert.That(result.Xaml, Does.Contain("Assign"));
            Assert.That(result.Xaml, Does.Contain("If"));
            Assert.That(result.Xaml, Does.Contain("WriteLine"));
            Assert.That(result.Xaml, Does.Contain("Комментарий"));
        }

        [Test]
        public void Convert_UnknownActivity_MapsToInvokeMethodWithWarning()
        {
            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Unknown.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>Some.Unknown.Activity</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;
            var warnings = new List<string>();

            var plan = CreateConverter().ConvertToPlan(pix, warnings);
            var node = plan.Root.Children[0];

            Assert.That(node.Kind, Is.EqualTo(ActivityNodeKind.InvokeMethod));
            Assert.That(node.MethodName, Is.EqualTo("Activity"));
            Assert.That(node.ClassName, Is.EqualTo("RpaActions"));
            Assert.That(warnings, Does.Contain("Unknown PIX activity \"Some.Unknown.Activity\" mapped to InvokeMethod Activity."));
        }

        [Test]
        public void Convert_ExecuteVBS_EmitsWarning()
        {
            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Vbs.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>Activities.VBS.ExecuteVBS</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;
            var warnings = new List<string>();

            CreateConverter().ConvertToPlan(pix, warnings);

            Assert.That(warnings, Does.Contain("VBScript is not portable to Workflow Foundation; mapped to InvokeMethod RpaActions.ExecuteVBS."));
        }

        [Test]
        public void Convert_WithMapping_OverridesKind()
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BR.Core.Base.Assign", "System.Activities.Statements.WriteLine" }
            };

            var result = CreateConverter(mappings).Convert(SamplePix);
            var assign = result.Plan.Root.Children[0];

            Assert.That(assign.Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
            Assert.That(assign.MappedActivity, Is.EqualTo("System.Activities.Statements.WriteLine"));
            Assert.That(assign.Expression, Is.EqualTo("https://example.com"));
            Assert.That(result.Mappings, Does.Contain("BR.Core.Base.Assign → System.Activities.Statements.WriteLine"));
        }

        [Test]
        public void Convert_WithUnknownMappedType_KeepsBuiltInMappingAndWarns()
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BR.Core.Base.Assign", "My.Custom.WfActivity" }
            };
            var warnings = new List<string>();

            var plan = CreateConverter(mappings).ConvertToPlan(SamplePix, warnings);

            Assert.That(plan.Root.Children[0].Kind, Is.EqualTo(ActivityNodeKind.Assign));
            Assert.That(warnings, Does.Contain("Mapped MWF activity \"My.Custom.WfActivity\" for \"BR.Core.Base.Assign\" is not a supported WF activity; kept the built-in mapping."));
        }

        [Test]
        public void Convert_WithArgumentMapping_AppliesRenameAndConversion()
        {
            var root = FindActivityLibrary();
            var argumentMappings = new Dictionary<string, IReadOnlyList<PixMwfArgumentMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Some.Unknown.Activity",
                    new List<PixMwfArgumentMapping>
                    {
                        new PixMwfArgumentMapping
                        {
                            PixArgument = "Text",
                            MwfArgument = "Message",
                            Conversion = ConversionKind.ToString
                        }
                    }
                }
            };

            var converter = new PixToWfConverter(
                new PixActivityLibrary(Path.Combine(root, "pix")),
                new WfSnippetLibrary(Path.Combine(root, "wf")),
                null,
                argumentMappings);

            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Args.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues>
                          <PropertyValue><PropertyName>Text</PropertyName><_dataType>Expression</_dataType><_expression>hello</_expression></PropertyValue>
                        </PropertyValues>
                        <Text></Text>
                        <_activityFullName>Some.Unknown.Activity</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;

            var warnings = new List<string>();
            var plan = converter.ConvertToPlan(pix, warnings);
            var node = plan.Root.Children[0];

            Assert.That(node.Kind, Is.EqualTo(ActivityNodeKind.InvokeMethod));

            var arg = node.Arguments.FirstOrDefault(a => a.Parameter == "Message");
            Assert.That(arg, Is.Not.Null);
            Assert.That(arg.Value, Is.EqualTo("(hello).ToString()"));
            Assert.That(warnings, Does.Contain("Applied conversion ToString to argument Text → Message of Some.Unknown.Activity."));
        }

        [Test]
        public void Convert_RealPixProject_DoesNotThrow()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pixRoot = Path.Combine(localAppData, "PIX", "Projects");
            if (!Directory.Exists(pixRoot))
            {
                Assert.Ignore("No PIX projects directory found.");
                return;
            }

            var mainFiles = Directory.GetFiles(pixRoot, "main.pix", SearchOption.AllDirectories);
            if (mainFiles.Length == 0)
            {
                Assert.Ignore("No main.pix found.");
                return;
            }

            var result = CreateConverter().Convert(File.ReadAllText(mainFiles[0]));

            Assert.That(result.Plan.Root, Is.Not.Null);
            Assert.That(result.Xaml, Is.Not.Empty);
            Assert.That(result.CSharpCode, Is.Not.Empty);
        }

        [Test]
        public void Convert_FailOnUnmapped_ThrowsForUnknownActivity()
        {
            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Unknown.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>Some.Unknown.Activity</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;

            var converter = CreateConverter();
            converter.FailOnUnmapped = true;

            Assert.That(() => converter.Convert(pix),
                Throws.TypeOf<PixConversionException>()
                    .With.Message.Contains("Some.Unknown.Activity"));
        }

        [Test]
        public void Convert_FailOnUnmapped_AllowsBuiltInAndMappedActivities()
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Some.Custom.Activity", "System.Activities.Statements.WriteLine" }
            };

            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Mixed.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>BR.Core.Base.Assign</_activityFullName>
                      </AbstractStep>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>Some.Custom.Activity</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;

            var converter = CreateConverter(mappings);
            converter.FailOnUnmapped = true;

            var result = converter.Convert(pix);

            Assert.That(result.Plan.Root.Children[0].Kind, Is.EqualTo(ActivityNodeKind.Assign));
            Assert.That(result.Plan.Root.Children[1].Kind, Is.EqualTo(ActivityNodeKind.WriteLine));
        }

        [Test]
        public void Convert_FailOnUnmapped_ThrowsForStatementWithoutBuiltInSnippet()
        {
            var pix = """
                <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                  <Name>Vbs.pix</Name>
                  <Logic i:type="ExecutedStep">
                    <Childs>
                      <AbstractStep i:type="ExecutedStep">
                        <Childs />
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>Activities.VBS.ExecuteVBS</_activityFullName>
                      </AbstractStep>
                    </Childs>
                    <PropertyValues />
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.Container</_activityFullName>
                  </Logic>
                </Script>
                """;

            var converter = CreateConverter();
            converter.FailOnUnmapped = true;

            Assert.That(() => converter.Convert(pix),
                Throws.TypeOf<PixConversionException>()
                    .With.Message.Contains("Activities.VBS.ExecuteVBS"));
        }
    }
}
