using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Checkpoints
{
    internal sealed class GitResult
    {
        public int ExitCode { get; set; } = -1;

        public string StdOut { get; set; } = "";

        public string StdErr { get; set; } = "";

        public bool Success => ExitCode == 0;
    }

    internal interface IGitRunner
    {
        Task<GitResult> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct, int timeoutSeconds = 120);
    }

    /// <summary>
    /// Runs git.exe as a child process. The environment is sanitized so inherited
    /// git variables (GIT_DIR, GIT_WORK_TREE, ...) cannot redirect a checkpoint
    /// operation to a different repository.
    /// </summary>
    internal sealed class ProcessGitRunner : IGitRunner
    {
        private static readonly string[] SanitizedEnvVars =
        {
            "GIT_DIR",
            "GIT_WORK_TREE",
            "GIT_INDEX_FILE",
            "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES",
            "GIT_CEILING_DIRECTORIES",
            "GIT_TEMPLATE_DIR",
        };

        public async Task<GitResult> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct, int timeoutSeconds = 120)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = BuildArguments(args),
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var name in SanitizedEnvVars)
            {
                psi.Environment.Remove(name);
            }

            try
            {
                using (var process = new Process { StartInfo = psi })
                using (ct.Register(() => { try { process.Kill(); } catch { } }))
                {
                    if (!process.Start())
                    {
                        return new GitResult { ExitCode = -1, StdErr = "Failed to start git.exe." };
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    var exitTask = Task.Run(() => process.WaitForExit());
                    var all = Task.WhenAll(stdoutTask, stderrTask, exitTask);

                    var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
                    if (completed != all)
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("git command timed out after " + timeoutSeconds + " seconds.");
                    }

                    await all.ConfigureAwait(false);
                    return new GitResult
                    {
                        ExitCode = process.ExitCode,
                        StdOut = stdoutTask.Result ?? "",
                        StdErr = stderrTask.Result ?? "",
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new GitResult { ExitCode = -1, StdErr = ex.Message };
            }
        }

        internal static string BuildArguments(IReadOnlyList<string> args)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArg(args[i] ?? ""));
            }
            return sb.ToString();
        }

        internal static string QuoteArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";

            var needsQuotes = false;
            for (var i = 0; i < arg.Length; i++)
            {
                if (char.IsWhiteSpace(arg[i]) || arg[i] == '"')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes) return arg;

            var sb = new StringBuilder(arg.Length + 2);
            sb.Append('"');
            var backslashes = 0;
            for (var i = 0; i < arg.Length; i++)
            {
                var c = arg[i];
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    for (var j = 0; j < backslashes; j++) sb.Append("\\\\");
                    sb.Append("\\\"");
                    backslashes = 0;
                }
                else
                {
                    for (var j = 0; j < backslashes; j++) sb.Append('\\');
                    sb.Append(c);
                    backslashes = 0;
                }
            }
            for (var j = 0; j < backslashes; j++) sb.Append("\\\\");
            sb.Append('"');
            return sb.ToString();
        }
    }
}
