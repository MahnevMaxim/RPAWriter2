using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Adapts the static <see cref="UiStateStore"/> to <see cref="IUiStateStore"/> so that models
    /// depend on the Core contract instead of the concrete JSON file location.
    /// </summary>
    public sealed class JsonUiStateStore : IUiStateStore
    {
        public UiStateData Load() => UiStateStore.Load();

        public void Save(UiStateData state) => UiStateStore.Save(state);
    }
}
