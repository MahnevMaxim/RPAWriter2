using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Offline stub of <see cref="IProdLogProvider"/> used for demos and tests.
    /// Returns a small fixed robot/task/log dataset instead of talking to a real server.
    /// </summary>
    public class StubProdLogProvider : IProdLogProvider
    {
        public string Name => "Stub (offline)";

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            IReadOnlyList<ProdTask> tasks = new[]
            {
                new ProdTask
                {
                    TaskId = "T-1001", Name = "Process invoices", RobotName = robotName ?? "", Status = "Completed",
                    StartedAtUtc = now.AddHours(-3), FinishedAtUtc = now.AddHours(-2)
                },
                new ProdTask
                {
                    TaskId = "T-1002", Name = "Export report", RobotName = robotName ?? "", Status = "Completed",
                    StartedAtUtc = now.AddHours(-2), FinishedAtUtc = now.AddHours(-1)
                },
                new ProdTask
                {
                    TaskId = "T-1003", Name = "Sync master data", RobotName = robotName ?? "", Status = "Failed",
                    StartedAtUtc = now.AddMinutes(-30), FinishedAtUtc = now.AddMinutes(-20), Notes = "Timeout on SAP connection"
                }
            };
            return Task.FromResult(tasks);
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            IReadOnlyList<IProdLog> logs = new IProdLog[]
            {
                new StubLogEntry(now.AddMinutes(-5), LogLevel.Info, "Orchestrator", "Queue item received.", "[INFO] Orchestrator: Queue item received."),
                new StubLogEntry(now.AddMinutes(-4), LogLevel.Info, "SAP", "Transaction VA01 started."),
                new StubLogEntry(now.AddMinutes(-3), LogLevel.Warning, "SAP", "Field VBAP-NETWR not found, using default.", "[WARNING] SAP: Field VBAP-NETWR not found, using default."),
                new StubLogEntry(now.AddMinutes(-2), LogLevel.Error, "SAP", "Connection reset while reading table.", "[ERROR] SAP: Connection reset while reading table."),
                new StubLogEntry(now.AddMinutes(-1), LogLevel.Fatal, "Orchestrator", "Robot stopped unexpectedly.", "[FATAL] Orchestrator: Robot stopped unexpectedly.")
            };
            return Task.FromResult(logs);
        }

        private sealed class StubLogEntry : IProdLog
        {
            public StubLogEntry(DateTime? timestamp, LogLevel level, string source, string message, string rawLine = null)
            {
                Timestamp = timestamp;
                Level = level;
                Source = source;
                Message = message;
                RawLine = rawLine ?? message;
            }

            public DateTime? Timestamp { get; }
            public LogLevel Level { get; }
            public string Source { get; }
            public string Message { get; }
            public string RawLine { get; }
        }
    }
}
