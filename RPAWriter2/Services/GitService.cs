using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Thin wrapper around the local git executable for the commit control.
    /// All commands run against a specific repository directory and are safe to
    /// call from a background thread.
    /// </summary>
    public sealed class GitService
    {
        private const string GitExecutable = "git";
        private const int MaxLoggedOutputChars = 20000;

        /// <summary>
        /// Resolves the repository root for the given directory by delegating to git.
        /// </summary>
        public async Task<string> GetRepositoryRootAsync(string startDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
                throw new ArgumentException("startDirectory is required.", nameof(startDirectory));

            var output = await RunAsync(startDirectory, "rev-parse --show-toplevel", cancellationToken).ConfigureAwait(false);
            var root = output.Trim();
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Could not determine the git repository root.");

            return root;
        }

        /// <summary>
        /// Returns the name of the currently checked-out branch.
        /// </summary>
        public async Task<string> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var output = await RunAsync(repositoryRoot, "branch --show-current", cancellationToken).ConfigureAwait(false);
            return output.Trim();
        }

        /// <summary>
        /// Builds the text used to auto-generate a commit message: the diff against HEAD plus the
        /// list of changed files from <c>git status --porcelain</c> (covers staged, unstaged and
        /// untracked entries, and also works when diffing against HEAD is not possible).
        /// </summary>
        public async Task<string> GetDiffForMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var status = await RunAsync(repositoryRoot, "status --porcelain", cancellationToken).ConfigureAwait(false);

            var diff = string.Empty;
            try
            {
                diff = await RunAsync(repositoryRoot, "diff HEAD --", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Warn("GetDiffForMessage: git diff HEAD failed, falling back to the changed-file list: " + ex.Message);
            }

            LogService.Info("GetDiffForMessage: status=" + status.Length + " chars, diff=" + diff.Length + " chars");

            var changed = new List<string>();
            foreach (var line in SplitLines(status))
            {
                // Porcelain format: two status characters followed by a space and the path.
                if (line.Length > 3)
                    changed.Add(line.Substring(3));
            }

            LogService.Info("GetDiffForMessage: changed=" + changed.Count);

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(diff))
                parts.Add(diff.TrimEnd());
            if (changed.Count > 0)
                parts.Add("Changed files:" + Environment.NewLine + string.Join(Environment.NewLine, changed));

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        /// <summary>
        /// Stages all changes: <c>git add -A</c>.
        /// </summary>
        public Task StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            return RunAsync(repositoryRoot, "add -A", cancellationToken);
        }

        /// <summary>
        /// Creates a commit with the given message: <c>git commit -F -</c> (message is fed via stdin,
        /// which avoids command-line quoting issues for multiline or quoted messages).
        /// </summary>
        public Task CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Commit message is required.", nameof(message));

            return RunAsync(repositoryRoot, "commit -F -", cancellationToken, sw => sw.Write(message));
        }

        private static async Task<string> RunAsync(
            string workingDirectory,
            string arguments,
            CancellationToken cancellationToken,
            Action<StreamWriter>? feedStandardInput = null)
        {
            LogService.Info("git " + arguments + " [dir=" + workingDirectory + "]");

            var psi = new ProcessStartInfo
            {
                FileName = GitExecutable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (feedStandardInput != null)
                psi.RedirectStandardInput = true;

            using (var process = new Process { StartInfo = psi })
            {
                try
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start git. Ensure git is installed and available on PATH.");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    LogService.Error(ex, "Failed to start git. Ensure git is installed and available on PATH.");
                    throw new InvalidOperationException("Failed to start git. Ensure git is installed and available on PATH.", ex);
                }

                if (feedStandardInput != null)
                {
                    feedStandardInput(process.StandardInput);
                    process.StandardInput.Close();
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var exitTask = Task.Run(() => process.WaitForExit());

                var completed = await Task.WhenAny(exitTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                if (completed != exitTask)
                {
                    try { process.Kill(); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                LogService.Info("git " + arguments + " -> exit=" + process.ExitCode + ", stdout=" + stdout.Length + " chars, stderr=" + stderr.Length + " chars");

                if (!string.IsNullOrWhiteSpace(stdout))
                    LogService.Info("git " + arguments + " stdout:" + Environment.NewLine + FormatLoggedOutput(stdout));
                if (!string.IsNullOrWhiteSpace(stderr))
                    LogService.Info("git " + arguments + " stderr:" + Environment.NewLine + FormatLoggedOutput(stderr));

                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    LogService.Warn("git " + arguments + " failed: " + detail.Trim());
                    throw new InvalidOperationException("git " + arguments + " failed (exit code " + process.ExitCode + "): " + detail.Trim());
                }

                return stdout;
            }
        }

        public async Task<List<string>> GetChangedFilesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var status = await RunAsync(repositoryRoot, "status --porcelain --untracked-files=all", cancellationToken).ConfigureAwait(false);
            var changed = new List<string>();
            foreach (var line in SplitLines(status))
            {
                if (line.Length > 3)
                    changed.Add(line.Substring(3));
            }
            return changed;
        }

        public async Task<string> GetDiffForPathAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
        {
            return await RunAsync(repositoryRoot, "diff HEAD -- " + relativePath, cancellationToken).ConfigureAwait(false);
        }

        private static string FormatLoggedOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var trimmed = text.TrimEnd();
            if (trimmed.Length <= MaxLoggedOutputChars)
                return trimmed;

            return trimmed.Substring(0, MaxLoggedOutputChars) + Environment.NewLine + "... (output truncated, " + trimmed.Length + " chars total)";
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
