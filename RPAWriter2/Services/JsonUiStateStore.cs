using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

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
