using System;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>A single saved workspace snapshot (a commit in the shadow repository).</summary>
    public sealed class Checkpoint
    {
        public string Ref { get; set; }

        public int Sequence { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public string Message { get; set; }
    }
}
