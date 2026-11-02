using RPAWriter.Models;
using RPAWriter.Mvvm;

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
    }
}
