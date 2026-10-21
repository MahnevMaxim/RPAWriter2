using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Runs a shell command as a child process and captures stdout/stderr, mirroring
    /// the process-handling pattern of <see cref="GitService"/>. PowerShell is invoked
    /// non-interactively so the agent can call it without a console.
    /// </summary>
    public sealed class ShellCommandService
    {
        private const int MaxOutputChars = 12000;

        public async Task<ShellCommandResult> RunAsync(
            string command,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = 300)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("command is required.", nameof(command));

            // Base64-encoded UTF-16 command avoids all quoting/escaping issues.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    if (!process.Start())
                        return ShellCommandResult.Failed("Failed to start powershell.exe.");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var exitTask = Task.Run(() => process.WaitForExit());
                    var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false);
                    if (completed != exitTask)
                    {
                        try { process.Kill(); } catch { }
                        cancellationToken.ThrowIfCancellationRequested();
                        return ShellCommandResult.Failed("Command timed out after " + timeoutSeconds + " seconds.");
                    }

                    var code = process.ExitCode;

                    var combined = new StringBuilder();
                    if (stdout.Length > 0) combined.AppendLine(stdout.ToString());
                    if (stderr.Length > 0) combined.AppendLine(stderr.ToString());
                    var text = Truncate(combined.ToString().TrimEnd());

                    LogService.Info("powershell -Command -> exit=" + code + ", output=" + text.Length + " chars");

                    if (code != 0)
                        return new ShellCommandResult { ExitCode = code, Output = text, Success = false };

                    return new ShellCommandResult
                    {
                        ExitCode = 0,
                        Output = string.IsNullOrWhiteSpace(text) ? "(no output)" : text,
                        Success = true
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ShellCommandResult.Failed("Command failed to run: " + ex.Message);
            }
        }

        /// <summary>Runs an arbitrary executable with arguments and captures stdout/stderr.</summary>
        public async Task<ShellCommandResult> RunFileAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = 300)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName is required.", nameof(fileName));
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    if (!process.Start())
                        return ShellCommandResult.Failed("Failed to start " + fileName + ".");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    var exitTask = Task.Run(() => process.WaitForExit());
                    var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)).ConfigureAwait(false);
                    if (completed != exitTask)
                    {
                        try { process.Kill(); } catch { }
                        cancellationToken.ThrowIfCancellationRequested();
                        return ShellCommandResult.Failed("Command timed out after " + timeoutSeconds + " seconds.");
                    }
                    var code = process.ExitCode;
                    var combined = new StringBuilder();
                    if (stdout.Length > 0) combined.AppendLine(stdout.ToString());
                    if (stderr.Length > 0) combined.AppendLine(stderr.ToString());
                    var text = Truncate(combined.ToString().TrimEnd());
                    LogService.Info(Path.GetFileName(fileName) + " -> exit=" + code + ", output=" + text.Length + " chars");
                    if (code != 0)
                        return new ShellCommandResult { ExitCode = code, Output = text, Success = false };
                    return new ShellCommandResult
                    {
                        ExitCode = 0,
                        Output = string.IsNullOrWhiteSpace(text) ? "(no output)" : text,
                        Success = true
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ShellCommandResult.Failed("Command failed to run: " + ex.Message);
            }
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= MaxOutputChars) return text;
            return text.Substring(0, MaxOutputChars) + "\n…(truncated)";
        }
    }

    public sealed class ShellCommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public bool Success { get; set; }

        public static ShellCommandResult Failed(string message)
        {
            return new ShellCommandResult { ExitCode = -1, Output = message, Success = false };
        }
    }
}