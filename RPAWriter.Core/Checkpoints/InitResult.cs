namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Result of initializing the shadow repository.</summary>
    public sealed class InitResult
    {
        public bool Created { get; set; }

        public string BaseHash { get; set; }

        public long DurationMs { get; set; }
    }
}
