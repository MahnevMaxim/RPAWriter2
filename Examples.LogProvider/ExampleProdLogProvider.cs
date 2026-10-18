using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace Examples.LogProvider
{
    /// <summary>
    /// Example implementation of <see cref="IProdLogProvider"/> for a third-party
    /// production-log library. It returns a rich, hard-coded dataset of robot tasks
    /// and their structured log entries so the RPAWriter2 "Analyze logs" UI can be
    /// exercised end-to-end without a real orchestrator.
    ///
    /// To use it: build this project and copy the resulting Examples.LogProvider.dll
    /// into the folder configured as "Log providers directory" (default
    /// %AppData%\RPAWriter2\Providers). The extension discovers it via reflection.
    /// </summary>
    public class ExampleProdLogProvider : IProdLogProvider
    {
        public string Name => "Example production logs";

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProdTask>>(BuildTasks(robotName ?? ""));
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task == null)
                return Task.FromResult<IReadOnlyList<IProdLog>>(new List<IProdLog>());
            return Task.FromResult<IReadOnlyList<IProdLog>>(BuildLogs(task));
        }

        private static IReadOnlyList<ProdTask> BuildTasks(string robotName)
        {
            var now = DateTime.UtcNow;
            return new List<ProdTask>
            {
                new ProdTask
                {
                    TaskId = "T-1001", Name = "Process incoming invoices", RobotName = robotName,
                    Status = "Completed", StartedAtUtc = now.AddHours(-6), FinishedAtUtc = now.AddHours(-5)
                },
                new ProdTask
                {
                    TaskId = "T-1002", Name = "Export daily sales report", RobotName = robotName,
                    Status = "Completed", StartedAtUtc = now.AddHours(-5), FinishedAtUtc = now.AddHours(-4),
                    Notes = "Report saved to shared folder"
                },
                new ProdTask
                {
                    TaskId = "T-1003", Name = "Reconcile SAP master data", RobotName = robotName,
                    Status = "Failed", StartedAtUtc = now.AddHours(-4), FinishedAtUtc = now.AddHours(-3),
                    Notes = "SAP connection timeout"
                },
                new ProdTask
                {
                    TaskId = "T-1004", Name = "Upload payments to bank", RobotName = robotName,
                    Status = "Completed", StartedAtUtc = now.AddHours(-3), FinishedAtUtc = now.AddHours(-2)
                },
                new ProdTask
                {
                    TaskId = "T-1005", Name = "Sync customer addresses", RobotName = robotName,
                    Status = "Running", StartedAtUtc = now.AddMinutes(-30)
                },
                new ProdTask
                {
                    TaskId = "T-1006", Name = "Generate month-end closing report", RobotName = robotName,
                    Status = "Failed", StartedAtUtc = now.AddHours(-2), FinishedAtUtc = now.AddHours(-1),
                    Notes = "Missing report template file"
                },
                new ProdTask
                {
                    TaskId = "T-1007", Name = "Update inventory levels", RobotName = robotName,
                    Status = "Completed", StartedAtUtc = now.AddHours(-1), FinishedAtUtc = now.AddMinutes(-40)
                },
                new ProdTask
                {
                    TaskId = "T-1008", Name = "Archive closed orders", RobotName = robotName,
                    Status = "Completed", StartedAtUtc = now.AddMinutes(-50), FinishedAtUtc = now.AddMinutes(-45),
                    Notes = "1,204 orders archived"
                }
            };
        }

        private static IReadOnlyList<IProdLog> BuildLogs(ProdTask task)
        {
            var logs = new List<IProdLog>();
            var t = task.StartedAtUtc ?? DateTime.UtcNow.AddMinutes(-5);

            logs.Add(Entry(t, LogLevel.Info, "Orchestrator", "Queue item received for task '" + task.Name + "'."));
            t = t.AddSeconds(2);
            logs.Add(Entry(t, LogLevel.Info, "Orchestrator", "Robot '" + task.RobotName + "' started."));
            t = t.AddSeconds(3);
            logs.Add(Entry(t, LogLevel.Debug, "Core", "Loaded configuration (environment=PROD, retries=3)."));
            t = t.AddSeconds(5);
            logs.Add(Entry(t, LogLevel.Info, "SAP", "Transaction " + PickTransaction(task) + " launched."));

            if (task.Status == "Failed")
            {
                t = t.AddSeconds(18);
                logs.Add(Entry(t, LogLevel.Warning, "SAP", "Field VBAP-NETWR is empty, using fallback value 0."));
                t = t.AddSeconds(40);
                logs.Add(Entry(t, LogLevel.Error, "SAP", "Connection reset while reading table VBAK."));
                t = t.AddSeconds(5);
                logs.Add(Entry(t, LogLevel.Error, "Core", "Retry 1 of 3 failed: transport error."));
                t = t.AddSeconds(12);
                logs.Add(Entry(t, LogLevel.Fatal, "Orchestrator", "Robot stopped: " + (task.Notes ?? "unhandled error") + "."));
            }
            else if (task.Status == "Running")
            {
                t = t.AddSeconds(30);
                logs.Add(Entry(t, LogLevel.Info, "SAP", "Processing rows: 1234 of 5678."));
                t = t.AddSeconds(10);
                logs.Add(Entry(t, LogLevel.Trace, "SAP", "Row 1234 -> material 000000000000123456."));
                t = t.AddSeconds(15);
                logs.Add(Entry(t, LogLevel.Info, "Core", "Still running..."));
            }
            else
            {
                t = t.AddSeconds(25);
                logs.Add(Entry(t, LogLevel.Info, "SAP", "Processing completed: 5678 rows."));
                t = t.AddSeconds(8);
                logs.Add(Entry(t, LogLevel.Debug, "Core", "Validating results against template."));
                t = t.AddSeconds(10);
                logs.Add(Entry(t, LogLevel.Info, "Orchestrator", "Task finished successfully."));
            }

            return logs;
        }

        private static IProdLog Entry(DateTime timestamp, LogLevel level, string source, string message)
        {
            return new ExampleLogEntry(timestamp, level, source, message);
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
            public ExampleLogEntry(DateTime timestamp, LogLevel level, string source, string message)
            {
                Timestamp = timestamp;
                Level = level;
                Source = source;
                Message = message;
                RawLine = timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + source + ": " + message;
            }

            public DateTime? Timestamp { get; }
            public LogLevel Level { get; }
            public string Source { get; }
            public string Message { get; }
            public string RawLine { get; }
        }
    }
}
