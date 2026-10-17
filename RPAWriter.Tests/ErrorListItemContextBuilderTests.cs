using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// The "Send to RPA Writer" Error List context menu forwards the selected Error List
    /// item(s) to the agent. These tests pin the builder used by that flow: unlike the
    /// compiler-error flow, it includes entries of every severity and uses a dedicated header.
    /// </summary>
    [TestFixture]
    public class ErrorListItemContextBuilderTests
    {
        private static Func<string, int, int, Task<string>> Reader(string text) => (f, l, c) => Task.FromResult(text);

        private static BuildError Item(string message, string file = "", int line = 0, string code = "", string severity = "Error")
            => new BuildError { Code = code, Message = message, File = file, Line = line, Severity = severity };

        [Test]
        public async Task BuildSelectedItemsAsync_IncludesAllSeverities()
        {
            var items = new List<BuildError>
            {
                Item("error", severity: "Error"),
                Item("warning", severity: "Warning"),
                Item("message", severity: "Message")
            };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, Reader("x"));
            Assert.That(text, Does.Contain("[Error] error"));
            Assert.That(text, Does.Contain("[Warning] warning"));
            Assert.That(text, Does.Contain("[Message] message"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_UsesSelectedItemsHeader()
        {
            var items = new List<BuildError> { Item("x", severity: "Warning") };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, Reader("x"));
            Assert.That(text, Does.StartWith("The user sent this from the Visual Studio Error List:"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_IncludesCodeAndLocation()
        {
            var items = new List<BuildError> { Item("Требуется \";\".", "C:\\proj\\Program.cs", 11, "CS1002") };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, Reader("code"));
            Assert.That(text, Does.Contain("[Error] CS1002: Требуется \";\"."));
            Assert.That(text, Does.Contain("    File: C:\\proj\\Program.cs"));
            Assert.That(text, Does.Contain("    Line: 11"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_IncludesSnippetAroundErrorLine()
        {
            var items = new List<BuildError> { Item("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, Reader(">>> 11: static void Main("));
            Assert.That(text, Does.Contain("C:\\proj\\Program.cs (around line 11):"));
            Assert.That(text, Does.Contain(">>> 11: static void Main("));
        }
        [Test]
        public async Task BuildSelectedItemsAsync_ReaderReturnsNull_WritesCouldNotRead()
        {
            var items = new List<BuildError> { Item("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, (f, l, c) => Task.FromResult<string>(null));
            Assert.That(text, Does.Contain("(could not read: no lines available)"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_ReaderThrows_WritesReason()
        {
            var items = new List<BuildError> { Item("x", "C:\\proj\\Program.cs", 11) };
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, (f, l, c) => Task.FromException<string>(new InvalidOperationException("boom")));
            Assert.That(text, Does.Contain("(could not read: boom)"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_DeduplicatesSnippetByFileAndLine()
        {
            var items = new List<BuildError>
            {
                Item("first", "C:\\proj\\Program.cs", 11),
                Item("second", "C:\\proj\\Program.cs", 11)
            };
            var calls = 0;
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, (f, l, c) => { calls++; return Task.FromResult("code"); });
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(text, Does.Contain("first"));
            Assert.That(text, Does.Contain("second"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_ShowsTruncationNote_SelectedItems()
        {
            var items = new List<BuildError>();
            for (var i = 0; i < 12; i++) items.Add(Item("e" + i, "C:\\proj\\File" + i + ".cs", i + 1));
            var text = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, (f, l, c) => Task.FromResult<string>(null), maxErrorsWithContext: 10);
            Assert.That(text, Does.Contain("(showing 10 of 12 selected items)"));
            Assert.That(text, Does.Contain("e0"));
            Assert.That(text, Does.Contain("e9"));
            Assert.That(text, Does.Not.Contain("e10"));
            Assert.That(text, Does.Not.Contain("e11"));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_NullOrEmptyList_ReturnsEmpty()
        {
            Assert.That(await CompilationErrorContextBuilder.BuildSelectedItemsAsync(null, Reader("x")), Is.EqualTo(string.Empty));
            Assert.That(await CompilationErrorContextBuilder.BuildSelectedItemsAsync(new List<BuildError>(), Reader("x")), Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task BuildSelectedItemsAsync_IsDeterministic()
        {
            var items = new List<BuildError>
            {
                Item("first", "C:\\proj\\A.cs", 3, "CS1", "Error"),
                Item("second", "C:\\proj\\B.cs", 7, "CS2", "Warning")
            };
            Func<string, int, int, Task<string>> reader = (f, l, c) => Task.FromResult(">>> " + l + ": line-of-code");

            var first = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, reader);
            var second = await CompilationErrorContextBuilder.BuildSelectedItemsAsync(items, reader);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Contain("first"));
            Assert.That(first, Does.Contain("second"));
        }
    }
}
