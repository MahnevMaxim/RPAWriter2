using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.AgentTools;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Covers the tool approval gate (<see cref="AgentRunner"/> + <see cref="ToolApprovalGate"/>)
    /// and the persistence round-trip of an approval card.
    /// </summary>
    [TestFixture]
    public class AgentRunnerApprovalTests
    {
        [Test]
        public async Task Gate_Approve_ExecutesTool()
        {
            var called = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false, (a, c) =>
            {
                called = true;
                return Task.FromResult("tool-result");
            }));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"my_tool\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            ToolApprovalGate gate = (request, ct) => Task.FromResult(ToolApprovalDecision.Approve);
            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 4, approvalGate: gate);

            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(called, Is.True);
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task Gate_Reject_SkipsToolAndFeedsRejectionNote()
        {
            var called = false;
            var rejected = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false, (a, c) =>
            {
                called = true;
                return Task.FromResult("tool-result");
            }));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"my_tool\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            ToolApprovalGate gate = (request, ct) => Task.FromResult(ToolApprovalDecision.Reject);
            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 4, approvalGate: gate);
            runner.ToolExecuted += (s, e) => rejected = e.Rejected;

            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(called, Is.False);
            Assert.That(rejected, Is.True);
            Assert.That(result.FinalText, Is.EqualTo("done"));

            Assert.That(result.Transcript.Count, Is.EqualTo(4));
            Assert.That(result.Transcript[2].Role, Is.EqualTo("tool"));
            Assert.That(result.Transcript[2].Content, Is.EqualTo("Tool \"my_tool\" was rejected by the user."));
        }

        [Test]
        public async Task Gate_Null_AlwaysApproves()
        {
            var called = false;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false, (a, c) =>
            {
                called = true;
                return Task.FromResult("tool-result");
            }));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"my_tool\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYSTEM", maxIterations: 4);

            var result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.That(called, Is.True);
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }
    }

    [TestFixture]
    public class ApprovalMappingTests
    {
        [Test]
        public void ApprovalMessage_RoundTrip_PreservesToolNameAndDecision()
        {
            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Approval,
                Payload = new ApprovalPayload { ToolName = "write_file", Decision = "rejected" }
            };

            var stored = ChatMessageMapper.ToHistoryMessage(message);
            Assert.That(stored.Kind, Is.EqualTo(ChatMessageKind.Approval));
            Assert.That(stored.Text, Is.EqualTo("write_file"));
            Assert.That(stored.ToolName, Is.EqualTo("rejected"));

            var restored = ChatMessageMapper.FromHistoryMessage(stored);
            Assert.That(restored.Kind, Is.EqualTo(ChatMessageKind.Approval));
            var payload = restored.Payload as ApprovalPayload;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload.ToolName, Is.EqualTo("write_file"));
            Assert.That(payload.Decision, Is.EqualTo("rejected"));
            Assert.That(payload.IsDecided, Is.True);
            Assert.That(payload.ShowButtons, Is.False);
        }

        [Test]
        public void ApprovalPayload_ShowButtons_FalseWhilePendingAndAfterDecision()
        {
            var pending = new ApprovalPayload();
            Assert.That(pending.IsDecided, Is.False);
            Assert.That(pending.ShowButtons, Is.True);

            pending.Decision = "approved";
            Assert.That(pending.IsDecided, Is.True);
            Assert.That(pending.ShowButtons, Is.False);
            Assert.That(pending.DecisionText, Is.EqualTo("Approved"));
        }
    }
}
