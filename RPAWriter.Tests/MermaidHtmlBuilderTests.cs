using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MermaidHtmlBuilderTests
    {
        [Test]
        public void Build_ContainsMermaidJsAndCode()
        {
            var html = MermaidHtmlBuilder.Build("function mermaid(){}", "flowchart LR\n    A --> B", "dark");

            Assert.That(html, Does.Contain("function mermaid(){}"));
            Assert.That(html, Does.Contain("flowchart LR"));
            Assert.That(html, Does.Contain("mermaid.initialize"));
            Assert.That(html, Does.Contain("mermaid.render"));
            Assert.That(html, Does.Contain("<html>"));
        }

        [Test]
        public void Build_EmbedsCodeAsJsonString()
        {
            var html = MermaidHtmlBuilder.Build("//js", "A[<br/>] --> B[-->]", "dark");

            Assert.That(html, Does.Contain("\"A[<br/>] --> B[-->]\""));
        }

        [Test]
        public void Build_PreservesEmoji()
        {
            var html = MermaidHtmlBuilder.Build("//js", "A[🤖 RPA Writer] --> B[📄 Чтение ТЗ]", "dark");

            Assert.That(html, Does.Contain("🤖"));
            Assert.That(html, Does.Contain("📄"));
        }

        [Test]
        public void Build_NullInputs_DoesNotThrow()
        {
            var html = MermaidHtmlBuilder.Build(null, null, null);

            Assert.That(html, Does.Contain("<html>"));
        }

        [Test]
        public void Build_ForwardsMouseWheelToHost()
        {
            var html = MermaidHtmlBuilder.Build("//js", "flowchart LR\n    A --> B", "dark");

            Assert.That(html, Does.Contain("window.addEventListener('wheel'"));
            Assert.That(html, Does.Contain("post({ wheel: e.deltaY })"));
            Assert.That(html, Does.Contain("passive: false"));
        }
    }
}
