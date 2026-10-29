using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Version-control of workspace files via an isolated shadow git repository.</summary>
    public interface ICheckpointService : IDisposable
    {
        bool IsInitialized { get; }

        IReadOnlyList<Checkpoint> Checkpoints { get; }

        Task<InitResult> InitializeAsync(CancellationToken ct = default);

        Task<CheckpointSaveResult> SaveAsync(string message, bool allowEmpty, CancellationToken ct = default);

        Task RestoreAsync(string commitHash, CancellationToken ct = default);

        Task<IReadOnlyList<CheckpointDiff>> GetDiffAsync(string from, string to, CancellationToken ct = default);

        Task<IReadOnlyList<Checkpoint>> GetCheckpointsAsync(CancellationToken ct = default);

        Task DeleteAsync(CancellationToken ct = default);
    }
}
