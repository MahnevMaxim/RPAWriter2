using System;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="IUiStateStore"/>: remembers the last saved state and can be told to fail
    /// on load (the model must then fall back to defaults).
    /// </summary>
    public sealed class InMemoryUiStateStore : IUiStateStore
    {
        public UiStateData State { get; private set; } = new UiStateData();

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public bool ThrowOnLoad { get; set; }

        public UiStateData Load()
        {
            if (ThrowOnLoad)
                throw new InvalidOperationException("ui state unavailable");

            LoadCount++;
            return State;
        }

        public void Save(UiStateData state)
        {
            SaveCount++;
            State = state;
        }
    }
}
