using System.Linq;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class CodeHighlightingTests
    {
        [Test]
        public void Highlight_KeywordsAreClassified()
        {
            var tokens = CSharpHighlighter.Highlight("public static void Main()");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Keyword && t.Text == "public"), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Keyword && t.Text == "static"), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Keyword && t.Text == "void"), Is.True);
        }

        [Test]
        public void Highlight_StringIsClassified()
        {
            var tokens = CSharpHighlighter.Highlight("var s = \"hello\";");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.String && t.Text == "\"hello\""), Is.True);
        }

        [Test]
        public void Highlight_VerbatimAndInterpolatedStringsAreClassified()
        {
            var tokens = CSharpHighlighter.Highlight("@\"a\"\"b\" $\"x\" $@\"y\"");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.String && t.Text == "@\"a\"\"b\""), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.String && t.Text == "$\"x\""), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.String && t.Text == "$@\"y\""), Is.True);
        }

        [Test]
        public void Highlight_LineCommentIsClassified()
        {
            var tokens = CSharpHighlighter.Highlight("// note\nint x;");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Comment && t.Text == "// note"), Is.True);
        }

        [Test]
        public void Highlight_BlockCommentIsClassified()
        {
            var tokens = CSharpHighlighter.Highlight("/* multi\nline */");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Comment && t.Text == "/* multi\nline */"), Is.True);
        }

        [Test]
        public void Highlight_NumbersAreClassified()
        {
            var tokens = CSharpHighlighter.Highlight("int a = 42; double b = 1.5e3; int c = 0x1F;");

            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Number && t.Text == "42"), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Number && t.Text == "1.5e3"), Is.True);
            Assert.That(tokens.Any(t => t.Kind == CodeTokenKind.Number && t.Text == "0x1F"), Is.True);
        }

        [Test]
        public void Highlight_TextIsPreservedInOrder()
        {
            const string code = "public static class R { // hi\n string s = \"x\"; int n = 5; }";

            var tokens = CSharpHighlighter.Highlight(code);

            Assert.That(string.Concat(tokens.Select(t => t.Text)), Is.EqualTo(code));
        }

        [Test]
        public void SplitBlocks_NoFence_ReturnsSingleTextBlock()
        {
            var blocks = ChatMarkdown.SplitBlocks("Hello world");

            Assert.That(blocks.Count, Is.EqualTo(1));
            Assert.That(blocks[0], Is.InstanceOf<TextMessageBlock>());
            Assert.That(((TextMessageBlock)blocks[0]).Text, Is.EqualTo("Hello world"));
        }

        [Test]
        public void SplitBlocks_CSharpFence_ProducesCodeBlockWithHighlight()
        {
            var blocks = ChatMarkdown.SplitBlocks("before\n```csharp\npublic class A {}\n```\nafter");

            Assert.That(blocks.Count, Is.EqualTo(3));
            Assert.That(blocks[0], Is.InstanceOf<TextMessageBlock>());
            Assert.That(blocks[1], Is.InstanceOf<CodeMessageBlock>());
            Assert.That(blocks[2], Is.InstanceOf<TextMessageBlock>());

            var code = (CodeMessageBlock)blocks[1];
            Assert.That(code.Language, Is.EqualTo("csharp"));
            Assert.That(code.Code, Is.EqualTo("public class A {}"));
            Assert.That(code.Tokens.Any(t => t.Kind == CodeTokenKind.Keyword), Is.True);
        }

        [Test]
        public void SplitBlocks_ShortLanguageAliases_AreRecognized()
        {
            Assert.That(((CodeMessageBlock)ChatMarkdown.SplitBlocks("```cs\nx = 1;\n```")[0]).Language, Is.EqualTo("cs"));
            Assert.That(((CodeMessageBlock)ChatMarkdown.SplitBlocks("```c#\nx = 1;\n```")[0]).Language, Is.EqualTo("c#"));
        }

        [Test]
        public void SplitBlocks_UnknownLanguage_IsCodeBlockWithoutHighlight()
        {
            var blocks = ChatMarkdown.SplitBlocks("```xml\n<a/>\n```");

            Assert.That(blocks.Count, Is.EqualTo(1));
            var code = (CodeMessageBlock)blocks[0];
            Assert.That(code.Tokens.Count, Is.EqualTo(1));
            Assert.That(code.Tokens[0].Kind, Is.EqualTo(CodeTokenKind.Plain));
        }

        [Test]
        public void SplitBlocks_UnterminatedFence_TreatsRestAsCode()
        {
            var blocks = ChatMarkdown.SplitBlocks("```csharp\nint x = 1;");

            Assert.That(blocks.Count, Is.EqualTo(1));
            Assert.That(blocks[0], Is.InstanceOf<CodeMessageBlock>());
            Assert.That(((CodeMessageBlock)blocks[0]).Code, Is.EqualTo("int x = 1;"));
        }
    }
}
