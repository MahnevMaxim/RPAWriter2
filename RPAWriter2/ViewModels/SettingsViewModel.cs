using Agent.Models;
using Agent.Mvvm;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2.ViewModels
{
    /// <summary>
    /// View model of the Settings tab: a thin adapter between <see cref="SettingsModel"/> and the
    /// XAML. It holds no business logic — commands are forwarded to the model and bindings read the
    /// model through <see cref="Settings"/>.
    /// </summary>
    public sealed class SettingsViewModel : BaseViewModel
    {
        /// <summary>The settings model this view model adapts.</summary>
        public SettingsModel Settings => Model as SettingsModel;

        /// <summary>The RPA-domain settings model this view model adapts (assigned by the host before attach).</summary>
        public RpaSettingsModel RpaSettings { get; set; }
    }
}
