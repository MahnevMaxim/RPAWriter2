using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SubAgentRunnerTests
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

        private static OpenAiCompatibleProvider Provider(FakeHttpHandler handler)
        {
            return new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test" }, new HttpClient(handler));
        }

        [Test]
        public async Task RunAsync_ReturnsSubAgentFinalText()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("{\"action\":\"final\",\"text\":\"the report\"}") };
            var provider = Provider(handler);
            var registry = new AgentToolRegistry();

            var text = await SubAgentRunner.RunAsync(
                provider, registry, AgentMode.Plan, ToolCallingMode.JsonChat, AgentPermissions.All,
                new ContextBudget { MaxIterations = 4 },
                m => "BASE",
                "Investigate auth",
                "Find how login works",
                CancellationToken.None);

            Assert.That(text, Is.EqualTo("the report"));
        }

        [Test]
        public async Task RunAsync_ExtendsSystemPromptWithInstructions()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("{\"action\":\"final\",\"text\":\"done\"}") };
            var provider = Provider(handler);
            var registry = new AgentToolRegistry();

            await SubAgentRunner.RunAsync(
                provider, registry, AgentMode.Plan, ToolCallingMode.JsonChat, AgentPermissions.All,
                new ContextBudget { MaxIterations = 4 },
                m => "BASE",
                "BE CONCISE",
                "task",
                CancellationToken.None);

            var body = JObject.Parse(handler.LastBody);
            var system = body["messages"][0]["content"].Value<string>();
            Assert.That(system, Does.Contain("BASE"));
            Assert.That(system, Does.Contain("BE CONCISE"));

            var user = body["messages"][1];
            Assert.That(user["role"].Value<string>(), Is.EqualTo("user"));
            Assert.That(user["content"].Value<string>(), Is.EqualTo("task"));
        }

        [Test]
        public void SubagentsPermission_IsPartOfAll_AndGatesRegistryFilter()
        {
            Assert.That((AgentPermissions.All & AgentPermissions.Subagents) != 0, Is.True);

            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("spawn_agent", "spawn", "{}", mutates: false,
                (a, c) => Task.FromResult("ok"), permission: AgentPermissions.Subagents));

            Assert.That(registry.ForMode(AgentMode.Act, AgentPermissions.ReadFiles).Count, Is.EqualTo(0));
            Assert.That(registry.ForMode(AgentMode.Act, AgentPermissions.Subagents).Count, Is.EqualTo(1));
        }
    }
}
