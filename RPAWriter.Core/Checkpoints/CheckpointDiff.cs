namespace RPAWriter.Core.Checkpoints
{
    /// <summary>Before/after content of one file between two checkpoints.</summary>
    public sealed class CheckpointDiff
    {
        public string RelativePath { get; set; }

        public string AbsolutePath { get; set; }

        public string Before { get; set; }

        public string After { get; set; }
    }
}
