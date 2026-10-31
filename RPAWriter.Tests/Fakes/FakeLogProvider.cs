using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="IProdLogProvider"/>: returns prepared tasks/logs and records every call,
    /// so the log analysis model can be exercised without a real production server.
    /// </summary>
    public sealed class FakeLogProvider : IProdLogProvider
    {
        public string Name { get; set; } = "Fake provider";

        public List<ProdTask> Tasks { get; } = new List<ProdTask>();

        public List<IProdLog> Logs { get; } = new List<IProdLog>();

        /// <summary>When set, <see cref="GetTasksAsync"/> throws it.</summary>
        public Exception TasksError { get; set; }

        /// <summary>When set, <see cref="GetLogsAsync"/> throws it.</summary>
        public Exception LogsError { get; set; }

        public int TasksCalls { get; private set; }

        public int LogsCalls { get; private set; }

        public string LastRobotName { get; private set; }

        public ProdLogQuery LastTasksQuery { get; private set; }

        public ProdLogQuery LastLogsQuery { get; private set; }

        public ProdTask LastTask { get; private set; }

        public Task<IReadOnlyList<ProdTask>> GetTasksAsync(string robotName, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            TasksCalls++;
            LastRobotName = robotName;
            LastTasksQuery = query;

            if (TasksError != null)
                throw TasksError;

            return Task.FromResult<IReadOnlyList<ProdTask>>(Tasks);
        }

        public Task<IReadOnlyList<IProdLog>> GetLogsAsync(ProdTask task, ProdLogQuery query, CancellationToken cancellationToken = default)
        {
            LogsCalls++;
            LastTask = task;
            LastLogsQuery = query;

            if (LogsError != null)
                throw LogsError;

            return Task.FromResult<IReadOnlyList<IProdLog>>(Logs);
        }
    }

    /// <summary>Structured log entry used by the log analysis tests.</summary>
    public sealed class FakeProdLog : IProdLog
    {
        public DateTime? Timestamp { get; set; }

        public LogLevel Level { get; set; } = LogLevel.Info;

        public string Source { get; set; } = "Robot";

        public string Message { get; set; } = "";

        public string RawLine { get; set; }

        public IReadOnlyList<LogParameter> Parameters { get; set; } = new List<LogParameter>();

        public static FakeProdLog Create(LogLevel level, string message, string source = "Robot", DateTime? timestamp = null)
        {
            return new FakeProdLog { Level = level, Message = message, Source = source, Timestamp = timestamp };
        }
    }
}
