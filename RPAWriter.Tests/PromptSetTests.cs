using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class PromptSetTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "rpawriter_promptset_" + Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Load_MissingDirectory_ReturnsBuiltInDefaults()
        {
            var set = PromptSet.Load(Path.Combine(_dir, "does-not-exist"));

            Assert.That(set.SystemPrompt, Is.EqualTo(PromptDefaults.SystemPrompt));
            Assert.That(set.ChatSystemPrompt, Is.EqualTo(PromptDefaults.ChatSystemPrompt));
            Assert.That(set.AnalyzeSpec, Is.EqualTo(PromptDefaults.AnalyzeSpec));
            Assert.That(set.GenerateRobot, Is.EqualTo(PromptDefaults.GenerateRobot));
            Assert.That(set.DecomposeTasks, Is.EqualTo(PromptDefaults.DecomposeTasks));
            Assert.That(set.ReconstructSpec, Is.EqualTo(PromptDefaults.ReconstructSpec));
            Assert.That(set.MapTaskToSolution, Is.EqualTo(PromptDefaults.MapTaskToSolution));
            Assert.That(set.AgentJsonBaseSystemPrompt, Is.EqualTo(PromptDefaults.AgentJsonBaseSystemPrompt));
            Assert.That(set.AgentCompactSystemPrompt, Is.EqualTo(PromptDefaults.AgentCompactSystemPrompt));
            Assert.That(set.AgentPlanSystemPromptNative, Is.EqualTo(PromptDefaults.AgentPlanSystemPromptNative));
            Assert.That(set.AgentActSystemPromptNative, Is.EqualTo(PromptDefaults.AgentActSystemPromptNative));
        }

        [Test]
        public void Load_ReadsDeployedFile_OthersFallBack()
        {
            var system = Path.Combine(_dir, "System");
            Directory.CreateDirectory(system);
            File.WriteAllText(Path.Combine(system, "agent-system.txt"), "  custom system prompt  ");

            var set = PromptSet.Load(_dir);

            Assert.That(set.SystemPrompt, Is.EqualTo("custom system prompt"));
            Assert.That(set.ChatSystemPrompt, Is.EqualTo(PromptDefaults.ChatSystemPrompt));
        }

        [Test]
        public void PromptDefaults_AllTemplatesAreNonEmpty()
        {
            Assert.That(PromptDefaults.SystemPrompt, Is.Not.Empty);
            Assert.That(PromptDefaults.ChatSystemPrompt, Is.Not.Empty);
            Assert.That(PromptDefaults.AnalyzeSpec, Is.Not.Empty);
            Assert.That(PromptDefaults.GenerateRobot, Is.Not.Empty);
            Assert.That(PromptDefaults.DecomposeTasks, Is.Not.Empty);
            Assert.That(PromptDefaults.ReconstructSpec, Is.Not.Empty);
            Assert.That(PromptDefaults.MapTaskToSolution, Is.Not.Empty);
            Assert.That(PromptDefaults.AgentJsonBaseSystemPrompt, Is.Not.Empty);
            Assert.That(PromptDefaults.AgentCompactSystemPrompt, Is.Not.Empty);
            Assert.That(PromptDefaults.AgentPlanSystemPromptNative, Is.Not.Empty);
            Assert.That(PromptDefaults.AgentActSystemPromptNative, Is.Not.Empty);
            Assert.That(PromptDefaults.MemoryBankConventionInstructions, Is.Not.Empty);
        }
    }
}
