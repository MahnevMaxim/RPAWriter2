using Agent.Rpa.Services;
using Agent.Rpa.Models;

namespace Agent.Rpa.Contracts
{
    /// <summary>Persistence seam for the RPA-domain settings.</summary>
    public interface IRpaSettingsStore
    {
        RpaSettingsData Load();

        void Save(RpaSettingsData settings);
    }
}
