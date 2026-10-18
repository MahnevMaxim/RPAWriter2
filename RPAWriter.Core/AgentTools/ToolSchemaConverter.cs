using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.AgentTools
{
    /// <summary>
    /// Converts the informal tool parameter schema (e.g. <c>{"path":"string"}</c>) into an
    /// OpenAI JSON Schema object for native function calling. Kept in one place so the
    /// existing <see cref="IAgentTool.ParametersSchema"/> strings do not need to change.
    /// </summary>
    public static class ToolSchemaConverter
    {
        /// <summary>Builds the OpenAI function-calling schema for a tool from its informal schema string.</summary>
        public static JObject ToOpenAiSchema(string informalSchema)
        {
            if (string.IsNullOrWhiteSpace(informalSchema))
                return EmptyObjectSchema();

            JObject obj;
            try
            {
                obj = JObject.Parse(informalSchema);
            }
            catch
            {
                return EmptyObjectSchema();
            }

            return ObjectSchema(obj);
        }

        /// <summary>Builds a <see cref="ToolDefinition"/> from an <see cref="IAgentTool"/>.</summary>
        public static ToolDefinition ToToolDefinition(IAgentTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            return new ToolDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = ToOpenAiSchema(tool.ParametersSchema)
            };
        }

        /// <summary>Builds the tool list for a mode from the registry.</summary>
        public static IReadOnlyList<ToolDefinition> BuildToolDefinitions(AgentToolRegistry registry, AgentMode mode)
        {
            return BuildToolDefinitions(registry, mode, AgentPermissions.All);
        }

        /// <summary>Builds the tool list for a mode and allowed permission set.</summary>
        public static IReadOnlyList<ToolDefinition> BuildToolDefinitions(AgentToolRegistry registry, AgentMode mode, AgentPermissions allowed)
        {
            return registry.ForMode(mode, allowed).Select(ToToolDefinition).ToList();
        }

        private static JObject EmptyObjectSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            };
        }

        private static JObject ObjectSchema(JObject informal)
        {
            var properties = new JObject();
            var required = new JArray();

            foreach (var prop in informal.Properties())
            {
                properties[prop.Name] = ValueSchema(prop.Value);
                required.Add(prop.Name);
            }

            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (required.Count > 0)
                schema["required"] = required;
            return schema;
        }

        private static JObject ValueSchema(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return new JObject();

            if (value.Type == JTokenType.Object)
                return ObjectSchema((JObject)value);

            if (value.Type == JTokenType.Array)
            {
                var array = (JArray)value;
                var items = array.Count > 0 ? ValueSchema(array[0]) : new JObject();
                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = items
                };
            }

            if (value.Type == JTokenType.Boolean)
                return new JObject { ["type"] = "boolean" };

            if (value.Type == JTokenType.Integer)
                return new JObject { ["type"] = "integer" };

            if (value.Type == JTokenType.Float)
                return new JObject { ["type"] = "number" };

            // String: map the well-known informal type keywords, support "a|b" enums,
            // and fall back to "string" for unknown values (e.g. "override|continue").
            var text = value.Value<string>() ?? "";
            switch (text.Trim().ToLowerInvariant())
            {
                case "string":
                case "str":
                case "text":
                    return new JObject { ["type"] = "string" };
                case "number":
                case "float":
                case "double":
                    return new JObject { ["type"] = "number" };
                case "integer":
                case "int":
                    return new JObject { ["type"] = "integer" };
                case "boolean":
                case "bool":
                    return new JObject { ["type"] = "boolean" };
                case "object":
                    return new JObject { ["type"] = "object" };
                default:
                    if (text.IndexOf('|') >= 0)
                    {
                        var enumValues = new JArray(text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0));
                        return new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = enumValues
                        };
                    }
                    return new JObject { ["type"] = "string" };
            }
        }
    }
}
