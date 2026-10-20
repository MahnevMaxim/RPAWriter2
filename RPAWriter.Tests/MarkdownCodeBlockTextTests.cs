using Markdig;
using Markdig.Syntax;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MarkdownCodeBlockTextTests
    {
        [Test]
        public void GetText_FencedMermaidBlock_ReturnsFlowchartText()
        {
            var fenced = FindBlock<FencedCodeBlock>("```mermaid\nflowchart LR\n    A --> B\n```");

            var text = MarkdownCodeBlockText.GetText(fenced);

            Assert.That(text.TrimEnd('\r', '\n'), Is.EqualTo("flowchart LR\n    A --> B"));
            Assert.That(text, Does.Not.Contain("CodeBlockLine"));
            Assert.That(text, Does.Not.Contain("System.Collections.Generic"));
        }

        [Test]
        public void GetText_CSharpFence_ReturnsCode()
        {
            var fenced = FindBlock<FencedCodeBlock>("```csharp\npublic class A {}\n```");

            var text = MarkdownCodeBlockText.GetText(fenced);

            Assert.That(text.TrimEnd('\r', '\n'), Is.EqualTo("public class A {}"));
            Assert.That(fenced.Info, Is.EqualTo("csharp"));
        }

        [Test]
        public void GetText_MultilineFence_ReturnsAllLines()
        {
            var fenced = FindBlock<FencedCodeBlock>("```\nline1\nline2\nline3\n```");

            var text = MarkdownCodeBlockText.GetText(fenced);

            Assert.That(text.TrimEnd('\r', '\n'), Is.EqualTo("line1\nline2\nline3"));
        }

        [Test]
        public void GetText_IndentedCodeBlock_ReturnsCode()
        {
            var code = FindBlock<CodeBlock>("    int x = 1;\n    int y = 2;");

            var text = MarkdownCodeBlockText.GetText(code);

            Assert.That(text.TrimEnd('\r', '\n'), Is.EqualTo("int x = 1;\nint y = 2;"));
        }

        [Test]
        public void GetText_Null_ReturnsEmpty()
        {
            Assert.That(MarkdownCodeBlockText.GetText(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetText_EmptyCodeBlock_ReturnsEmpty()
        {
            var fenced = FindBlock<FencedCodeBlock>("```\n```");

            var text = MarkdownCodeBlockText.GetText(fenced);

            Assert.That(text.TrimEnd('\r', '\n'), Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetText_DoesNotReturnCodeBlockLinesTypeName()
        {
            var fenced = FindBlock<FencedCodeBlock>("```mermaid\nflowchart LR\n```");

            var text = MarkdownCodeBlockText.GetText(fenced);

            Assert.That(text, Is.Not.EqualTo(fenced.CodeBlockLines.ToString()));
        }

        private static T FindBlock<T>(string markdown) where T : Block
        {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var document = Markdown.Parse(markdown, pipeline);
            foreach (var block in document)
            {
                if (block is T typed)
                    return typed;
            }
            return null;
        }
    }
}
