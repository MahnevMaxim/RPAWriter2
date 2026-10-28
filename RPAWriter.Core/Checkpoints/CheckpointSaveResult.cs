namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Result of saving a checkpoint.</summary>
    public sealed class CheckpointSaveResult
    {
        /// <summary>Commit hash, or null when there was nothing to commit and <see cref="IsEmpty"/> is true.</summary>
        public string CommitHash { get; set; }

        /// <summary>True when there were no changes to snapshot and no commit was created.</summary>
        public bool IsEmpty { get; set; }
    }
}
