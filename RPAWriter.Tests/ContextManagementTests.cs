using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests
{
    /// <summary>Minimal AI provider double for exercising the context-management loop.</summary>
    internal sealed class FakeAiProvider : IAiProvider
    {
        private readonly Queue<object> _responses; // ChatResponse or Exception
        public int CallCount { get; private set; }
        public List<IReadOnlyList<ChatMessageData>> Calls { get; } = new List<IReadOnlyList<ChatMessageData>>();

        public FakeAiProvider(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Calls.Add(messages);
            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : (object)new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" };

            if (next is Exception ex)
                throw ex;

            return Task.FromResult((ChatResponse)next);
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default) =>
            ChatAsync(messages, systemPrompt, cancellationToken);

        public Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SpecModel> ReconstructSpecAsync(string robotName, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf) =>
            throw new NotImplementedException();
        public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, IReadOnlyList<RobotSourceFile> robotFiles, CancellationToken cancellationToken = default, RobotPlatform platform = RobotPlatform.Mwf) =>
            throw new NotImplementedException();
    }

    [TestFixture]
    public class TokenEstimatorTests
    {
        [Test]
        public void Estimate_Empty_ReturnsZero()
        {
            Assert.That(new TokenEstimator().Estimate("", new List<ChatMessageData>()), Is.EqualTo(0));
        }

        [Test]
        public void Estimate_IncludesSystemPromptAndMessages()
        {
            var estimator = new TokenEstimator();
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "12345678" }
            };
            var estimate = estimator.Estimate("1234", messages);

            Assert.That(estimate, Is.GreaterThanOrEqualTo(1 + 1 + 2)); // system(4/4=1) + overhead(4) + content(8/4=2)
        }

        [Test]
        public void Estimate_GrowsWithContent()
        {
            var estimator = new TokenEstimator();
            var small = estimator.Estimate("", new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "a" } });
            var large = estimator.Estimate("", new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = new string('a', 100) } });
            Assert.That(large, Is.GreaterThan(small));
        }
    }

    [TestFixture]
    public class ContextCondenserTests
    {
        private static List<ChatMessageData> Transcript(int count)
        {
            var list = new List<ChatMessageData>();
            for (var i = 0; i < count; i++)
                list.Add(new ChatMessageData { Role = "user", Content = "message " + i });
            return list;
        }

        [Test]
        public async Task Condense_SummarizesOlderMessages_AndKeepsRecent()
        {
            var ai = new FakeAiProvider(new ChatResponse { Answer = "SUMMARIZED", FinishReason = "stop" });
            var condenser = new ContextCondenser(ai, new TokenEstimator(), "COMPACT");
            var budget = new ContextBudget { KeepRecentMessages = 2 };

            var result = await condenser.CondenseAsync(Transcript(10), budget, CancellationToken.None);

            Assert.That(result.Count, Is.EqualTo(3)); // summary + 2 kept
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[0].Content, Does.Contain("SUMMARIZED"));
            Assert.That(result[1].Role, Is.EqualTo("user"));
            Assert.That(result[1].Content, Is.EqualTo("message 8"));
            Assert.That(result[2].Content, Is.EqualTo("message 9"));
        }

        [Test]
        public async Task Condense_IncludesNativeToolCallsInSummaryInput()
        {
            var ai = new FakeAiProvider(new ChatResponse { Answer = "SUMMARIZED", FinishReason = "stop" });
            var condenser = new ContextCondenser(ai, new TokenEstimator(), "COMPACT");
            var budget = new ContextBudget { KeepRecentMessages = 1 };

            var transcript = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = new List<NativeToolCall>
                    {
                        new NativeToolCall { Id = "call_1", Name = "read_file", Arguments = new JObject { ["path"] = "a.txt" } }
                    }
                },
                new ChatMessageData { Role = "tool", Content = "", ToolCallId = "call_1" },
                new ChatMessageData { Role = "user", Content = "recent" }
            };

            var result = await condenser.CondenseAsync(transcript, budget, CancellationToken.None);

            Assert.That(result.Count, Is.EqualTo(2)); // summary + 1 kept
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[0].Content, Does.Contain("SUMMARIZED"));
            Assert.That(ai.Calls.Count, Is.EqualTo(1));
            Assert.That(ai.Calls[0][0].Content, Does.Contain("read_file"));
            Assert.That(ai.Calls[0][0].Content, Does.Contain("call_1"));
        }

        [Test]
        public async Task Condense_WhenSummarizationFails_FallsBackToTruncation()
        {
            var ai = new FakeAiProvider(new InvalidOperationException("boom"));
            var condenser = new ContextCondenser(ai, new TokenEstimator(), "COMPACT");
            var budget = new ContextBudget { KeepRecentMessages = 2, ContextWindowSize = 40, ReserveFraction = 0 };

            var result = await condenser.CondenseAsync(Transcript(10), budget, CancellationToken.None);

            Assert.That(result.Count, Is.LessThanOrEqualTo(10));
            Assert.That(result[result.Count - 1].Content, Is.EqualTo("message 9"));
        }
    }

    [TestFixture]
    public class ContextAssemblerTests
    {
        [Test]
        public async Task Prepare_OverThreshold_Condenses()
        {
            var ai = new FakeAiProvider(new ChatResponse { Answer = "SUMMARY", FinishReason = "stop" });
            var condenser = new ContextCondenser(ai, new TokenEstimator(), "COMPACT");
            var assembler = new ContextAssembler(new TokenEstimator(), condenser);

            var transcript = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = new string('x', 200) }
            };
            var budget = new ContextBudget { ContextWindowSize = 50, ReserveFraction = 0.1, CompactThresholdFraction = 0.5 };

            var prepared = await assembler.PrepareAsync(transcript, "SYS", budget, CancellationToken.None);

            Assert.That(prepared.WasCondensed, Is.True);
        }

        [Test]
        public async Task Prepare_UnderThreshold_DoesNotCondense()
        {
            var condenser = new ContextCondenser(new FakeAiProvider(), new TokenEstimator(), "COMPACT");
            var assembler = new ContextAssembler(new TokenEstimator(), condenser);

            var transcript = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "short" }
            };
            var budget = new ContextBudget { ContextWindowSize = 100000 };

            var prepared = await assembler.PrepareAsync(transcript, "SYS", budget, CancellationToken.None);

            Assert.That(prepared.WasCondensed, Is.False);
        }
    }

    [TestFixture]
    public class AgentRunnerContextTests
    {
        private static AgentToolRegistry Registry()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false, (a, c) => Task.FromResult("tool-result")));
            return registry;
        }

        [Test]
        public async Task RunAsync_ContextOverflow_CondensesAndRetries()
        {
            var ai = new FakeAiProvider(
                new ProviderErrorException(ProviderErrorKind.ContextWindowExceeded, "overflow"),
                new ChatResponse { Answer = "SUMMARY", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"recovered\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, Registry(), mode => "SYS", new ContextBudget { MaxIterations = 4, KeepRecentMessages = 1 }, "COMPACT");
            var compacted = false;
            runner.ContextCompacted += (s, e) => compacted = true;

            var history = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "user", Content = "first" },
                new ChatMessageData { Role = "assistant", Content = "second" },
                new ChatMessageData { Role = "user", Content = "third" }
            };
            var result = await runner.RunAsync(history, "go", AgentMode.Act, new ContextBudget { MaxIterations = 4, KeepRecentMessages = 1 }, CancellationToken.None);

            Assert.That(result.FinalText, Is.EqualTo("recovered"));
            Assert.That(compacted, Is.True);
            Assert.That(ai.CallCount, Is.EqualTo(3));
        }

        [Test]
        public async Task RunAsync_MaxTokensTruncated_NudgesAndRetries()
        {
            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"partial\"}", FinishReason = "length" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"complete\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, Registry(), mode => "SYS", new ContextBudget { MaxIterations = 4 }, "COMPACT");

            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(result.FinalText, Is.EqualTo("complete"));
            Assert.That(ai.CallCount, Is.EqualTo(2));
        }

        [Test]
        public async Task RunAsync_ToolResult_UsesToolRoleByDefault()
        {
            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"my_tool\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, Registry(), mode => "SYS", new ContextBudget { MaxIterations = 4, UseToolRole = true }, "COMPACT");

            await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            var secondCall = ai.Calls[1];
            var hasToolRole = false;
            foreach (var m in secondCall)
                if (m.Role == "tool" && m.Content.Contains("tool-result"))
                    hasToolRole = true;
            Assert.That(hasToolRole, Is.True);
        }
    }

}
