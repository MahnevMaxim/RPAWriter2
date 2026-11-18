using NUnit.Framework;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Covers <see cref="AgentModeNames"/>: the single mapping between <see cref="AgentMode"/> and
    /// the toggle value shown in the chat footer. Both the XAML converter and the transcript entry
    /// use it, so the round-trip and the fallback are asserted here.
    /// </summary>
    [TestFixture]
    public class AgentModeNamesTests
    {
        [TestCase(AgentMode.Plan, "Plan")]
        [TestCase(AgentMode.Act, "Act")]
        [TestCase(AgentMode.Help, "Help")]
        public void ToValue_ReturnsTheToggleValue(AgentMode mode, string expected)
        {
            Assert.That(AgentModeNames.ToValue(mode), Is.EqualTo(expected));
        }

        [TestCase("Plan", AgentMode.Plan)]
        [TestCase("Act", AgentMode.Act)]
        [TestCase("Help", AgentMode.Help)]
        public void Parse_IsCaseInsensitiveAndReversible(string value, AgentMode expected)
        {
            Assert.That(AgentModeNames.Parse(value), Is.EqualTo(expected));
            Assert.That(AgentModeNames.Parse(value.ToUpperInvariant()), Is.EqualTo(expected));
            Assert.That(AgentModeNames.Parse(AgentModeNames.ToValue(expected)), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Unknown")]
        public void Parse_FallsBackToPlan(string value)
        {
            Assert.That(AgentModeNames.Parse(value), Is.EqualTo(AgentMode.Plan));
        }

        [Test]
        public void Parse_AcceptsAnEnumValue()
        {
            Assert.That(AgentModeNames.Parse(AgentMode.Help), Is.EqualTo(AgentMode.Help));
        }
    }
}
