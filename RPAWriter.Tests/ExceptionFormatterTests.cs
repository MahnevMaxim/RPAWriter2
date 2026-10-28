using System;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// The caught-exception chat path formats an exception into a readable multi-line
    /// block: message, stack trace, then each inner exception in turn. These tests pin
    /// the formatter so the chat never receives the whole dump as one unreadable line.
    /// </summary>
    [TestFixture]
    public class ExceptionFormatterTests
    {
        [Test]
        public void Format_IncludesMessageStackTraceAndInnerChain()
        {
            Exception inner = null;
            try { throw new ArgumentException("inner boom"); }
            catch (Exception ex) { inner = ex; }

            Exception outer = null;
            try { throw new InvalidOperationException("outer boom", inner); }
            catch (Exception ex) { outer = ex; }

            var text = ExceptionFormatter.Format(outer);
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

            Assert.That(text, Does.Contain("System.InvalidOperationException: outer boom"));
            Assert.That(text, Does.Contain("Stack trace:"));
            Assert.That(text, Does.Contain("Inner error: System.ArgumentException: inner boom"));
            Assert.That(text, Does.Contain("Inner stack trace:"));

            // Each stack trace is wrapped in a fenced (monospace) code block.
            Assert.That(normalized, Does.Contain("```\nStack trace:\n"));
            Assert.That(normalized, Does.Contain("```\nInner stack trace:\n"));

            // The inner exception must come after the top-level stack trace.
            Assert.That(text.IndexOf("Stack trace:", StringComparison.Ordinal), Is.LessThan(
                text.IndexOf("Inner error:", StringComparison.Ordinal)));
        }

        [Test]
        public void Format_ExceptionWithoutInner_HasNoInnerLabels()
        {
            Exception ex = null;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception caught) { ex = caught; }

            var text = ExceptionFormatter.Format(ex);

            Assert.That(text, Does.Contain("System.InvalidOperationException: boom"));
            Assert.That(text, Does.Not.Contain("Inner error:"));
            Assert.That(text, Does.Not.Contain("Inner stack trace:"));
        }

        [Test]
        public void Format_AggregateException_ListsAllInnerExceptions()
        {
            var aggregate = new AggregateException(
                new InvalidOperationException("first"),
                new ArgumentException("second"));

            var text = ExceptionFormatter.Format(aggregate);

            Assert.That(text, Does.Contain("System.AggregateException:"));
            Assert.That(text, Does.Contain("Inner error: System.InvalidOperationException: first"));
            Assert.That(text, Does.Contain("Inner error: System.ArgumentException: second"));
        }

        [Test]
        public void Format_Null_ReturnsEmpty()
        {
            Assert.That(ExceptionFormatter.Format(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Format_IsDeterministic()
        {
            Exception ex = null;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception caught) { ex = caught; }

            Assert.That(ExceptionFormatter.Format(ex), Is.EqualTo(ExceptionFormatter.Format(ex)));
        }
    }
}
