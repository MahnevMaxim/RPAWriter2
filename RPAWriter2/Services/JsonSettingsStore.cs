using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Adapts the static <see cref="SettingsStore"/> to <see cref="ISettingsStore"/> so that
    /// models depend on the Core contract instead of the concrete JSON file location.
    /// </summary>
    public sealed class JsonSettingsStore : ISettingsStore
    {
        public SettingsData Load() => SettingsStore.Load();

        public void Save(SettingsData settings) => SettingsStore.Save(settings);
    }
}
