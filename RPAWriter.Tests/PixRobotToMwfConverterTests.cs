using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PixRobotToMwfConverterTests
    {
        private const string MainPix = """
            <?xml version="1.0" encoding="utf-8"?>
            <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
              <Name>main.pix</Name>
              <Logic i:type="ExecutedStep">
                <Childs>
                  <AbstractStep i:type="ExecutedStep">
                    <Childs />
                    <PropertyValues>
                      <PropertyValue><PropertyName>FilePath</PropertyName><_dataType>Expression</_dataType><_expression>@"newScript.pix"</_expression></PropertyValue>
                    </PropertyValues>
                    <Text></Text>
                    <_activityFullName>BR.Core.Base.ExecuteScript</_activityFullName>
                  </AbstractStep>
                </Childs>
                <PropertyValues />
                <Text></Text>
                <_activityFullName>BR.Core.Base.Container</_activityFullName>
              </Logic>
            </Script>
            """;

        private const string NewScriptPix = """
            <?xml version="1.0" encoding="utf-8"?>
            <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
              <Name>newScript.pix</Name>
              <Logic i:type="ExecutedStep">
                <Childs />
                <PropertyValues />
                <Text></Text>
                <_activityFullName>BR.Core.Base.Container</_activityFullName>
              </Logic>
            </Script>
            """;

        private const string Pixproj = """
            <?xml version="1.0" encoding="utf-8"?>
            <RobotizationProject xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
              <Name>TestRobot</Name>
              <Files>
                <ProjectFileItem><IsMain>true</IsMain><Path>main.pix</Path></ProjectFileItem>
                <ProjectFileItem><IsMain>false</IsMain><Path>newScript.pix</Path></ProjectFileItem>
              </Files>
            </RobotizationProject>
            """;

        private PixRobotToMwfConverter CreateConverter()
        {
            var root = FindActivityLibrary();
            return new PixRobotToMwfConverter(
                new PixActivityLibrary(Path.Combine(root, "pix")),
                new WfSnippetLibrary(Path.Combine(root, "wf")));
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

        private static string CreateTempFolder()
        {
            var path = Path.Combine(Path.GetTempPath(), "PixRobotToMwfConverterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteText(string folder, string fileName, string content)
        {
            File.WriteAllText(Path.Combine(folder, fileName), content);
        }

        [Test]
        public void Convert_WithPixproj_GeneratesMainAndChildInOrder()
        {
            var folder = CreateTempFolder();
            try
            {
                WriteText(folder, "TestRobot.pixproj", Pixproj);
                WriteText(folder, "main.pix", MainPix);
                WriteText(folder, "newScript.pix", NewScriptPix);

                var result = CreateConverter().Convert(folder);

                Assert.That(result.RobotName, Is.EqualTo("TestRobot"));
                Assert.That(result.Scripts.Count, Is.EqualTo(2));

                Assert.That(result.Scripts[0].SourceFileName, Is.EqualTo("main.pix"));
                Assert.That(result.Scripts[0].OutputFileName, Is.EqualTo("Main.xaml"));
                Assert.That(result.Scripts[0].Xaml, Does.Contain("<local:newScript"));

                Assert.That(result.Scripts[1].SourceFileName, Is.EqualTo("newScript.pix"));
                Assert.That(result.Scripts[1].OutputFileName, Is.EqualTo("newScript.xaml"));

                Assert.That(result.AggregateCSharpCode, Does.Contain("RpaActions"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void Convert_WithoutPixproj_FollowsExecuteScriptReferencesOnly()
        {
            var folder = CreateTempFolder();
            try
            {
                WriteText(folder, "main.pix", MainPix);
                WriteText(folder, "newScript.pix", NewScriptPix);
                WriteText(folder, "orphan.pix", NewScriptPix.Replace("newScript.pix", "orphan.pix"));

                var result = CreateConverter().Convert(folder);

                Assert.That(result.Scripts.Count, Is.EqualTo(2));
                Assert.That(result.Scripts[0].SourceFileName, Is.EqualTo("main.pix"));
                Assert.That(result.Scripts[1].SourceFileName, Is.EqualTo("newScript.pix"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void ConvertAndWrite_WritesWorkflowsHelperAndReport()
        {
            var pixFolder = CreateTempFolder();
            var mwfFolder = CreateTempFolder();
            try
            {
                WriteText(pixFolder, "TestRobot.pixproj", Pixproj);
                WriteText(pixFolder, "main.pix", MainPix);
                WriteText(pixFolder, "newScript.pix", NewScriptPix);

                CreateConverter().ConvertAndWrite(pixFolder, mwfFolder);

                Assert.That(File.Exists(Path.Combine(mwfFolder, "Main.xaml")), Is.True);
                Assert.That(File.Exists(Path.Combine(mwfFolder, "newScript.xaml")), Is.True);
                Assert.That(File.Exists(Path.Combine(mwfFolder, "RpaActions.cs")), Is.True);
                Assert.That(File.Exists(Path.Combine(mwfFolder, "conversion-report.txt")), Is.True);

                var mainXaml = File.ReadAllText(Path.Combine(mwfFolder, "Main.xaml"));
                Assert.That(mainXaml, Does.Contain("x:Class=\"Robots.main\""));
                Assert.That(mainXaml, Does.Contain("<local:newScript"));
            }
            finally
            {
                Directory.Delete(pixFolder, true);
                Directory.Delete(mwfFolder, true);
            }
        }

        [Test]
        public void Convert_RealPixRobotFolder_DoesNotThrow()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pixRoot = Path.Combine(localAppData, "PIX", "Projects");
            if (!Directory.Exists(pixRoot))
            {
                Assert.Ignore("No PIX projects directory found.");
                return;
            }

            var pixproj = Directory.GetFiles(pixRoot, "*.pixproj", SearchOption.AllDirectories).FirstOrDefault();
            if (pixproj == null)
            {
                Assert.Ignore("No .pixproj found.");
                return;
            }

            var folder = Path.GetDirectoryName(pixproj);
            var result = CreateConverter().Convert(folder);

            Assert.That(result.Scripts.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Scripts[0].OutputFileName, Is.EqualTo("Main.xaml"));
            Assert.That(result.Scripts.All(s => !string.IsNullOrEmpty(s.Xaml)), Is.True);
        }

        [Test]
        public void Convert_FailOnUnmapped_ThrowsWithScriptName()
        {
            var folder = CreateTempFolder();
            try
            {
                WriteText(folder, "main.pix", """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                      <Name>main.pix</Name>
                      <Logic i:type="ExecutedStep">
                        <Childs>
                          <AbstractStep i:type="ExecutedStep">
                            <Childs />
                            <PropertyValues>
                              <PropertyValue><PropertyName>FilePath</PropertyName><_dataType>Expression</_dataType><_expression>@"sub.pix"</_expression></PropertyValue>
                            </PropertyValues>
                            <Text></Text>
                            <_activityFullName>BR.Core.Base.ExecuteScript</_activityFullName>
                          </AbstractStep>
                        </Childs>
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>BR.Core.Base.Container</_activityFullName>
                      </Logic>
                    </Script>
                    """);

                WriteText(folder, "sub.pix", """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Script xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/BR.Logic">
                      <Name>sub.pix</Name>
                      <Logic i:type="ExecutedStep">
                        <Childs>
                          <AbstractStep i:type="ExecutedStep">
                            <Childs />
                            <PropertyValues />
                            <Text></Text>
                            <_activityFullName>Some.Unmapped.Activity</_activityFullName>
                          </AbstractStep>
                        </Childs>
                        <PropertyValues />
                        <Text></Text>
                        <_activityFullName>BR.Core.Base.Container</_activityFullName>
                      </Logic>
                    </Script>
                    """);

                var converter = CreateConverter();
                converter.FailOnUnmapped = true;

                Assert.That(() => converter.Convert(folder),
                    Throws.TypeOf<PixConversionException>()
                        .With.Message.Contains("sub.pix")
                        .And.Message.Contains("Some.Unmapped.Activity"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
