using Newtonsoft.Json;

namespace RPAWriter.Mvvm
{
    /// <summary>
    /// Optional adapter between a model and a view.
    /// A view model only translates model state for XAML and forwards commands;
    /// it must not contain business logic.
    /// </summary>
    public abstract class BaseViewModel : BaseEntity
    {
        [JsonIgnore]
        public BaseModel Model { get; set; }

        [JsonIgnore]
        public IView View { get; set; }

        /// <summary>
        /// Attaches the model to this view model. <paramref name="view"/> may be <c>null</c>
        /// when there is no view (headless tests).
        /// </summary>
        public void SetModel(BaseModel model, IView view)
        {
            View = view;
            Model = model;
            Model.ViewModel = this;
            SendCommand += model.SendCommand;
            InitializeViewModel();
        }

        /// <summary>Hook for extra initialization after the model has been attached.</summary>
        public virtual void InitializeViewModel() { }

        #region Commands

        public delegate void CommandHandler(object parameters);

        public event CommandHandler SendCommand;

        /// <summary>
        /// Command for XAML: <c>Command="{Binding Cmd}" CommandParameter="MethodName"</c>.
        /// The parameter is forwarded to <see cref="BaseModel.SendCommand"/>.
        /// </summary>
        public RelayCommand Cmd => new RelayCommand(obj => SendCommand?.Invoke(obj));

        #endregion
    }
}
