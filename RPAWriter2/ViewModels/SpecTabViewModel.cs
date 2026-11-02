using RPAWriter.Models;
using RPAWriter.Mvvm;

namespace RPAWriter2.ViewModels
{
    /// <summary>
    /// View model of the Spec tab: a thin adapter between <see cref="SpecTabModel"/> and the XAML.
    /// It holds no business logic — commands are forwarded to the model and bindings read the model
    /// through <see cref="Spec"/>.
    /// </summary>
    public sealed class SpecTabViewModel : BaseViewModel
    {
        /// <summary>The spec model this view model adapts.</summary>
        public SpecTabModel Spec => Model as SpecTabModel;
    }
}
