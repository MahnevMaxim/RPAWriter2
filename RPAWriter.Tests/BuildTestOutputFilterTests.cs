using System;
using NUnit.Framework;
using Agent.Core.Services;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class BuildTestOutputFilterTests
    {
        [TestCase("dotnet build")]
        [TestCase("dotnet build RPAWriter2.slnx")]
        [TestCase("dotnet test")]
        [TestCase("dotnet test RPAWriter.Tests")]
        [TestCase("dotnet vstest RPAWriter.Tests.dll")]
        [TestCase("dotnet msbuild RPAWriter2.slnx")]
        [TestCase("dotnet.exe build")]
        [TestCase("dotnet.exe test")]
        [TestCase("msbuild RPAWriter2.slnx /t:Build")]
        [TestCase("vstest.console RPAWriter.Tests.dll")]
        public void IsSupported_TrueForBuildAndTestCommands(string command)
        {
            Assert.That(BuildTestOutputFilter.IsSupported(command), Is.True);
        }

        [TestCase("git status --porcelain")]
        [TestCase("dotnet run")]
        [TestCase("dotnet restore")]
        [TestCase("dir")]
        [TestCase("")]
        [TestCase(null)]
        public void IsSupported_FalseForOtherCommands(string command)
        {
            Assert.That(BuildTestOutputFilter.IsSupported(command), Is.False);
        }

        [Test]
        public void Filter_BuildFailed_ReturnsOnlyErrorsAndWarnings()
        {
            var output = string.Join("\n",
                "MSBuild version 17.11.9+abcdef",
                "Copyright (C) Microsoft Corporation. All rights reserved.",
                "",
                "  Determining projects to restore...",
                "  All projects are up-to-date for restore.",
                "  Agent.Core -> C:\\src\\Agent.Core\\bin\\Debug\\netstandard2.0\\Agent.Core.dll",
                "Program.cs(12,5): error CS1002: ; expected",
                "Program.cs(5,1): warning CS0168: The variable 'x' is declared but never used",
                "error MSB3073: The command \"cmd.exe\" exited with code 1.",
                "    1 Warning(s)",
                "    2 Error(s)",
                "Time Elapsed 00:00:03.42",
                "",
                "Build FAILED.");

            var result = BuildTestOutputFilter.Filter("dotnet build", 1, output);

            Assert.That(result, Does.Contain("Errors (2):"));
            Assert.That(result, Does.Contain("- CS1002: ; expected — Program.cs:12:5"));
            Assert.That(result, Does.Contain("- MSB3073: The command \"cmd.exe\" exited with code 1."));
            Assert.That(result, Does.Contain("Warnings (1):"));
            Assert.That(result, Does.Contain("- CS0168: The variable 'x' is declared but never used — Program.cs:5:1"));
            Assert.That(result, Does.Contain("Build failed (exit 1)."));
            Assert.That(result, Does.Not.Contain("MSBuild version"));
            Assert.That(result, Does.Not.Contain("Determining projects to restore"));
            Assert.That(result, Does.Not.Contain("Time Elapsed"));
        }

        [Test]
        public void Filter_BuildSucceeded_ReturnsCompactSummary()
        {
            var output = string.Join("\n",
                "MSBuild version 17.11.9+abcdef",
                "  Agent.Core -> C:\\src\\Agent.Core\\bin\\Debug\\netstandard2.0\\Agent.Core.dll",
                "    0 Warning(s)",
                "    0 Error(s)",
                "",
                "Build succeeded.");

            var result = BuildTestOutputFilter.Filter("dotnet build", 0, output);

            Assert.That(result, Is.EqualTo("Build succeeded."));
        }

        [Test]
        public void Filter_TestPassed_ReturnsTotals()
        {
            var output = string.Join("\n",
                "Starting test execution, please wait...",
                "A total of 1 test files matched the specified pattern.",
                "",
                "Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 1 s - RPAWriter.Tests.dll (net8.0)");

            var result = BuildTestOutputFilter.Filter("dotnet test", 0, output);

            Assert.That(result, Does.Contain("Test run: PASSED (Total: 15, Passed: 15, Failed: 0, Skipped: 0)"));
            Assert.That(result, Does.Not.Contain("Failed tests:"));
        }

        [Test]
        public void Filter_TestFailed_ReturnsFailedTestList()
        {
            var output = string.Join("\n",
                "Starting test execution, please wait...",
                "A total of 1 test files matched the specified pattern.",
                "",
                "  Failed RPAWriter.Tests.CalculatorTests.Add_TwoPlusTwo_ReturnsFour [123 ms]",
                "  Error Message:",
                "     Expected: 5",
                "     But was:  4",
                "  Stack Trace:",
                "     at RPAWriter.Tests.CalculatorTests.Add_TwoPlusTwo_ReturnsFour() in C:\\src\\CalculatorTests.cs:line 10",
                "",
                "Failed!  - Failed: 1, Passed: 14, Skipped: 0, Total: 15, Duration: 5 s - RPAWriter.Tests.dll (net8.0)");

            var result = BuildTestOutputFilter.Filter("dotnet test", 1, output);

            Assert.That(result, Does.Contain("Test run: FAILED (Total: 15, Passed: 14, Failed: 1, Skipped: 0)"));
            Assert.That(result, Does.Contain("Failed tests:"));
            Assert.That(result, Does.Contain("- RPAWriter.Tests.CalculatorTests.Add_TwoPlusTwo_ReturnsFour: Expected: 5"));
            Assert.That(result, Does.Not.Contain("Stack Trace:"));
        }

        [Test]
        public void Filter_TestNunitStyleSummary_ReturnsTotals()
        {
            var output = string.Join("\n",
                "Total tests: 15",
                "     Passed: 14",
                "     Failed: 1",
                "    Skipped: 0",
                " Total time: 5.0000 Seconds");

            var result = BuildTestOutputFilter.Filter("dotnet test", 1, output);

            Assert.That(result, Does.Contain("Test run: FAILED (Total: 15, Passed: 14, Failed: 1, Skipped: 0)"));
        }

        [Test]
        public void Filter_UnrecognizedOutput_ReturnsRawText()
        {
            var output = "some completely unrelated command output";

            var result = BuildTestOutputFilter.Filter("dotnet build", 0, output);

            Assert.That(result, Is.EqualTo(output));
        }
    }
}

