using System;
using System.Collections.Generic;
using RPAWriter.Core.Checkpoints;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services.Checkpoints
{
    /// <summary>
    /// Creates and caches per-task <see cref="ICheckpointService"/> instances backed by a
    /// shadow git repository under the extension's local-app-data directory.
    /// </summary>
    public sealed class CheckpointServiceFactory : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, ICheckpointService> _services =
            new Dictionary<string, ICheckpointService>(StringComparer.OrdinalIgnoreCase);
        private readonly string _stateDir;

        public CheckpointServiceFactory(string stateDir = null)
        {
            _stateDir = stateDir ?? AppDataPaths.ResolveLocal();
        }

        /// <summary>Returns a cached service for the given task + workspace, creating it on first use.</summary>
        public ICheckpointService GetOrCreate(string taskId, string workspaceDir)
        {
            if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("taskId is required.", nameof(taskId));
            if (string.IsNullOrWhiteSpace(workspaceDir)) throw new ArgumentException("workspaceDir is required.", nameof(workspaceDir));

            var key = taskId + "|" + workspaceDir;
            lock (_gate)
            {
                if (_services.TryGetValue(key, out var existing))
                    return existing;

                var service = new CheckpointService(new CheckpointServiceOptions
                {
                    TaskId = taskId,
                    WorkspaceDir = workspaceDir,
                    StateDir = _stateDir,
                    Log = LogService.Info,
                });
                _services[key] = service;
                return service;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var service in _services.Values)
                    service.Dispose();
                _services.Clear();
            }
        }
    }
}
