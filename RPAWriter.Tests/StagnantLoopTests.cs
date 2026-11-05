using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Context;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ToolRepetitionDetectorTests
    {
        [Test]
        public void IsBlocked_SameCallRepeated_BlocksAfterLimit()
        {
            var detector = new ToolRepetitionDetector(3);

            Assert.That(detector.IsBlocked("t", new JObject()), Is.False); // 1st: baseline
            Assert.That(detector.IsBlocked("t", new JObject()), Is.False); // 2nd
            Assert.That(detector.IsBlocked("t", new JObject()), Is.False); // 3rd
            Assert.That(detector.IsBlocked("t", new JObject()), Is.True);  // 4th: blocked
        }

        [Test]
        public void IsBlocked_DifferentToolName_ResetsCount()
        {
            var detector = new ToolRepetitionDetector(3);

            Assert.That(detector.IsBlocked("a", new JObject()), Is.False);
            Assert.That(detector.IsBlocked("a", new JObject()), Is.False);
            Assert.That(detector.IsBlocked("b", new JObject()), Is.False); // reset
            Assert.That(detector.IsBlocked("b", new JObject()), Is.False);
            Assert.That(detector.IsBlocked("b", new JObject()), Is.False);
            Assert.That(detector.IsBlocked("b", new JObject()), Is.True);
        }

        [Test]
        public void IsBlocked_DifferentArguments_ResetsCount()
        {
            var detector = new ToolRepetitionDetector(3);

            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 1 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 1 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 2 }), Is.False); // reset
            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 2 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 2 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["x"] = 2 }), Is.True);
        }

        [Test]
        public void IsBlocked_LimitZero_NeverBlocks()
        {
            var detector = new ToolRepetitionDetector(0);
            for (var i = 0; i < 10; i++)
                Assert.That(detector.IsBlocked("t", new JObject()), Is.False);
        }

        [Test]
        public void IsBlocked_KeyOrderDoesNotMatter()
        {
            var detector = new ToolRepetitionDetector(2);

            Assert.That(detector.IsBlocked("t", new JObject { ["a"] = 1, ["b"] = 2 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["b"] = 2, ["a"] = 1 }), Is.False);
            Assert.That(detector.IsBlocked("t", new JObject { ["b"] = 2, ["a"] = 1 }), Is.True);
        }
    }

    [TestFixture]
    public class StagnantLoopTests
    {
        private static ChatResponse ToolCall(string toolName, string arguments = "{}")
        {
            return new ChatResponse
            {
                Answer = "{\"action\":\"tool_call\",\"tool\":\"" + toolName + "\",\"arguments\":" + arguments + "}",
                FinishReason = "stop"
            };
        }

        [Test]
        public async Task RunAsync_RepeatedIdenticalToolCall_StopsLoopAndRaisesEvent()
        {
            var executed = 0;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "t", "{}", mutates: false, (a, c) =>
            {
                executed++;
                return Task.FromResult("same-result");
            }));

            var ai = new FakeAiProvider(
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 10);
            var repetitions = 0;
            runner.ToolRepetitionDetected += (s, e) =>
            {
                repetitions++;
                Assert.That(e.ToolName, Is.EqualTo("my_tool"));
                Assert.That(e.Threshold, Is.EqualTo(3));
            };

            var budget = new ContextBudget { MaxIterations = 10, MaxStagnantRepeats = 3, StagnantLoopNudge = false };
            var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), "go", AgentMode.Act, budget, CancellationToken.None);

            Assert.That(executed, Is.EqualTo(3));
            Assert.That(repetitions, Is.EqualTo(1));
            Assert.That(result.FinalText, Does.Contain("зацикливание"));
            Assert.That(result.FinalText, Does.Contain("my_tool"));
        }

        [Test]
        public async Task RunAsync_VariedArguments_AreNotConsideredStagnant()
        {
            var executed = 0;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "t", "{}", mutates: false, (a, c) =>
            {
                executed++;
                return Task.FromResult("result");
            }));

            var ai = new FakeAiProvider(
                ToolCall("my_tool", "{\"x\":1}"),
                ToolCall("my_tool", "{\"x\":2}"),
                ToolCall("my_tool", "{\"x\":3}"),
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 10);
            var budget = new ContextBudget { MaxIterations = 10, MaxStagnantRepeats = 3 };
            var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), "go", AgentMode.Act, budget, CancellationToken.None);

            Assert.That(executed, Is.EqualTo(3));
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task RunAsync_GuardDisabled_DoesNotStop()
        {
            var executed = 0;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "t", "{}", mutates: false, (a, c) =>
            {
                executed++;
                return Task.FromResult("same-result");
            }));

            var ai = new FakeAiProvider(
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 10);
            var budget = new ContextBudget { MaxIterations = 10, MaxStagnantRepeats = 0 };
            var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), "go", AgentMode.Act, budget, CancellationToken.None);

            Assert.That(executed, Is.EqualTo(4));
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task RunAsync_NudgeGivesOneMoreChance_ThenStops()
        {
            var executed = 0;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "t", "{}", mutates: false, (a, c) =>
            {
                executed++;
                return Task.FromResult("same-result");
            }));

            // With nudge enabled the first threshold hit feeds a nudge instead of stopping;
            // the detector resets, so the model gets one more series before the second hit stops.
            var ai = new FakeAiProvider(
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                ToolCall("my_tool"),
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 10);
            var budget = new ContextBudget { MaxIterations = 10, MaxStagnantRepeats = 3, StagnantLoopNudge = true };
            var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), "go", AgentMode.Act, budget, CancellationToken.None);

            // First 3 calls execute; 4th is nudged (not executed); detector resets, so calls
            // 5-7 execute; the 8th hits the threshold again and stops.
            Assert.That(executed, Is.EqualTo(6));
            Assert.That(result.FinalText, Does.Contain("зацикливание"));
        }

    }
}
