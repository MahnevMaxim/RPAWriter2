using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Mcp;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpToolResultParserTests
    {
        [Test]
        public void ToText_ConcatenatesTextBlocks()
        {
            var result = new McpCallToolResult
            {
                IsError = false,
                Content = new List<McpContentBlock>
                {
                    new McpContentBlock { Type = "text", Text = "first" },
                    new McpContentBlock { Type = "text", Text = "second" }
                }
            };

            var text = McpToolResultParser.ToText(result);

            Assert.That(text, Is.EqualTo("first\nsecond"));
        }

        [Test]
        public void ToText_ImageBlock_ProducesPlaceholder()
        {
            var result = new McpCallToolResult
            {
                Content = new List<McpContentBlock>
                {
                    new McpContentBlock { Type = "image", MimeType = "image/png", Data = "aGVsbG8=" }
                }
            };

            var text = McpToolResultParser.ToText(result);

            Assert.That(text, Does.Contain("[image: image/png"));
            Assert.That(text, Does.Contain("8 chars"));
        }

        [Test]
        public void ToText_ErrorFlag_Prefixes()
        {
            var result = new McpCallToolResult
            {
                IsError = true,
                Content = new List<McpContentBlock> { new McpContentBlock { Type = "text", Text = "bad" } }
            };

            Assert.That(McpToolResultParser.ToText(result), Is.EqualTo("[MCP error] bad"));
        }

        [Test]
        public void ToText_EmptyContent_FallsBack()
        {
            var result = new McpCallToolResult { IsError = false, Content = new List<McpContentBlock>() };
            Assert.That(McpToolResultParser.ToText(result), Is.EqualTo("(no content)"));
        }
    }
}
