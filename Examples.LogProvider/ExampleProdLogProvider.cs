using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace Examples.LogProvider
{
    /// <summary>
    /// Example implementation of <see cref="IProdLogProvider"/> for a third-party
    /// production-log library. It returns a deterministic, generated dataset of 50 robot
    /// tasks running between 2026-01-01 and 2026-12-28, each carrying 10 to 200 structured
    /// log entries so the RPAWriter2 "Analyze logs" UI can be exercised end-to-end without
    /// a real orchestrator.
    ///
    /// To use it: build this project and copy the resulting Examples.LogProvider.dll
    /// into the folder configured as "Log providers directory" (default
    /// %AppData%\RPAWriter2\<17|18>\Providers). The extension discovers it via reflection.
    /// </summary>
    public class ExampleProdLogProvider : IProdLogProvider
    {
        public string Name => "Example production logs";

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectiveQuery = query ?? new ProdLogQuery();
            var tasks = BuildTasks(robotName ?? "")
                .Where(t => MatchesRunDate(t, effectiveQuery))
                .ToList();
            return Task.FromResult<IReadOnlyList<ProdTask>>(tasks);
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task == null)
                return Task.FromResult<IReadOnlyList<IProdLog>>(new List<IProdLog>());

            var effectiveQuery = query ?? new ProdLogQuery();
            var logs = BuildLogs(task)
                .Where(l => !effectiveQuery.MinLevel.HasValue || l.Level >= effectiveQuery.MinLevel.Value)
                .ToList();
            return Task.FromResult<IReadOnlyList<IProdLog>>(logs);
        }

        private static bool MatchesRunDate(ProdTask task, ProdLogQuery query)
        {
            if (query.FromUtc.HasValue && (task.StartedAtUtc == null || task.StartedAtUtc.Value < query.FromUtc.Value))
                return false;
            if (query.ToUtc.HasValue && (task.StartedAtUtc == null || task.StartedAtUtc.Value > query.ToUtc.Value))
                return false;
            return true;
        }

        private static readonly DateTime YearStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime YearEndUtc = new DateTime(2026, 12, 28, 23, 59, 59, DateTimeKind.Utc);

        private const int TaskCount = 50;
        private const int MinLogsPerTask = 10;
        private const int MaxLogsPerTask = 200;

        private static readonly string[] TaskNames =
        {
            "Process incoming invoices",
            "Export daily sales report",
            "Reconcile SAP master data",
            "Upload payments to bank",
            "Sync customer addresses",
            "Generate month-end closing report",
            "Update inventory levels",
            "Archive closed orders",
            "Post vendor payments",
            "Refresh customer credit limits",
            "Import purchase orders",
            "Validate open sales orders",
            "Release blocked invoices",
            "Settle intercompany payments",
            "Update material master data",
            "Extract monthly tax report",
            "Correct customer master data",
            "Reverse duplicate payments",
            "Close delivered orders",
            "Approve pending purchase orders",
            "Reconcile bank statements",
            "Update customer price lists",
            "Monitor open invoice aging",
            "Post goods receipts",
            "Generate weekly KPI report"
        };

        private static readonly string[] FailureNotes =
        {
            "SAP connection timeout",
            "Missing report template file",
            "Unhandled exception while processing",
            "Selector not found on screen",
            "Queue item validation failed",
            "Out of memory while loading table"
        };

        private static readonly string[] SapTables = { "VBAK", "VBAP", "MARA", "MAKT", "LFA1", "KNA1", "BSEG", "BKPF" };
        private static readonly string[] Materials = { "000000000000123456", "000000000000654321", "000000000000111222", "000000000000333444" };
        private static readonly string[] Fields = { "VBAP-NETWR", "VBAK-VBELN", "MARA-MATNR", "KNA1-KUNNR", "BSEG-WRBTR" };
        private static readonly string[] Targets = { "btn_Save", "input_InvoiceNo", "grid_Rows", "tab_Details" };

        private static IReadOnlyList<ProdTask> BuildTasks(string robotName)
        {
            var rng = new Random(20260101);
            var spanTicks = (YearEndUtc - YearStartUtc).Ticks;
            var stepTicks = spanTicks / (TaskCount - 1);

            var tasks = new List<ProdTask>(TaskCount);
            for (var i = 0; i < TaskCount; i++)
            {
                // Spread the 50 runs evenly across the year, then add deterministic jitter.
                var baseTicks = YearStartUtc.Ticks + stepTicks * i;
                var jitter = (long)(rng.NextDouble() * stepTicks) - (stepTicks / 2);
                var startTicks = Math.Max(YearStartUtc.Ticks, Math.Min(YearEndUtc.Ticks, baseTicks + jitter));
                var started = new DateTime(startTicks, DateTimeKind.Utc);

                var status = ResolveStatus(i);
                DateTime? finished = null;
                string notes = null;

                if (status != "Running")
                {
                    var finishedUtc = started.AddMinutes(rng.Next(5, 361));
                    finished = finishedUtc > YearEndUtc ? YearEndUtc : finishedUtc;
                    if (status == "Failed")
                        notes = FailureNotes[rng.Next(FailureNotes.Length)];
                }
                else
                {
                    notes = "Still running";
                }

                tasks.Add(new ProdTask
                {
                    TaskId = "T-" + (1001 + i),
                    Name = TaskNames[i % TaskNames.Length],
                    RobotName = robotName,
                    Status = status,
                    StartedAtUtc = started,
                    FinishedAtUtc = finished,
                    Notes = notes
                });
            }

            return tasks;
        }

        private static string ResolveStatus(int index)
        {
            if (index == TaskCount - 1)
                return "Running";
            if (index % 7 == 3)
                return "Failed";
            return "Completed";
        }

        private static IReadOnlyList<IProdLog> BuildLogs(ProdTask task)
        {
            var logs = new List<IProdLog>();
            var rng = new Random(StableHash(task.TaskId ?? "task"));
            var count = rng.Next(MinLogsPerTask, MaxLogsPerTask + 1);

            var started = task.StartedAtUtc ?? DateTime.UtcNow.AddMinutes(-30);
            var finished = task.FinishedAtUtc ?? started.AddHours(2);
            if (finished <= started)
                finished = started.AddMinutes(5);

            var span = finished - started;
            var transaction = PickTransaction(task);
            var robot = string.IsNullOrEmpty(task.RobotName) ? "robot" : task.RobotName;
            var isFailed = string.Equals(task.Status, "Failed", StringComparison.OrdinalIgnoreCase);

            logs.Add(Entry(started, LogLevel.Info, "Orchestrator", "Queue item received for task '" + task.Name + "'."));
            logs.Add(Entry(TimestampAt(started, span, 1, count, rng), LogLevel.Info, "Orchestrator", "Robot '" + robot + "' started."));

            // Middle entries sit between the fixed opening and closing lines.
            var middleCount = Math.Max(0, count - 3);
            for (var i = 0; i < middleCount; i++)
            {
                var timestamp = TimestampAt(started, span, i + 2, count, rng);
                logs.Add(BuildProcessingEntry(rng, timestamp, task, transaction, isFailed));
            }

            logs.Add(BuildClosingEntry(rng, TimestampAt(started, span, count - 1, count, rng), task, isFailed));
            return logs;
        }

        /// <summary>Places a log entry at the given fraction of the task span with a small deterministic jitter.</summary>
        private static DateTime TimestampAt(DateTime started, TimeSpan span, int index, int total, Random rng)
        {
            if (total <= 1)
                return started;

            var stepTicks = span.Ticks / (double)(total - 1);
            var baseTicks = started.Ticks + (long)(stepTicks * index);
            var halfStep = stepTicks / 2;
            var jitter = halfStep <= 0 ? 0 : (long)(rng.NextDouble() * halfStep * 2) - (long)halfStep;
            var ticks = Math.Max(started.Ticks, Math.Min(started.Ticks + span.Ticks, baseTicks + jitter));
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static IProdLog BuildProcessingEntry(Random rng, DateTime timestamp, ProdTask task, string transaction, bool isFailed)
        {
            switch (PickLevel(rng, isFailed))
            {
                case LogLevel.Trace:
                    return BuildTraceEntry(rng, timestamp);
                case LogLevel.Debug:
                    return BuildDebugEntry(rng, timestamp);
                case LogLevel.Warning:
                    return BuildWarningEntry(rng, timestamp);
                case LogLevel.Error:
                    return BuildErrorEntry(rng, timestamp, transaction);
                default:
                    return BuildInfoEntry(rng, timestamp, task, transaction);
            }
        }

        private static IProdLog BuildClosingEntry(Random rng, DateTime timestamp, ProdTask task, bool isFailed)
        {
            if (isFailed)
                return Entry(timestamp, LogLevel.Fatal, "Orchestrator", "Robot stopped: " + (task.Notes ?? "unhandled error") + ".");
            if (string.Equals(task.Status, "Running", StringComparison.OrdinalIgnoreCase))
                return Entry(timestamp, LogLevel.Info, "Orchestrator", "Still running... " + rng.Next(1000, 99999) + " rows processed so far.");
            return Entry(timestamp, LogLevel.Info, "Orchestrator", "Task finished successfully.", Parameters("Rows", rng.Next(100, 99999).ToString()));
        }

        private static LogLevel PickLevel(Random rng, bool isFailed)
        {
            var roll = rng.Next(100);
            if (isFailed)
            {
                if (roll < 45) return LogLevel.Info;
                if (roll < 65) return LogLevel.Debug;
                if (roll < 75) return LogLevel.Trace;
                if (roll < 90) return LogLevel.Warning;
                return LogLevel.Error;
            }

            if (roll < 55) return LogLevel.Info;
            if (roll < 72) return LogLevel.Debug;
            if (roll < 84) return LogLevel.Trace;
            if (roll < 96) return LogLevel.Warning;
            return LogLevel.Error;
        }

        private static IProdLog BuildInfoEntry(Random rng, DateTime timestamp, ProdTask task, string transaction)
        {
            var row = rng.Next(1, 99999);
            switch (rng.Next(6))
            {
                case 0:
                    return Entry(timestamp, LogLevel.Info, "SAP", "Processing row " + row + " of " + (row + rng.Next(1, 99999)) + ".", Parameters("Row", row.ToString()));
                case 1:
                    return Entry(timestamp, LogLevel.Info, "SAP", "Transaction " + transaction + " launched.", Parameters("Transaction", transaction));
                case 2:
                {
                    var table = SapTables[rng.Next(SapTables.Length)];
                    return Entry(timestamp, LogLevel.Info, "SAP", "Reading table " + table + ".", Parameters("Table", table));
                }
                case 3:
                    return Entry(timestamp, LogLevel.Info, "Core", "Validating results against template.");
                case 4:
                    return Entry(timestamp, LogLevel.Info, "Core", "Batch " + rng.Next(1, 1000) + " completed.");
                default:
                    return Entry(timestamp, LogLevel.Info, "Orchestrator", "Task '" + task.Name + "' heartbeat: " + row + " rows processed.");
            }
        }

        private static IProdLog BuildDebugEntry(Random rng, DateTime timestamp)
        {
            switch (rng.Next(4))
            {
                case 0:
                    return Entry(timestamp, LogLevel.Debug, "Core", "Loaded configuration (environment=PROD, retries=3).");
                case 1:
                    return Entry(timestamp, LogLevel.Debug, "UiPath", "Resolved selector for '" + Targets[rng.Next(Targets.Length)] + "'.");
                case 2:
                    return Entry(timestamp, LogLevel.Debug, "Core", "Cache hit for key 'task-" + rng.Next(1000, 9999) + "'.");
                default:
                    return Entry(timestamp, LogLevel.Debug, "Core", "Building request payload for batch " + rng.Next(1, 100) + ".");
            }
        }

        private static IProdLog BuildTraceEntry(Random rng, DateTime timestamp)
        {
            switch (rng.Next(4))
            {
                case 0:
                    return Entry(timestamp, LogLevel.Trace, "SAP", "Row " + rng.Next(1, 99999) + " -> material " + Materials[rng.Next(Materials.Length)] + ".");
                case 1:
                    return Entry(timestamp, LogLevel.Trace, "Core", "HTTP GET /api/items?page=" + rng.Next(1, 50) + " -> 200 OK.");
                case 2:
                    return Entry(timestamp, LogLevel.Trace, "SAP", "Field " + Fields[rng.Next(Fields.Length)] + " = '" + rng.Next(1, 999999) + "'.");
                default:
                    return Entry(timestamp, LogLevel.Trace, "UiPath", "Entering method ProcessRow" + rng.Next(1, 9) + ".");
            }
        }

        private static IProdLog BuildWarningEntry(Random rng, DateTime timestamp)
        {
            switch (rng.Next(4))
            {
                case 0:
                    return Entry(timestamp, LogLevel.Warning, "SAP", "Field " + Fields[rng.Next(Fields.Length)] + " is empty, using fallback value 0.");
                case 1:
                    return Entry(timestamp, LogLevel.Warning, "Database", "Slow query detected (" + rng.Next(500, 5000) + " ms).");
                case 2:
                    return Entry(timestamp, LogLevel.Warning, "Core", "Retry " + rng.Next(1, 3) + " of 3 failed: transport error.");
                default:
                    return Entry(timestamp, LogLevel.Warning, "UiPath", "Deprecated API 'GetText' in use.");
            }
        }

        private static IProdLog BuildErrorEntry(Random rng, DateTime timestamp, string transaction)
        {
            switch (rng.Next(5))
            {
                case 0:
                    return Entry(timestamp, LogLevel.Error, "SAP", "Connection reset while reading table " + SapTables[rng.Next(SapTables.Length)] + ".");
                case 1:
                    return Entry(timestamp, LogLevel.Error, "UiPath", "Timeout waiting for selector '" + Targets[rng.Next(Targets.Length)] + "'.");
                case 2:
                    return Entry(timestamp, LogLevel.Error, "Core", "Validation failed for " + Fields[rng.Next(Fields.Length)] + ".");
                case 3:
                    return Entry(timestamp, LogLevel.Error, "SAP", "Transaction " + transaction + " aborted with code " + rng.Next(100, 999) + ".");
                default:
                    return Entry(timestamp, LogLevel.Error, "Core", "Unable to parse '" + rng.Next(1000, 99999) + "' as decimal.");
            }
        }

        private static IReadOnlyList<LogParameter> Parameters(params string[] keyValues)
        {
            var list = new List<LogParameter>();
            for (var i = 0; i + 1 < keyValues.Length; i += 2)
                list.Add(new LogParameter { Key = keyValues[i], Value = keyValues[i + 1] });
            return list;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in value)
                    hash = hash * 31 + c;
                return hash;
            }
        }

        private static IProdLog Entry(DateTime timestamp, LogLevel level, string source, string message, IReadOnlyList<LogParameter> parameters = null)
        {
            return new ExampleLogEntry(timestamp, level, source, message, parameters);
        }

        private static string PickTransaction(ProdTask task)
        {
            var name = (task.Name ?? "").ToLowerInvariant();
            if (name.Contains("invoice")) return "VF01";
            if (name.Contains("report")) return "SE16N";
            if (name.Contains("payment") || name.Contains("bank")) return "F110";
            if (name.Contains("inventory") || name.Contains("master data")) return "MM01";
            if (name.Contains("customer")) return "XD02";
            if (name.Contains("order")) return "VA02";
            return "VA01";
        }

        private sealed class ExampleLogEntry : IProdLog
        {
            public ExampleLogEntry(DateTime timestamp, LogLevel level, string source, string message, IReadOnlyList<LogParameter> parameters = null)
            {
                Timestamp = timestamp;
                Level = level;
                Source = source;
                Message = message;
                RawLine = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + source + ": " + message;
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
