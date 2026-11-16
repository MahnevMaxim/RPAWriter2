using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reduces raw MSBuild/VSTest console output to a compact, structured summary:
    /// a build yields only errors/warnings (code + location + message) plus a result line,
    /// a test run yields passed/failed/skipped totals plus the list of failed tests.
    /// Everything else (SDK banners, progress, duplicate lines) is dropped by construction,
    /// because unmatched lines are never echoed. Pure string parsing with no external
    /// dependencies, so it is fully unit-testable.
    /// </summary>
    public static class BuildTestOutputFilter
    {
        // MSBuild/MSVC/NuGet diagnostics:
        //   path\File.cs(12,5): error CS1002: ; expected
        //   error MSB3073: The command "..." exited with code 1.
        private static readonly Regex DiagnosticRegex = new Regex(
            @"^\s*(?:(?<file>[^( \t]+)\((?<line>\d+)(?:,(?<column>\d+))?\)\s*:\s*)?(?<severity>error|warning)\s+(?<code>[A-Za-z]{1,10}\d{2,6})\s*:\s*(?<message>.*)$",
            RegexOptions.Compiled);

        // VSTest summary: "Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 1 s - ..."
        private static readonly Regex VstestSummaryRegex = new Regex(
            @"^(?<result>Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)",
            RegexOptions.Compiled);

        // NUnit/MSTest v1 style summary lines.
        private static readonly Regex TotalTestsRegex = new Regex(@"^\s*Total tests:\s*(?<total>\d+)", RegexOptions.Compiled);
        private static readonly Regex PassedCountRegex = new Regex(@"^\s*Passed:\s*(?<passed>\d+)", RegexOptions.Compiled);
        private static readonly Regex FailedCountRegex = new Regex(@"^\s*Failed:\s*(?<failed>\d+)", RegexOptions.Compiled);
        private static readonly Regex SkippedCountRegex = new Regex(@"^\s*Skipped:\s*(?<skipped>\d+)", RegexOptions.Compiled);

        // VSTest failed-test line: "  Failed Fully.Qualified.Name [42 ms]"
        private static readonly Regex FailedTestRegex = new Regex(
            @"^\s*Failed\s+(?<name>[^\s\[].*?)(?:\s+\[[^\]]*\])?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex ErrorMessageHeaderRegex = new Regex(@"^\s*Error Message:\s*$", RegexOptions.Compiled);
        private static readonly Regex StackTraceHeaderRegex = new Regex(@"^\s*Stack Trace:\s*$", RegexOptions.Compiled);

        /// <summary>
        /// True when the command is a .NET/MSBuild build or test invocation whose output should
        /// be summarized instead of returned raw.
        /// </summary>
        public static bool IsSupported(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            var c = command.Trim();
            return ContainsInvocation(c, "dotnet build")
                || ContainsInvocation(c, "dotnet test")
                || ContainsInvocation(c, "dotnet vstest")
                || ContainsInvocation(c, "dotnet msbuild")
                || ContainsInvocation(c, "dotnet.exe build")
                || ContainsInvocation(c, "dotnet.exe test")
                || ContainsInvocation(c, "msbuild")
                || ContainsInvocation(c, "vstest.console");
        }

        /// <summary>
        /// Returns a compact structured summary for build/test output, or the original text when
        /// the output does not look like a build/test run (fallback keeps unknown commands working).
        /// </summary>
        public static string Filter(string command, int exitCode, string output)
        {
            var text = output ?? string.Empty;
            var result = Parse(text);

            if (result.IsTestRun)
                return FormatTestResult(result, exitCode);

            if (result.IsBuild)
                return FormatBuildResult(result, exitCode);

            return text;
        }

        private static string FormatBuildResult(FilterResult result, int exitCode)
        {
            var sb = new StringBuilder();
            if (result.Errors.Count > 0)
            {
                sb.AppendLine("Errors (" + result.Errors.Count + "):");
                foreach (var error in result.Errors)
                    sb.AppendLine("- " + error);
            }

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings (" + result.Warnings.Count + "):");
                foreach (var warning in result.Warnings)
                    sb.AppendLine("- " + warning);
            }

            sb.Append(exitCode == 0 && result.Errors.Count == 0
                ? "Build succeeded."
                : "Build failed (exit " + exitCode + ").");
            return sb.ToString();
        }

        private static string FormatTestResult(FilterResult result, int exitCode)
        {
            var sb = new StringBuilder();
            sb.AppendLine(result.TestSummary ?? (exitCode == 0 ? "Test run passed." : "Test run failed (exit " + exitCode + ")."));

            if (result.FailedTests.Count > 0)
            {
                sb.AppendLine("Failed tests:");
                foreach (var test in result.FailedTests)
                    sb.AppendLine("- " + test.Name + (string.IsNullOrEmpty(test.Message) ? string.Empty : ": " + test.Message));
            }

            return sb.ToString().TrimEnd();
        }

        private static FilterResult Parse(string text)
        {
            var result = new FilterResult();
            if (string.IsNullOrEmpty(text))
                return result;

            var lines = SplitLines(text);

            var isTest = lines.Any(IsTestMarker);
            if (!isTest)
                isTest = lines.Any(line =>
                    VstestSummaryRegex.IsMatch(line) ||
                    TotalTestsRegex.IsMatch(line));

            var isBuild = false;
            if (!isTest)
                isBuild = lines.Any(IsBuildMarker) || lines.Any(line => DiagnosticRegex.IsMatch(line));

            if (isTest)
            {
                result.IsTestRun = true;
                ParseTest(lines, result);
            }
            else if (isBuild)
            {
                result.IsBuild = true;
                ParseBuild(lines, result);
            }

            return result;
        }

        private static bool IsBuildMarker(string line)
        {
            return line.IndexOf("Build succeeded", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Build FAILED", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Build started", StringComparison.OrdinalIgnoreCase) >= 0
                || Regex.IsMatch(line, @"^\s*\d+\s+(?:Error|Warning)\(s\)\s*$");
        }

        private static bool IsTestMarker(string line)
        {
            return line.IndexOf("Test Run", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Test run", StringComparison.OrdinalIgnoreCase) >= 0
                || VstestSummaryRegex.IsMatch(line)
                || TotalTestsRegex.IsMatch(line);
        }

        private static void ParseBuild(string[] lines, FilterResult result)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var line in lines)
            {
                var match = DiagnosticRegex.Match(line);
                if (!match.Success)
                    continue;

                var code = match.Groups["code"].Value;
                var message = (match.Groups["message"].Value ?? string.Empty).Trim();
                var location = FormatLocation(match);
                var item = code + ": " + message + (location.Length > 0 ? " — " + location : string.Empty);

                if (string.Equals(match.Groups["severity"].Value, "error", StringComparison.OrdinalIgnoreCase))
                    errors.Add(item);
                else
                    warnings.Add(item);
            }

            result.Errors = errors.Distinct(StringComparer.Ordinal).ToList();
            result.Warnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string FormatLocation(Match match)
        {
            var file = match.Groups["file"].Value;
            if (string.IsNullOrEmpty(file))
                return string.Empty;

            var line = match.Groups["line"].Value;
            var column = match.Groups["column"].Value;
            var location = file;
            if (!string.IsNullOrEmpty(line))
                location += ":" + line;
            if (!string.IsNullOrEmpty(column))
                location += ":" + column;
            return location;
        }


        private static void ParseTest(string[] lines, FilterResult result)
        {
            int? failed = null, passed = null, skipped = null, total = null;

            foreach (var line in lines)
            {
                var match = VstestSummaryRegex.Match(line);
                if (match.Success)
                {
                    failed = ToNullableInt(match.Groups["failed"].Value);
                    passed = ToNullableInt(match.Groups["passed"].Value);
                    skipped = ToNullableInt(match.Groups["skipped"].Value);
                    total = ToNullableInt(match.Groups["total"].Value);
                    break;
                }
            }

            if (!total.HasValue)
            {
                foreach (var line in lines)
                {
                    if (!total.HasValue) total = ReadCount(line, TotalTestsRegex, "total");
                    if (!passed.HasValue) passed = ReadCount(line, PassedCountRegex, "passed");
                    if (!failed.HasValue) failed = ReadCount(line, FailedCountRegex, "failed");
                    if (!skipped.HasValue) skipped = ReadCount(line, SkippedCountRegex, "skipped");
                }
            }

            if (total.HasValue)
            {
                var overall = failed.GetValueOrDefault() == 0 ? "PASSED" : "FAILED";
                result.TestSummary = "Test run: " + overall
                    + " (Total: " + total.Value
                    + ", Passed: " + passed.GetValueOrDefault()
                    + ", Failed: " + failed.GetValueOrDefault()
                    + ", Skipped: " + skipped.GetValueOrDefault() + ")";
            }

            result.FailedTests = ParseFailedTests(lines);
        }

        private static int? ReadCount(string line, Regex regex, string group)
        {
            var match = regex.Match(line);
            return match.Success ? ToNullableInt(match.Groups[group].Value) : (int?)null;
        }

        private static List<FailedTest> ParseFailedTests(string[] lines)
        {
            var failedTests = new List<FailedTest>();
            FailedTest current = null;

            foreach (var line in lines)
            {
                var match = FailedTestRegex.Match(line);
                if (match.Success)
                {
                    current = new FailedTest { Name = match.Groups["name"].Value.Trim() };
                    failedTests.Add(current);
                    continue;
                }

                if (current == null || current.Message.Length > 0)
                    continue;

                if (ErrorMessageHeaderRegex.IsMatch(line) || StackTraceHeaderRegex.IsMatch(line))
                    continue;

                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("at ", StringComparison.Ordinal))
                    continue;

                current.Message = trimmed;
            }

            return failedTests;
        }

        private static int? ToNullableInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? (int?)parsed : null;
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static bool ContainsInvocation(string command, string invocation)
        {
            return command.IndexOf(invocation, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class FilterResult
        {
            public bool IsBuild { get; set; }
            public bool IsTestRun { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
            public string TestSummary { get; set; }
            public List<FailedTest> FailedTests { get; set; } = new List<FailedTest>();
        }

        private sealed class FailedTest
        {
            public string Name { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }
    }
}

