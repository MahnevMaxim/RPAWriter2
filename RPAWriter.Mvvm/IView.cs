namespace RPAWriter.Mvvm
{
    /// <summary>
    /// A view the model can talk to. Implemented by the WPF layer (windows and control hosts),
    /// never by a model.
    /// </summary>
    public interface IView
    {
        /// <summary>For WPF windows; returns the dialog result.</summary>
        bool? ShowDialog();

        void Show();

        void NotifyMessage(Notify notify);

        bool? ShowDialogMessage(string message);
    }
}
