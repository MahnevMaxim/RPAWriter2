using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MwfActivitySourceImporterTests
    {
        [Test]
        public void Import_AirpaSource_ResolvesRussianLocalizedDisplayNameAndDescription()
        {
            var root = Path.Combine(Path.GetTempPath(), "RPAWriter2_Src_" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(Path.GetTempPath(), "RPAWriter2_SrcTarget_" + Guid.NewGuid().ToString("N"));
            try
            {
                var srcDir = Path.Combine(root, "Activities.Browser");
                Directory.CreateDirectory(Path.Combine(srcDir, "Main"));
                Directory.CreateDirectory(Path.Combine(srcDir, "Resources"));

                WriteAll(Path.Combine(srcDir, "Main", "GoToUrl.cs"), @"
using System.Activities;
using ActivityLibraryBase.Attributes;
using Activities.Browser.Resources;

namespace Activities.Browser
{
    [LocalizedDescription(""go_to_url"", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName(""go_to_url"", typeof(ActivitiesBrowser))]
    public sealed class GoToUrl : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription(""page_desc"", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName(""page"", typeof(ActivitiesBrowser))]
        public InArgument<IPage> Page { get; set; }

        [RequiredArgument]
        [LocalizedDescription(""url_desc"", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName(""url"", typeof(ActivitiesBrowser))]
        public InArgument<string> Url { get; set; }
    }
}
");

                WriteAll(Path.Combine(srcDir, "Resources", "ActivitiesBrowser.resx"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""go_to_url""><value>Go to URL</value></data>
  <data name=""page""><value>Page</value></data>
  <data name=""page_desc""><value>Browser page</value></data>
</root>
");

                WriteAll(Path.Combine(srcDir, "Resources", "ActivitiesBrowser.ru-RU.resx"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""go_to_url""><value>Перейти на url</value></data>
  <data name=""page""><value>Страница</value></data>
  <data name=""page_desc""><value>Страница браузера</value></data>
</root>
");

                var result = new MwfActivitySourceImporter().Import(root, target);

                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.ActivitiesImported, Is.EqualTo(1));

                var library = new MwfActivityLibrary(target);
                var descriptor = library.Load("Activities.Browser.GoToUrl");
                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.DefaultDisplayName, Is.EqualTo("Перейти на url"));
                Assert.That(descriptor.Description, Is.EqualTo("Перейти на url"));
                Assert.That(descriptor.Parameters.Count, Is.EqualTo(2));

                var page = descriptor.Parameters.FirstOrDefault(p => p.Name == "Page");
                Assert.That(page, Is.Not.Null);
                Assert.That(page.Direction, Is.EqualTo("In"));
                Assert.That(page.DisplayName, Is.EqualTo("Страница"));
                Assert.That(page.Description, Is.EqualTo("Страница браузера"));
                Assert.That(page.IsRequired, Is.True);

                // Missing keys (url / url_desc) must fall back to the property name / empty description.
                var url = descriptor.Parameters.FirstOrDefault(p => p.Name == "Url");
                Assert.That(url, Is.Not.Null);
                Assert.That(url.DisplayName, Is.EqualTo("Url"));
                Assert.That(url.Description, Is.EqualTo(string.Empty));
                Assert.That(url.Direction, Is.EqualTo("In"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }

        private static void WriteAll(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        [Test]
        public void Import_MwfSource_ReadsDispNameAndDescriptionAttribute()
        {
            var root = Path.Combine(Path.GetTempPath(), "RPAWriter2_Src_" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(Path.GetTempPath(), "RPAWriter2_SrcTarget_" + Guid.NewGuid().ToString("N"));
            try
            {
                var srcDir = Path.Combine(root, "Custom");
                Directory.CreateDirectory(srcDir);

                WriteAll(Path.Combine(srcDir, "MyActivity.cs"), @"
using System.Activities;
using System.ComponentModel;

namespace Custom
{
    [Description(""My activity"")]
    public class MyActivity : CodeActivity
    {
        public string DispName => ""My Activity"";

        [DisplayName(""Text"")]
        [Description(""Input text"")]
        public InArgument<string> Text { get; set; }
    }
}
");

                var result = new MwfActivitySourceImporter().Import(root, target);

                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.ActivitiesImported, Is.EqualTo(1));

                var descriptor = new MwfActivityLibrary(target).Load("Custom.MyActivity");
                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.DefaultDisplayName, Is.EqualTo("My Activity"));
                Assert.That(descriptor.Description, Is.EqualTo("My activity"));
                Assert.That(descriptor.Parameters.Count, Is.EqualTo(1));

                var text = descriptor.Parameters[0];
                Assert.That(text.Name, Is.EqualTo("Text"));
                Assert.That(text.DisplayName, Is.EqualTo("Text"));
                Assert.That(text.Description, Is.EqualTo("Input text"));
                Assert.That(text.Direction, Is.EqualTo("In"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }

        [Test]
        public void IsSourceDirectory_DetectsCsSources()
        {
            var root = Path.Combine(Path.GetTempPath(), "RPAWriter2_Src_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                Assert.That(MwfActivitySourceImporter.IsSourceDirectory(root), Is.False);

                File.WriteAllText(Path.Combine(root, "Activity.cs"), "class A {}");
                Assert.That(MwfActivitySourceImporter.IsSourceDirectory(root), Is.True);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
