using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.Mcp;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class McpSchemaConverterTests
    {
        [Test]
        public void ToInformal_Empty_ReturnsEmptyObject()
        {
            Assert.That(McpSchemaConverter.ToInformal(null), Is.EqualTo("{}"));
            Assert.That(McpSchemaConverter.ToInformal(new JObject()), Is.EqualTo("{}"));
        }

        [Test]
        public void ToInformal_PrimitiveTypes_AndOptional()
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["url"] = new JObject { ["type"] = "string" },
                    ["width"] = new JObject { ["type"] = "integer" },
                    ["ratio"] = new JObject { ["type"] = "number" },
                    ["fullPage"] = new JObject { ["type"] = "boolean" }
                },
                ["required"] = new JArray("url")
            };

            var informal = JObject.Parse(McpSchemaConverter.ToInformal(schema));

            Assert.That(informal["url"].Value<string>(), Is.EqualTo("string"));
            Assert.That(informal["width?"].Value<string>(), Is.EqualTo("integer"));
            Assert.That(informal["ratio?"].Value<string>(), Is.EqualTo("number"));
            Assert.That(informal["fullPage?"].Value<string>(), Is.EqualTo("boolean"));
        }

        [Test]
        public void ToInformal_Enum_CollapsesToPipeString()
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("headless", "headed") }
                },
                ["required"] = new JArray("mode")
            };

            var informal = JObject.Parse(McpSchemaConverter.ToInformal(schema));
            Assert.That(informal["mode"].Value<string>(), Is.EqualTo("headless|headed"));
        }

        [Test]
        public void ToInformal_Array_ProducesArraySchema()
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["urls"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JArray("urls")
            };

            var informal = JObject.Parse(McpSchemaConverter.ToInformal(schema));
            Assert.That(informal["urls"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(informal["urls"][0].Value<string>(), Is.EqualTo("string"));
        }

        [Test]
        public void ToInformal_NestedObject_Preserved()
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["options"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { ["timeout"] = new JObject { ["type"] = "integer" } }
                    }
                },
                ["required"] = new JArray("options")
            };

            var informal = JObject.Parse(McpSchemaConverter.ToInformal(schema));
            Assert.That(informal["options"]["timeout"].Value<string>(), Is.EqualTo("integer"));
        }
    }
}
