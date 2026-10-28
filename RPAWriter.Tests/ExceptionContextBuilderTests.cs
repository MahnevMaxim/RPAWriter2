using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// The "send debugger exception" flow must assemble the analysis prompt and the full
    /// exception dump deterministically and only then send one message to the agent. These
    /// tests pin the builder: it returns the complete context in one call and is a pure
    /// function of its input.
    /// </summary>
    [TestFixture]
    public class ExceptionContextBuilderTests
    {
        [Test]
        public void Build_StartsWithAnalysisPrompt()
        {
            var text = ExceptionContextBuilder.Build("System.Exception: boom");

            Assert.That(text, Does.StartWith(ExceptionContextBuilder.AnalysisPrompt));
            Assert.That(text, Does.Contain("Полный дамп исключения"));
        }

        [Test]
        public void Build_WrapsDumpInCodeFence_AndPreservesNestedInnerExceptions()
        {
            var dump = "System.InvalidOperationException: outer\r\n ---> System.ArgumentNullException: inner";

            var text = ExceptionContextBuilder.Build(dump);

            Assert.That(text, Does.Contain("```"));
            Assert.That(text, Does.Contain("System.InvalidOperationException: outer"));
            Assert.That(text, Does.Contain("System.ArgumentNullException: inner"));
        }

        [Test]
        public void Build_IncludesTypeReasonAndLocation()
        {
            var text = ExceptionContextBuilder.Build(
                "dump",
                "System.NullReferenceException",
                "dbgEventReasonExceptionThrown",
                "Program.Main");

            Assert.That(text, Does.Contain("Тип исключения: System.NullReferenceException"));
            Assert.That(text, Does.Contain("Причина остановки отладчика: dbgEventReasonExceptionThrown"));
            Assert.That(text, Does.Contain("Место остановки: Program.Main"));
        }

        [Test]
        public void Build_UnescapesDebuggerLineBreaks()
        {
            var text = ExceptionContextBuilder.Build("System.Exception: boom\\r\\n   at App.Main()");

            Assert.That(text, Does.Not.Contain("\\r\\n"));
            Assert.That(text, Does.Contain("boom\r\n   at App.Main()"));
        }

        [Test]
        public void Build_EmptyDump_UsesPlaceholder_DoesNotThrow()
        {
            var text = ExceptionContextBuilder.Build("", "", "", "");

            Assert.That(text, Does.Contain("(no exception dump available)"));
        }

        [Test]
        public void Build_NullDump_IsSafe()
        {
            Assert.DoesNotThrow(() => ExceptionContextBuilder.Build(null));
        }

        [Test]
        public void Build_IsDeterministic()
        {
            Assert.That(
                ExceptionContextBuilder.Build("dump", "T", "R", "L"),
                Is.EqualTo(ExceptionContextBuilder.Build("dump", "T", "R", "L")));
        }

        [Test]
        public void Build_FromSnapshot_UsesItsFields()
        {
            var snapshot = new DebuggerExceptionSnapshot
            {
                TypeName = "System.NullReferenceException",
                Message = "Object reference not set",
                ExceptionDump = "full dump",
                BreakReason = "dbgEventReasonExceptionNotHandled",
                Location = "Program.Run"
            };

            var text = ExceptionContextBuilder.Build(snapshot);

            Assert.That(text, Does.Contain("Тип исключения: System.NullReferenceException"));
            Assert.That(text, Does.Contain("Причина остановки отладчика: dbgEventReasonExceptionNotHandled"));
            Assert.That(text, Does.Contain("Место остановки: Program.Run"));
            Assert.That(text, Does.Contain("full dump"));
        }

        [Test]
        public void Build_NullSnapshot_IsSafe()
        {
            Assert.DoesNotThrow(() => ExceptionContextBuilder.Build((DebuggerExceptionSnapshot)null));
        }
    }
}
