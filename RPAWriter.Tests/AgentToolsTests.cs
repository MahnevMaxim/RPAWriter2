using System;
using System.Collections.Generic;
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
    public class AgentResponseParserTests
    {
        [Test]
        public void Parse_FinalJson_ReturnsFinal()
        {
            var result = AgentResponseParser.Parse("{\"action\":\"final\",\"text\":\"Готово\"}");
            Assert.That(result.IsFinal, Is.True);
            Assert.That(result.Text, Is.EqualTo("Готово"));
        }

        [Test]
        public void Parse_ToolCallJson_ReturnsToolCall()
        {
            var result = AgentResponseParser.Parse("{\"action\":\"tool_call\",\"tool\":\"read_spec\",\"arguments\":{\"x\":1}}");
            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolName, Is.EqualTo("read_spec"));
            Assert.That(result.Arguments.Value<int>("x"), Is.EqualTo(1));
        }

        [Test]
        public void Parse_FencedJson_ReturnsToolCall()
        {
            var result = AgentResponseParser.Parse("```json\n{\"action\":\"tool_call\",\"tool\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}\n```");
            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolName, Is.EqualTo("read_file"));
        }

        [Test]
        public void Parse_PlainText_FallsBackToFinal()
        {
            var result = AgentResponseParser.Parse("Просто ответ без JSON.");
            Assert.That(result.IsFinal, Is.True);
            Assert.That(result.Text, Is.EqualTo("Просто ответ без JSON."));
        }

        [Test]
        public void Parse_FinalWithCodeFencesInsideText_ExtractsHumanReadableText()
        {
            var content = "{\"action\":\"final\",\"text\":\"## Fix\\n\\n```csharp\\nstatic void Main(string[] args)\\n```\\n\\nInvalid C#.\"}";
            var result = AgentResponseParser.Parse(content);

            Assert.That(result.IsFinal, Is.True);
            Assert.That(result.Text, Does.Contain("## Fix"));
            Assert.That(result.Text, Does.Contain("```csharp"));
            Assert.That(result.Text, Does.Contain("static void Main"));
            Assert.That(result.Text, Does.Not.Contain("\"action\":\"final\""));
        }

        [Test]
        public void Parse_FinalWrappedInCodeFence_ExtractsText()
        {
            var result = AgentResponseParser.Parse("```json\n{\"action\":\"final\",\"text\":\"ok\"}\n```");

            Assert.That(result.IsFinal, Is.True);
            Assert.That(result.Text, Is.EqualTo("ok"));
        }

        [Test]
        public void Parse_EmptyOrWhitespaceContent_ReturnsFinalEmpty()
        {
            var result = AgentResponseParser.Parse("   ");

            Assert.That(result.IsFinal, Is.True);
            Assert.That(result.Text, Is.EqualTo(""));
        }
    }

    [TestFixture]
    public class AgentToolRegistryTests
    {
        [Test]
        public void ForMode_Plan_ExposesOnlyReadOnlyTools()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("read", "read tool", "{}", mutates: false, (a, c) => Task.FromResult("r")));
            registry.Register(new AgentTool("write", "write tool", "{}", mutates: true, (a, c) => Task.FromResult("w")));

            var tools = registry.ForMode(AgentMode.Plan);
            Assert.That(tools.Count, Is.EqualTo(1));
            Assert.That(tools[0].Name, Is.EqualTo("read"));
        }

        [Test]
        public void ForMode_Act_ExposesAllTools()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("read", "read tool", "{}", mutates: false, (a, c) => Task.FromResult("r")));
            registry.Register(new AgentTool("write", "write tool", "{}", mutates: true, (a, c) => Task.FromResult("w")));

            var tools = registry.ForMode(AgentMode.Act);
            Assert.That(tools.Count, Is.EqualTo(2));
        }

        [Test]
        public void Find_IsCaseInsensitive()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("ReadFile", "read", "{}", mutates: false, (a, c) => Task.FromResult("r")));

            Assert.That(registry.Find("readfile"), Is.Not.Null);
        }
    }

    [TestFixture]
    public class ChatHistoryTests
    {
        private static string Completion(string content)
        {
            var payload = new Dictionary<string, object>
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object> { ["content"] = content }
                    }
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task ChatAsync_WithHistory_SendsSystemAndAllMessages()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("ok") };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "hi" },
                new ChatMessageData { Role = "assistant", Content = "hello" },
                new ChatMessageData { Role = "tool", Content = "result" }
            };

            await provider.ChatAsync(messages, "SYSPROMPT");

            var body = JObject.Parse(handler.LastBody);
            var sent = body["messages"] as JArray;
            Assert.That(sent, Is.Not.Null);
            Assert.That(sent.Count, Is.EqualTo(4));

            Assert.That(sent[0]["role"].Value<string>(), Is.EqualTo("system"));
            Assert.That(sent[0]["content"].Value<string>(), Is.EqualTo("SYSPROMPT"));
            Assert.That(sent[1]["role"].Value<string>(), Is.EqualTo("user"));
            Assert.That(sent[2]["role"].Value<string>(), Is.EqualTo("assistant"));
            Assert.That(sent[3]["role"].Value<string>(), Is.EqualTo("tool"));
        }
    }

    [TestFixture]
    public class AgentRunnerTests
    {
        private static string Completion(string content)
        {
            var payload = new Dictionary<string, object>
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object> { ["content"] = content }
                    }
                }
            };
            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task RunAsync_ToolCallThenFinal_ExecutesToolAndReturnsFinal()
        {
            var called = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false, (a, c) =>
            {
                called = true;
                return Task.FromResult("tool-result");
            }));

            var responses = new Queue<string>();
            responses.Enqueue(Completion("{\"action\":\"tool_call\",\"tool\":\"my_tool\",\"arguments\":{}}"));
            responses.Enqueue(Completion("{\"action\":\"final\",\"text\":\"готово\"}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(called, Is.True);
            Assert.That(result.FinalText, Is.EqualTo("готово"));
        }

        [Test]
        public async Task RunAsync_MutatingToolInPlan_BlocksIt()
        {
            var executed = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("write_file", "write", "{}", mutates: true, (a, c) =>
            {
                executed = true;
                return Task.FromResult("should-not-run");
            }));

            var responses = new Queue<string>();
            responses.Enqueue(Completion("{\"action\":\"tool_call\",\"tool\":\"write_file\",\"arguments\":{}}"));
            responses.Enqueue(Completion("{\"action\":\"final\",\"text\":\"done\"}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4);
            var result = await runner.RunAsync("go", AgentMode.Plan, CancellationToken.None);

            Assert.That(executed, Is.False);
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }
    }
}
