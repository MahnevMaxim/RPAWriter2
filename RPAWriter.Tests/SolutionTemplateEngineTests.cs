using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SolutionTemplateEngineTests
    {
        [Test]
        public void Apply_NullTemplate_Throws()
        {
            Assert.That(() => new SolutionTemplateEngine().Apply(null, new Dictionary<string, string>()), Throws.ArgumentNullException);
        }

        [Test]
        public void Apply_MissingPlaceholderKey_LeavesPlaceholder()
        {
            var template = new SolutionRecord
            {
                XamlFragment = "Open {{url}}",
                CSharpCode = "Run(\"{{url}}\")"
            };

            var result = new SolutionTemplateEngine().Apply(template, new Dictionary<string, string>());

            Assert.That(result.XamlFragment, Is.EqualTo("Open {{url}}"));
            Assert.That(result.CSharpCode, Is.EqualTo("Run(\"{{url}}\")"));
        }

        [Test]
        public void Apply_NullOrEmptyParameters_ReturnsClone()
        {
            var template = new SolutionRecord { Name = "N", XamlFragment = "x {{url}}" };
            var engine = new SolutionTemplateEngine();

            var fromNull = engine.Apply(template, null);
            var fromEmpty = engine.Apply(template, new Dictionary<string, string>());

            Assert.That(fromNull.XamlFragment, Is.EqualTo("x {{url}}"));
            Assert.That(fromEmpty.XamlFragment, Is.EqualTo("x {{url}}"));
            Assert.That(fromNull.Name, Is.EqualTo("N"));
        }

        [Test]
        public void Apply_WhitespaceInsideBraces_IsReplaced()
        {
            var template = new SolutionRecord { XamlFragment = "{{ url }}" };

            var result = new SolutionTemplateEngine().Apply(template, new Dictionary<string, string> { { "url", "v" } });

            Assert.That(result.XamlFragment, Is.EqualTo("v"));
        }

        [Test]
        public void Clone_DeepCopiesNestedCollections()
        {
            var source = new SolutionRecord
            {
                Id = "1",
                Name = "Robot",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor
                    {
                        Action = "open",
                        Inputs = new List<string> { "a" },
                        Outputs = new List<string> { "b" },
                        Keywords = new List<string> { "k" }
                    }
                }
            };

            var clone = new SolutionTemplateEngine().Clone(source);
            clone.Tasks[0].Inputs.Add("c");
            clone.Tasks[0].Action = "changed";

            Assert.That(source.Tasks[0].Inputs, Does.Not.Contain("c"));
            Assert.That(source.Tasks[0].Action, Is.EqualTo("open"));
            Assert.That(clone.Tasks[0].Inputs, Does.Contain("c"));
        }
    }
}
