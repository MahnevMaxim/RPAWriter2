using System;
using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Tests of the robot-name history rules: newest first, no case-insensitive duplicates,
    /// bounded length, placeholder never remembered.
    /// </summary>
    [TestFixture]
    public class RobotNameHistoryTests
    {
        [Test]
        public void Add_PutsNewNameFirst()
        {
            var history = new List<string> { "RPA-1" };

            Assert.That(RobotNameHistory.Add(history, "RPA-2"), Is.True);
            Assert.That(history, Is.EqualTo(new[] { "RPA-2", "RPA-1" }));
        }

        [Test]
        public void Add_TrimsName()
        {
            var history = new List<string>();

            RobotNameHistory.Add(history, "  RPA-1212  ");

            Assert.That(history, Is.EqualTo(new[] { "RPA-1212" }));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Add_IgnoresBlankNames(string name)
        {
            var history = new List<string> { "RPA-1" };

            Assert.That(RobotNameHistory.Add(history, name), Is.False);
            Assert.That(history, Is.EqualTo(new[] { "RPA-1" }));
        }

        [Test]
        public void Add_IgnoresPlaceholderRegardlessOfCase()
        {
            var history = new List<string>();

            Assert.That(RobotNameHistory.Add(history, "rpa-"), Is.False);
            Assert.That(history, Is.Empty);
        }

        [Test]
        public void Add_DeduplicatesIgnoringCaseAndMovesToFront()
        {
            var history = new List<string> { "RPA-1", "rpa-2", "RPA-3" };

            RobotNameHistory.Add(history, "RPA-2");

            Assert.That(history, Is.EqualTo(new[] { "RPA-2", "RPA-1", "RPA-3" }));
        }

        [Test]
        public void Add_ReturnsFalseWhenListDoesNotChange()
        {
            var history = new List<string> { "RPA-1" };

            Assert.That(RobotNameHistory.Add(history, "RPA-1"), Is.False);
        }

        [Test]
        public void Add_TruncatesToLimit()
        {
            var history = new List<string>
            {
                "A", "B", "C"
            };

            RobotNameHistory.Add(history, "D", max: 3);

            Assert.That(history, Is.EqualTo(new[] { "D", "A", "B" }));
        }

        [Test]
        public void Add_HandlesNullList()
        {
            Assert.That(RobotNameHistory.Add(null, "RPA-1"), Is.False);
        }

        [Test]
        public void Normalize_TrimsDropsBlanksAndDuplicates()
        {
            var result = RobotNameHistory.Normalize(new[] { " RPA-1 ", "", null, "rpa-1", "RPA-2 " });

            Assert.That(result, Is.EqualTo(new[] { "RPA-1", "RPA-2" }));
        }

        [Test]
        public void Normalize_EnforcesLimit()
        {
            var result = RobotNameHistory.Normalize(new[] { "A", "B", "C", "D" }, max: 2);

            Assert.That(result, Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void Normalize_HandlesNull()
        {
            Assert.That(RobotNameHistory.Normalize(null), Is.Empty);
        }

        [Test]
        public void ResolveCurrent_FallsBackToPlaceholder()
        {
            Assert.That(RobotNameHistory.ResolveCurrent(new List<string>()), Is.EqualTo(RobotNameHistory.DefaultRobotName));
            Assert.That(RobotNameHistory.ResolveCurrent(null), Is.EqualTo(RobotNameHistory.DefaultRobotName));
            Assert.That(RobotNameHistory.ResolveCurrent(new List<string> { "RPA-7" }), Is.EqualTo("RPA-7"));
        }

        [Test]
        public void ResolveSuggestions_SkipsTheCurrentName()
        {
            var suggestions = RobotNameHistory.ResolveSuggestions(new List<string> { "RPA-7", "RPA-1", "RPA-2" });

            Assert.That(suggestions, Is.EqualTo(new[] { "RPA-1", "RPA-2" }));
            Assert.That(RobotNameHistory.ResolveSuggestions(new List<string> { "RPA-7" }), Is.Empty);
            Assert.That(RobotNameHistory.ResolveSuggestions(null), Is.Empty);
        }
    }
}
