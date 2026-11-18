using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Agent.Core.AgentTools;
using Agent.Core.Context;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Minimal AI provider double that records the system prompt and tool definitions it receives,
    /// so the agent-loop tests can prove which tool environment actually produced the prompt.
    /// </summary>
    internal sealed class CapturingAiProvider : IAiProvider
    {
        private readonly Queue<object> _responses;
        public int CallCount { get; private set; }
        public string LastSystemPrompt { get; private set; }
        public IReadOnlyList<ToolDefinition> LastTools { get; private set; }

        public CapturingAiProvider(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSystemPrompt = systemPrompt;
            return NextAsync();
        }

        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSystemPrompt = systemPrompt;
            LastTools = tools;
            return NextAsync();
        }

        private Task<ChatResponse> NextAsync()
        {
            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : (object)new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" };

            if (next is Exception ex)
                throw ex;

            return Task.FromResult((ChatResponse)next);
        }

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
    public class AgentToolEnvironmentTests
    {
        private static AgentTool ReadTool(string name) =>
            new AgentTool(name, name + " desc", "{}", mutates: false, (a, c) => Task.FromResult(name + "-result"));

        private static AgentTool WriteTool(string name) =>
            new AgentTool(name, name + " desc", "{}", mutates: true, (a, c) => Task.FromResult(name + "-result"));

        [Test]
        public void BuildPrompt_And_BuildToolDefinitions_UseTheSameSnapshot()
        {
            var registry = new AgentToolRegistry();
            registry.Register(ReadTool("read_files"));
            registry.Register(WriteTool("editor"));

            var env = new AgentToolEnvironment
            {
                Registry = registry,
                ToolCallingMode = ToolCallingMode.JsonChat,
                AllowedPermissions = AgentPermissions.All,
                MemoryBankMode = MemoryBankMode.Disabled,
                AllowedReadPaths = new List<string>()
            };

            var planPrompt = env.BuildPrompt(AgentMode.Plan);
            var planTools = env.BuildToolDefinitions(AgentMode.Plan);
            Assert.That(planTools.Count, Is.EqualTo(1));
            Assert.That(planTools[0].Name, Is.EqualTo("read_files"));
            Assert.That(planPrompt, Does.Contain("- read_files"));
            Assert.That(planPrompt, Does.Not.Contain("- editor"));

            var actTools = env.BuildToolDefinitions(AgentMode.Act);
            Assert.That(actTools.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task RunAsync_UsesEnvironment_NotConstructorRegistry()
        {
            var constructorRegistry = new AgentToolRegistry();
            constructorRegistry.Register(ReadTool("old_tool"));

            var envRegistry = new AgentToolRegistry();
            var envCalled = false;
            envRegistry.Register(new AgentTool("env_tool", "env tool", "{}", mutates: false, (a, c) =>
            {
                envCalled = true;
                return Task.FromResult("env-result");
            }));

            var env = new AgentToolEnvironment
            {
                Registry = envRegistry,
                ToolCallingMode = ToolCallingMode.JsonChat,
                AllowedPermissions = AgentPermissions.All,
                MemoryBankMode = MemoryBankMode.Disabled,
                AllowedReadPaths = new List<string>()
            };

            var ai = new CapturingAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"env_tool\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, constructorRegistry, mode => "CONSTRUCTOR-SYSTEM", maxIterations: 4);

            var result = await runner.RunAsync(
                new List<ChatMessageData>(),
                "go",
                null,
                AgentMode.Act,
                new ContextBudget(),
                CancellationToken.None,
                env);

            Assert.That(envCalled, Is.True);
            Assert.That(ai.LastSystemPrompt, Does.Contain("env_tool"));
            Assert.That(ai.LastSystemPrompt, Does.Not.Contain("old_tool"));
            Assert.That(ai.LastSystemPrompt, Does.Not.Contain("CONSTRUCTOR-SYSTEM"));
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task ResumeAsync_UsesSnapshotEnvironment_WhenNoExplicitEnvironment()
        {
            var constructorRegistry = new AgentToolRegistry();
            constructorRegistry.Register(ReadTool("old_tool"));

            var envRegistry = new AgentToolRegistry();
            envRegistry.Register(ReadTool("env_tool"));

            var env = new AgentToolEnvironment
            {
                Registry = envRegistry,
                ToolCallingMode = ToolCallingMode.JsonChat,
                AllowedPermissions = AgentPermissions.All,
                MemoryBankMode = MemoryBankMode.Disabled,
                AllowedReadPaths = new List<string>()
            };

            var snapshot = new AgentResumeSnapshot
            {
                Messages = new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "go" } },
                NextIteration = 0,
                Environment = env
            };

            var ai = new CapturingAiProvider(
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, constructorRegistry, mode => "CONSTRUCTOR-SYSTEM", maxIterations: 4);

            var result = await runner.ResumeAsync(snapshot, AgentMode.Act, new ContextBudget(), CancellationToken.None);

            Assert.That(ai.LastSystemPrompt, Does.Contain("env_tool"));
            Assert.That(ai.LastSystemPrompt, Does.Not.Contain("old_tool"));
            Assert.That(ai.LastSystemPrompt, Does.Not.Contain("CONSTRUCTOR-SYSTEM"));
            Assert.That(result.FinalText, Is.EqualTo("done"));
        }

        [Test]
        public async Task ResumeAsync_ExplicitEnvironment_WinsOverSnapshotEnvironment()
        {
            var snapshotRegistry = new AgentToolRegistry();
            snapshotRegistry.Register(ReadTool("snapshot_tool"));

            var explicitRegistry = new AgentToolRegistry();
            explicitRegistry.Register(ReadTool("explicit_tool"));

            var snapshot = new AgentResumeSnapshot
            {
                Messages = new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "go" } },
                NextIteration = 0,
                Environment = new AgentToolEnvironment
                {
                    Registry = snapshotRegistry,
                    ToolCallingMode = ToolCallingMode.JsonChat,
                    AllowedPermissions = AgentPermissions.All,
                    MemoryBankMode = MemoryBankMode.Disabled
                }
            };

            var explicitEnv = new AgentToolEnvironment
            {
                Registry = explicitRegistry,
                ToolCallingMode = ToolCallingMode.JsonChat,
                AllowedPermissions = AgentPermissions.All,
                MemoryBankMode = MemoryBankMode.Disabled
            };

            var ai = new CapturingAiProvider(
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var runner = new AgentRunner(ai, new AgentToolRegistry(), mode => "CONSTRUCTOR-SYSTEM", maxIterations: 4);

            await runner.ResumeAsync(snapshot, AgentMode.Act, new ContextBudget(), CancellationToken.None, explicitEnv);

            Assert.That(ai.LastSystemPrompt, Does.Contain("explicit_tool"));
            Assert.That(ai.LastSystemPrompt, Does.Not.Contain("snapshot_tool"));
        }
    }
}

