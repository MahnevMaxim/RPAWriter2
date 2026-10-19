using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ToolSchemaConverterTests
    {
        [Test]
        public void ToOpenAiSchema_SimpleTypes_ProducesJsonSchema()
        {
            var schema = ToolSchemaConverter.ToOpenAiSchema("{\"path\":\"string\",\"count\":\"integer\"}");

            Assert.That(schema["type"].Value<string>(), Is.EqualTo("object"));
            Assert.That(schema["properties"]["path"]["type"].Value<string>(), Is.EqualTo("string"));
            Assert.That(schema["properties"]["count"]["type"].Value<string>(), Is.EqualTo("integer"));
            Assert.That(schema["required"].Values<string>().Contains("path"), Is.True);
            Assert.That(schema["required"].Values<string>().Contains("count"), Is.True);
        }

        [Test]
        public void ToOpenAiSchema_Empty_ProducesEmptyObjectSchema()
        {
            var schema = ToolSchemaConverter.ToOpenAiSchema("{}");

            Assert.That(schema["type"].Value<string>(), Is.EqualTo("object"));
            Assert.That(schema["properties"], Is.Not.Null);
        }

        [Test]
        public void ToOpenAiSchema_EnumLike_ProducesStringEnum()
        {
            var schema = ToolSchemaConverter.ToOpenAiSchema("{\"mode\":\"override|continue\"}");

            Assert.That(schema["properties"]["mode"]["type"].Value<string>(), Is.EqualTo("string"));
            Assert.That(schema["properties"]["mode"]["enum"].Values<string>(),
                Is.EquivalentTo(new[] { "override", "continue" }));
        }

        [Test]
        public void BuildToolDefinitions_FiltersByMode()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("read", "r", "{\"path\":\"string\"}", false, (a, c) => Task.FromResult("r")));
            registry.Register(new AgentTool("write", "w", "{}", true, (a, c) => Task.FromResult("w")));

            var plan = ToolSchemaConverter.BuildToolDefinitions(registry, AgentMode.Plan);
            var act = ToolSchemaConverter.BuildToolDefinitions(registry, AgentMode.Act);

            Assert.That(plan.Count, Is.EqualTo(1));
            Assert.That(plan[0].Name, Is.EqualTo("read"));
            Assert.That(act.Count, Is.EqualTo(2));
        }
    }

    [TestFixture]
    public class NativeToolCallingTests
    {
        private static string OkResponse() =>
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";

        private static string NativeToolCallsResponse()
        {
            var payload = new Dictionary<string, object>
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object>
                        {
                            ["role"] = "assistant",
                            ["content"] = null,
                            ["tool_calls"] = new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["id"] = "call_1",
                                    ["type"] = "function",
                                    ["function"] = new Dictionary<string, object>
                                    {
                                        ["name"] = "read_file",
                                        ["arguments"] = "{\"path\":\"a.txt\"}"
                                    }
                                }
                            }
                        },
                        ["finish_reason"] = "tool_calls"
                    }
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task NativeMode_RequestIncludesToolsAndKeepsToolRole()
        {
            var handler = new FakeHttpHandler { Responder = req => OkResponse() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "m",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var tools = new List<ToolDefinition>
            {
                new ToolDefinition { Name = "read_file", Description = "read a file", Parameters = new JObject { ["type"] = "object", ["properties"] = new JObject() } }
            };
            await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "tool", Content = "result", ToolCallId = "call_1" } },
                "SYS",
                tools);

            var body = JObject.Parse(handler.LastBody);
            Assert.That(body["tools"], Is.Not.Null);
            Assert.That(body["tools"][0]["function"]["name"].Value<string>(), Is.EqualTo("read_file"));
            Assert.That(body["tool_choice"].Value<string>(), Is.EqualTo("auto"));

            var messages = (JArray)body["messages"];
            Assert.That(messages[1]["role"].Value<string>(), Is.EqualTo("tool"));
            Assert.That(messages[1]["tool_call_id"].Value<string>(), Is.EqualTo("call_1"));
        }

        [Test]
        public async Task JsonChatMode_NoToolsAndDowngradesToolRole()
        {
            var handler = new FakeHttpHandler { Responder = req => OkResponse() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "m",
                ToolCallingMode = ToolCallingMode.JsonChat
            }, new HttpClient(handler));

            await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "tool", Content = "result", ToolCallId = "call_1" } },
                "SYS");

            var body = JObject.Parse(handler.LastBody);
            Assert.That(body["tools"], Is.Null);
            var messages = (JArray)body["messages"];
            Assert.That(messages[1]["role"].Value<string>(), Is.EqualTo("user"));
            Assert.That(messages[1]["tool_call_id"], Is.Null);
        }

        [Test]
        public async Task NativeMode_ParsesToolCallsFromResponse()
        {
            var handler = new FakeHttpHandler { Responder = req => NativeToolCallsResponse() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "m",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var response = await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "read a.txt" } },
                "SYS",
                new List<ToolDefinition> { new ToolDefinition { Name = "read_file", Description = "read", Parameters = new JObject() } });

            Assert.That(response.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(response.ToolCalls[0].Id, Is.EqualTo("call_1"));
            Assert.That(response.ToolCalls[0].Name, Is.EqualTo("read_file"));
            Assert.That(response.ToolCalls[0].Arguments["path"].Value<string>(), Is.EqualTo("a.txt"));
        }

        [Test]
        public async Task NativeMode_StreamingAccumulatesToolCallArguments()
        {
            string Chunk(object delta) =>
                "data: " + System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta } } }) + "\n\n";

            var chunk1 = Chunk(new
            {
                tool_calls = new[] { new { index = 0, id = "call_1", function = new { name = "read_file", arguments = "" } } }
            });
            var chunk2 = Chunk(new
            {
                tool_calls = new[] { new { index = 0, function = new { arguments = "{\"path\":\"a.txt\"}" } } }
            });
            var sse = chunk1 + chunk2 + "data: [DONE]\n\n";

            var handler = new FakeHttpHandler { Responder = req => sse };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                Model = "m",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var response = await provider.ChatStreamingAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "SYS",
                new List<ToolDefinition>(),
                new SyncProgress<ChatStreamDelta>());

            Assert.That(response.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(response.ToolCalls[0].Name, Is.EqualTo("read_file"));
            Assert.That(response.ToolCalls[0].Arguments["path"].Value<string>(), Is.EqualTo("a.txt"));
        }
    }

    [TestFixture]
    public class AgentRunnerNativeTests
    {
        private static string ToolCallsResponse(string tool, string id, string args)
        {
            var payload = new Dictionary<string, object>
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object>
                        {
                            ["role"] = "assistant",
                            ["content"] = null,
                            ["tool_calls"] = new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["id"] = id,
                                    ["type"] = "function",
                                    ["function"] = new Dictionary<string, object>
                                    {
                                        ["name"] = tool,
                                        ["arguments"] = args
                                    }
                                }
                            }
                        },
                        ["finish_reason"] = "tool_calls"
                    }
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        private static string FinalResponse(string content)
        {
            var payload = new Dictionary<string, object>
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object> { ["role"] = "assistant", ["content"] = content },
                        ["finish_reason"] = "stop"
                    }
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task NativeMode_ExecutesToolCallThenReturnsFinal()
        {
            var called = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{\"x\":\"string\"}", false, (a, c) =>
            {
                called = true;
                return Task.FromResult("tool-result");
            }));

            var responses = new Queue<string>();
            responses.Enqueue(ToolCallsResponse("my_tool", "call_1", "{\"x\":\"v\"}"));
            responses.Enqueue(FinalResponse("готово"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4, toolCallingMode: ToolCallingMode.Native);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(called, Is.True);
            Assert.That(result.FinalText, Is.EqualTo("готово"));
        }

        [Test]
        public async Task NativeMode_CompletesRunTool_ReturnsItsResultAsFinal()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("submit_and_exit", "submit", "{}", mutates: false,
                (a, c) => Task.FromResult("The summary result"),
                completesRun: true));

            var responses = new Queue<string>();
            responses.Enqueue(ToolCallsResponse("submit_and_exit", "call_1", "{\"summary\":\"The summary result\",\"verified\":true}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4, toolCallingMode: ToolCallingMode.Native);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(result.FinalText, Is.EqualTo("The summary result"));
            Assert.That(responses.Count, Is.Zero, "the run should stop after a completesRun tool without another model call");
        }

        [Test]
        public async Task NativeMode_MutatingToolInPlan_BlocksIt()
        {
            var executed = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("write_file", "write", "{}", true, (a, c) =>
            {
                executed = true;
                return Task.FromResult("should-not-run");
            }));

            var responses = new Queue<string>();
            responses.Enqueue(ToolCallsResponse("write_file", "call_1", "{}"));
            responses.Enqueue(FinalResponse("done"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                ToolCallingMode = ToolCallingMode.Native
            }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4, toolCallingMode: ToolCallingMode.Native);
            var result = await runner.RunAsync("go", AgentMode.Plan, CancellationToken.None);

            Assert.That(executed, Is.False);
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }
    }
}
