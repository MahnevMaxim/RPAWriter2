using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Mcp
{
    /// <summary>
    /// Launches an MCP server as a subprocess and exchanges newline-delimited JSON-RPC
    /// messages over its stdin/stdout. stderr is consumed separately and surfaced via
    /// <see cref="StderrLine"/> so it never corrupts the protocol stream.
    /// </summary>
    public sealed class StdioMcpTransport : IMcpTransport
    {
        private readonly McpServerConfig _config;
        private readonly object _writeLock = new object();
        private Process _process;
        private StreamWriter _stdin;
        private bool _disposed;

        public StdioMcpTransport(McpServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public event EventHandler<McpMessageEventArgs> MessageReceived;
        public event EventHandler<McpMessageEventArgs> StderrLine;

        public bool IsStarted => _process != null && !_process.HasExited;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsStarted)
                throw new InvalidOperationException("The transport is already started.");

            if (string.IsNullOrWhiteSpace(_config.Command))
                throw new InvalidOperationException("MCP server \"" + _config.Name + "\" has no command to run.");

            var psi = BuildStartInfo(_config);
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += OnOutputLine;
            process.ErrorDataReceived += OnErrorLine;

            if (!process.Start())
                throw new InvalidOperationException("Failed to start MCP server \"" + _config.Name + "\" (" + psi.FileName + ").");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            _stdin = process.StandardInput;
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken cancellationToken)
        {
            if (_process == null || _stdin == null)
                throw new InvalidOperationException("The transport is not started.");

            // Writes must be serialized: stdin is a single shared pipe.
            lock (_writeLock)
            {
                _stdin.WriteLine(json ?? "");
                _stdin.Flush();
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            var process = _process;
            _process = null;
            _stdin = null;

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        if (!process.WaitForExit(2000))
                            process.Kill();
                    }
                }
                catch { /* best effort */ }
                finally
                {
                    process.Dispose();
                }
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* best effort */ }
        }

        private void OnOutputLine(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            var handler = MessageReceived;
            if (handler != null)
                handler(this, new McpMessageEventArgs { Json = e.Data });
        }

        private void OnErrorLine(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            var handler = StderrLine;
            if (handler != null)
                handler(this, new McpMessageEventArgs { Json = e.Data });
        }

        internal static ProcessStartInfo BuildStartInfo(McpServerConfig config)
        {
            string fileName;
            var args = new List<string>(config.Args ?? new List<string>());

            var command = (config.Command ?? "").Trim();
            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

            // On Windows, "npx"/"npm" are .cmd shims and cannot be spawned directly; run them
            // through cmd.exe, mirroring the documented "command": "cmd", "args": ["/c", "npx", …].
            bool needsCmd = isWindows &&
                (string.Equals(command, "npx", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(command, "npm", StringComparison.OrdinalIgnoreCase) ||
                 command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                 command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

            if (needsCmd)
            {
                fileName = "cmd.exe";
                args.Insert(0, command);
                args.Insert(0, "/c");
            }
            else
            {
                fileName = command;
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = BuildArguments(args),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var kvp in config.Env ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value ?? "";
            }

            return psi;
        }

        private static string BuildArguments(IEnumerable<string> args)
        {
            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            return string.Join(" ", args.Select(a => isWindows ? QuoteWindowsArg(a) : QuotePosixArg(a)));
        }

        private static string QuoteWindowsArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        private static string QuotePosixArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '\'', '"' }) < 0) return arg;
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
    }
}

