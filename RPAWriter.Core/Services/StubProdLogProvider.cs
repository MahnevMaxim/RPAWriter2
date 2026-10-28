using System;
using System.Collections.Generic;
using System.Linq;
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

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            var effectiveQuery = query ?? new ProdLogQuery();
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

            var filtered = tasks.Where(t => MatchesRunDate(t, effectiveQuery)).ToList();
            return Task.FromResult<IReadOnlyList<ProdTask>>(filtered);
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            var effectiveQuery = query ?? new ProdLogQuery();
            IReadOnlyList<IProdLog> logs = new IProdLog[]
            {
                new StubLogEntry(now.AddMinutes(-5), LogLevel.Info, "Orchestrator", "Queue item received.", "[INFO] Orchestrator: Queue item received."),
                new StubLogEntry(now.AddMinutes(-4), LogLevel.Info, "SAP", "Transaction VA01 started.", null, new[] { new LogParameter { Key = "Transaction", Value = "VA01" } }),
                new StubLogEntry(now.AddMinutes(-3), LogLevel.Warning, "SAP", "Field VBAP-NETWR not found, using default.", "[WARNING] SAP: Field VBAP-NETWR not found, using default."),
                new StubLogEntry(now.AddMinutes(-2), LogLevel.Error, "SAP", "Connection reset while reading table.", "[ERROR] SAP: Connection reset while reading table.", new[] { new LogParameter { Key = "Table", Value = "VBAK" } }),
                new StubLogEntry(now.AddMinutes(-1), LogLevel.Fatal, "Orchestrator", "Robot stopped unexpectedly.", "[FATAL] Orchestrator: Robot stopped unexpectedly.")
            };

            var filtered = logs.Where(l => MatchesLevel(l, effectiveQuery)).ToList();
            return Task.FromResult<IReadOnlyList<IProdLog>>(filtered);
        }

        private static bool MatchesRunDate(ProdTask task, ProdLogQuery query)
        {
            if (query.FromUtc.HasValue && (task.StartedAtUtc == null || task.StartedAtUtc.Value < query.FromUtc.Value))
                return false;
            if (query.ToUtc.HasValue && (task.StartedAtUtc == null || task.StartedAtUtc.Value > query.ToUtc.Value))
                return false;
            return true;
        }

        private static bool MatchesLevel(IProdLog log, ProdLogQuery query)
        {
            return !query.MinLevel.HasValue || log.Level >= query.MinLevel.Value;
        }

        private sealed class StubLogEntry : IProdLog
        {
            public StubLogEntry(DateTime? timestamp, LogLevel level, string source, string message, string rawLine = null, IReadOnlyList<LogParameter> parameters = null)
            {
                Timestamp = timestamp;
                Level = level;
                Source = source;
                Message = message;
                RawLine = rawLine ?? message;
                Parameters = parameters ?? new List<LogParameter>();
            }

            public DateTime? Timestamp { get; }
            public LogLevel Level { get; }
            public string Source { get; }
            public string Message { get; }
            public string RawLine { get; }
            public IReadOnlyList<LogParameter> Parameters { get; }
        }
    }
}
