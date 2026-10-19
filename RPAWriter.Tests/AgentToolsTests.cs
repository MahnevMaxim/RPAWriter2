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
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_spec"));
            Assert.That(result.ToolCalls[0].Arguments.Value<int>("x"), Is.EqualTo(1));
        }

        [Test]
        public void Parse_FencedJson_ReturnsToolCall()
        {
            var result = AgentResponseParser.Parse("```json\n{\"action\":\"tool_call\",\"tool\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}\n```");
            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_file"));
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

        [Test]
        public void Parse_ArrayOfToolCalls_ReturnsMultipleCalls()
        {
            var result = AgentResponseParser.Parse("[{\"action\":\"tool_call\",\"tool\":\"read_spec\",\"arguments\":{}},{\"action\":\"tool_call\",\"tool\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}]");

            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(2));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_spec"));
            Assert.That(result.ToolCalls[1].ToolName, Is.EqualTo("read_file"));
            Assert.That(result.ToolCalls[1].Arguments.Value<string>("path"), Is.EqualTo("a.txt"));
        }

        [Test]
        public void Parse_FencedArrayOfToolCalls_ReturnsMultipleCalls()
        {
            var result = AgentResponseParser.Parse("```json\n[{\"action\":\"tool_call\",\"tool\":\"read_spec\",\"arguments\":{}}]\n```");

            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_spec"));
        }

        [Test]
        public void Parse_DsmlToolCalls_ReturnsToolCalls()
        {
            var content =
                "Agent [Plan]: <｜DSML｜ calls>\n" +
                "<｜DSML｜ invoke name=\"tool_call\">\n" +
                "<｜DSML｜ parameter name=\"tool\" string=\"true\">list_files</｜DSML｜ parameter>\n" +
                "<｜DSML｜ parameter name=\"arguments\" string=\"false\">{\"path\": \".\", \"recursive\": true}</｜DSML｜ parameter>\n" +
                "</｜DSML｜ invoke>\n" +
                "<｜DSML｜ invoke name=\"tool_call\">\n" +
                "<｜DSML｜ parameter name=\"tool\" string=\"true\">git_status</｜DSML｜ parameter>\n" +
                "<｜DSML｜ parameter name=\"arguments\" string=\"false\">{}</｜DSML｜ parameter>\n" +
                "</｜DSML｜ invoke>\n" +
                "</｜DSML｜ calls>";

            var result = AgentResponseParser.Parse(content);

            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(2));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("list_files"));
            Assert.That(result.ToolCalls[0].Arguments.Value<string>("path"), Is.EqualTo("."));
            Assert.That(result.ToolCalls[0].Arguments.Value<bool>("recursive"), Is.True);
            Assert.That(result.ToolCalls[1].ToolName, Is.EqualTo("git_status"));
        }

        [Test]
        public void Parse_DsmlPipeAndLowercaseVariant_ReturnsToolCall()
        {
            var content =
                "<dsml|invoke name=\"tool_call\">\n" +
                "<dsml|parameter name=\"tool\" string=\"true\">read_file</dsml|parameter>\n" +
                "<dsml|parameter name=\"arguments\" string=\"false\">{\"path\":\"a.txt\"}</dsml|parameter>\n" +
                "</dsml|invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_file"));
            Assert.That(result.ToolCalls[0].Arguments.Value<string>("path"), Is.EqualTo("a.txt"));
        }

        [Test]
        public void Parse_PlainXmlInvoke_ReturnsToolCall()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">read_spec</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{}</parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.That(result.IsFinal, Is.False);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("read_spec"));
        }

        [Test]
        public void LooksLikeToolMarkup_Dsml_ReturnsTrue()
        {
            Assert.That(AgentResponseParser.LooksLikeToolMarkup("<｜DSML｜ invoke name=\"tool_call\">"), Is.True);
        }

        [Test]
        public void LooksLikeToolMarkup_Json_ReturnsFalse()
        {
            Assert.That(AgentResponseParser.LooksLikeToolMarkup("{\"action\":\"final\",\"text\":\"ok\"}"), Is.False);
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

        [Test]
        public void ForMode_WithPermissionFilter_ExcludesDisabledCategories()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("read", "read", "{}", mutates: false, (a, c) => Task.FromResult("r")));
            registry.Register(new AgentTool("run", "run", "{}", mutates: true, (a, c) => Task.FromResult("r"), permission: AgentPermissions.ExecuteCommand));

            var readOnly = registry.ForMode(AgentMode.Act, AgentPermissions.ReadFiles);
            Assert.That(readOnly.Count, Is.EqualTo(1));
            Assert.That(readOnly[0].Name, Is.EqualTo("read"));

            var exec = registry.ForMode(AgentMode.Act, AgentPermissions.ExecuteCommand);
            Assert.That(exec.Count, Is.EqualTo(1));
            Assert.That(exec[0].Name, Is.EqualTo("run"));

            var all = registry.ForMode(AgentMode.Act, AgentPermissions.All);
            Assert.That(all.Count, Is.EqualTo(2));
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
            Assert.That(sent[3]["role"].Value<string>(), Is.EqualTo("user"));
        }

        [Test]
        public async Task ChatAsync_ToolMessageWithoutToolCallId_IsDowngradedToUser()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("ok") };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "hi" },
                new ChatMessageData { Role = "assistant", Content = "call" },
                new ChatMessageData { Role = "tool", Content = "result" }
            };

            await provider.ChatAsync(messages, "SYS");

            var body = JObject.Parse(handler.LastBody);
            var sent = (JArray)body["messages"];
            Assert.That(sent[3]["role"].Value<string>(), Is.EqualTo("user"));
            Assert.That(sent[3]["tool_call_id"], Is.Null);
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
        public async Task RunAsync_MultipleToolCallsInOneTurn_ExecutesAll()
        {
            var calls = new List<string>();
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("tool_a", "a", "{}", mutates: false, (a, c) => { calls.Add("a"); return Task.FromResult("ra"); }));
            registry.Register(new AgentTool("tool_b", "b", "{}", mutates: false, (a, c) => { calls.Add("b"); return Task.FromResult("rb"); }));

            var responses = new Queue<string>();
            responses.Enqueue(Completion("[{\"action\":\"tool_call\",\"tool\":\"tool_a\",\"arguments\":{}},{\"action\":\"tool_call\",\"tool\":\"tool_b\",\"arguments\":{}}]"));
            responses.Enqueue(Completion("{\"action\":\"final\",\"text\":\"done\"}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(calls, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(result.FinalText, Is.EqualTo("done"));
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

        [Test]
        public async Task RunAsync_ToolWithDisabledPermission_BlocksIt()
        {
            var executed = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("run_command", "run", "{}", mutates: true, (a, c) =>
            {
                executed = true;
                return Task.FromResult("should-not-run");
            }, permission: AgentPermissions.ExecuteCommand));

            var responses = new Queue<string>();
            responses.Enqueue(Completion("{\"action\":\"tool_call\",\"tool\":\"run_command\",\"arguments\":{}}"));
            responses.Enqueue(Completion("{\"action\":\"final\",\"text\":\"done\"}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4, allowedPermissions: AgentPermissions.ReadFiles | AgentPermissions.EditFiles);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(executed, Is.False);
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task RunAsync_CompletesRunTool_ReturnsItsResultAsFinal()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("submit_and_exit", "submit", "{}", mutates: false,
                (a, c) => Task.FromResult("The summary result"),
                completesRun: true));

            var responses = new Queue<string>();
            responses.Enqueue(Completion("{\"action\":\"tool_call\",\"tool\":\"submit_and_exit\",\"arguments\":{\"summary\":\"The summary result\",\"verified\":true}}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4);
            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(result.FinalText, Is.EqualTo("The summary result"));
            Assert.That(responses.Count, Is.Zero, "the run should stop after a completesRun tool without another model call");
        }

        [Test]
        public async Task RunAsync_DsmlGarbage_RecoversThenReturnsFinal()
        {
            var registry = new AgentToolRegistry();

            var responses = new Queue<string>();
            responses.Enqueue(Completion("<｜DSML｜ invoke name=\"tool_call\">\n</｜DSML｜ invoke>"));
            responses.Enqueue(Completion("{\"action\":\"final\",\"text\":\"готово\"}"));

            var handler = new FakeHttpHandler { Responder = req => responses.Dequeue() };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));

            var runner = new AgentRunner(provider, registry, mode => "SYSTEM", maxIterations: 4);
            var result = await runner.RunAsync("go", AgentMode.Plan, CancellationToken.None);

            Assert.That(result.FinalText, Is.EqualTo("готово"));
        }
    }

    [TestFixture]
    public class AgentPromptBuilderTests
    {
        private static AgentToolRegistry EmptyRegistry() => new AgentToolRegistry();

        [Test]
        public void Build_WithNameAndAddressByName_AppendsAddressing()
        {
            var prompt = AgentPromptBuilder.Build(AgentMode.Act, EmptyRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All, "Иван", true);

            Assert.That(prompt, Does.Contain("Address the user by their name when replying: \"Иван\"."));
        }

        [Test]
        public void Build_AddressByNameFalse_DoesNotAppendAddressing()
        {
            var prompt = AgentPromptBuilder.Build(AgentMode.Act, EmptyRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All, "Иван", false);

            Assert.That(prompt, Does.Not.Contain("Address the user by their name"));
        }

        [Test]
        public void Build_BlankName_DoesNotAppendAddressing()
        {
            var prompt = AgentPromptBuilder.Build(AgentMode.Act, EmptyRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All, "   ", true);

            Assert.That(prompt, Does.Not.Contain("Address the user by their name"));
        }

        [Test]
        public void Build_ExistingOverload_StillWorks()
        {
            var prompt = AgentPromptBuilder.Build(AgentMode.Act, EmptyRegistry());

            Assert.That(prompt, Does.Contain("Available tools"));
        }
    }
}
