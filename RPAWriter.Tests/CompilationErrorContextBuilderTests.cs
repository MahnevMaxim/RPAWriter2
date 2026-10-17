using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// The "send errors" flow must gather the full compiler-error context deterministically and
    /// only then send a single message to the agent. These tests pin the builder: it returns the
    /// complete context in one call (no incremental emission) and is a pure function of its input.
    /// </summary>
    [TestFixture]
    public class CompilationErrorContextBuilderTests
    {
        private static Func<string, int, int, Task<string>> Reader(string text) => (f, l, c) => Task.FromResult(text);

        private static BuildError Error(string message, string file = "C:\\proj\\Program.cs", int line = 11, string code = "", string severity = "Error")
            => new BuildError { Code = code, Message = message, File = file, Line = line, Severity = severity };

        [Test]
        public async Task BuildAsync_WarningOnlyEntries_AreIncluded()
        {
            var errors = new List<BuildError> { Error("warning", severity: "Warning") };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, Reader("x"));
            Assert.That(text, Does.Contain("[Warning] warning"));
        }

        [Test]
        public async Task BuildAsync_NullOrEmptyList_ReturnsEmpty()
        {
            Assert.That(await CompilationErrorContextBuilder.BuildAsync(null, Reader("x")), Is.EqualTo(string.Empty));
            Assert.That(await CompilationErrorContextBuilder.BuildAsync(new List<BuildError>(), Reader("x")), Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task BuildAsync_IncludesHeaderAndErrorLineWithCodeAndLocation()
        {
            var errors = new List<BuildError> { Error("Требуется \";\".", "C:\\proj\\Program.cs", 11, "CS1002") };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, Reader(">>> 11: static void Main("));
            Assert.That(text, Does.StartWith("Errors, warnings, and messages from the Visual Studio Error List:"));
            Assert.That(text, Does.Contain("[Error] CS1002: Требуется \";\"."));
            Assert.That(text, Does.Contain("    File: C:\\proj\\Program.cs"));
            Assert.That(text, Does.Contain("    Line: 11"));
        }

        [Test]
        public async Task BuildAsync_IncludesSnippetAroundErrorLine()
        {
            var errors = new List<BuildError> { Error("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, Reader(">>> 11: static void Main("));
            Assert.That(text, Does.Contain("C:\\proj\\Program.cs (around line 11):"));
            Assert.That(text, Does.Contain(">>> 11: static void Main("));
        }

        [Test]
        public async Task BuildAsync_ReaderReturnsNull_WritesCouldNotRead()
        {
            var errors = new List<BuildError> { Error("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => Task.FromResult<string>(null));
            Assert.That(text, Does.Contain("(could not read: no lines available)"));
        }

        [Test]
        public async Task BuildAsync_ReaderThrows_WritesReason()
        {
            var errors = new List<BuildError> { Error("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => Task.FromException<string>(new InvalidOperationException("boom")));
            Assert.That(text, Does.Contain("(could not read: boom)"));
        }

        [Test]
        public async Task BuildAsync_DeduplicatesSnippetByFileAndLine()
        {
            var errors = new List<BuildError>
            {
                Error("first", "C:\\proj\\Program.cs", 11),
                Error("second", "C:\\proj\\Program.cs", 11)
            };
            var calls = 0;
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => { calls++; return Task.FromResult("code"); });
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(text, Does.Contain("first"));
            Assert.That(text, Does.Contain("second"));
        }

        [Test]
        public async Task BuildAsync_ShowsTruncationNote_WhenMoreThanMaxErrors()
        {
            var errors = new List<BuildError>();
            for (var i = 0; i < 12; i++) errors.Add(Error("e" + i, "C:\\proj\\File" + i + ".cs", i + 1));
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => Task.FromResult<string>(null), maxErrorsWithContext: 10);
            Assert.That(text, Does.Contain("(showing 10 of 12 entries)"));
            Assert.That(text, Does.Contain("e0"));
            Assert.That(text, Does.Contain("e9"));
            Assert.That(text, Does.Not.Contain("e10"));
            Assert.That(text, Does.Not.Contain("e11"));
        }

        [Test]
        public async Task BuildAsync_IncludesAllSeverities()
        {
            var errors = new List<BuildError>
            {
                Error("error", severity: "Error"),
                Error("warning", severity: "Warning"),
                Error("message", severity: "Message")
            };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => Task.FromResult<string>(null));
            Assert.That(text, Does.Contain("[Error] error"));
            Assert.That(text, Does.Contain("[Warning] warning"));
            Assert.That(text, Does.Contain("[Message] message"));
        }

        [Test]
        public async Task BuildAsync_RendersExtendedProperties()
        {
            var errors = new List<BuildError>
            {
                new BuildError
                {
                    Code = "CS1002",
                    Message = "Требуется \";\".",
                    File = "C:\\proj\\Program.cs",
                    Line = 11,
                    Column = 20,
                    Project = "MyProject",
                    Severity = "Error",
                    ErrorLevel = 4,
                    ErrorLevelName = "vsBuildErrorLevelHigh",
                    Category = "Compiler"
                }
            };
            var text = await CompilationErrorContextBuilder.BuildAsync(errors, (f, l, c) => Task.FromResult<string>(null));
            Assert.That(text, Does.Contain("[Error] CS1002: Требуется \";\"."));
            Assert.That(text, Does.Contain("    File: C:\\proj\\Program.cs"));
            Assert.That(text, Does.Contain("    Line: 11"));
            Assert.That(text, Does.Contain("    Column: 20"));
            Assert.That(text, Does.Contain("    Project: MyProject"));
            Assert.That(text, Does.Contain("    Severity: Error (vsBuildErrorLevelHigh = 4)"));
            Assert.That(text, Does.Contain("    Category: Compiler"));
        }

        [Test]
        public async Task BuildAsync_AssemblesEverythingInOneCall_IsDeterministic()
        {
            // The whole context (all errors + snippets) is produced in one call; nothing is
            // emitted incrementally, and the same input always yields the same output.
            var errors = new List<BuildError>
            {
                Error("first", "C:\\proj\\A.cs", 3),
                Error("second", "C:\\proj\\B.cs", 7)
            };
            Func<string, int, int, Task<string>> reader = (f, l, c) => Task.FromResult(">>> " + l + ": line-of-code");

            var first = await CompilationErrorContextBuilder.BuildAsync(errors, reader);
            var second = await CompilationErrorContextBuilder.BuildAsync(errors, reader);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Contain("first"));
            Assert.That(first, Does.Contain("second"));
            Assert.That(first, Does.Contain(">>> 3: line-of-code"));
            Assert.That(first, Does.Contain(">>> 7: line-of-code"));
        }
    }
}