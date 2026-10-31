using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RPAWriter.Mvvm
{
    /// <summary>
    /// Base class for every model of the application.
    /// <para>
    /// A model owns state and business logic. It must be usable headless: it is created,
    /// exercised and asserted without a <see cref="ViewModel"/> and without a view.
    /// It therefore must not touch WPF, <c>Application.Current</c>, <c>Dispatcher</c>,
    /// visual studio services or any static UI state. Everything external is injected
    /// through the constructor as an interface.
    /// </para>
    /// Rules: <c>documentation/13-model-layer.md</c>.
    /// </summary>
    public abstract class BaseModel : INotifyPropertyChanged
    {
        /// <summary>
        /// View model attached to this model, or <c>null</c> when the model runs headless
        /// (unit tests, background work). Never assume it is set.
        /// </summary>
        [JsonIgnore]
        public BaseViewModel ViewModel { get; set; }

        /// <summary><c>true</c> when nobody listens to <see cref="PropertyChanged"/> (useful in hot paths).</summary>
        public bool IsPropertyChangedNull => PropertyChanged == null;

        private delegate void MethodContainer();

        #region Events

        /// <summary>Raised when the model wants a modal message for the user. The view renders it.</summary>
        public event EventHandler<string> ShowDialog;

        /// <summary>Raised when the model wants a non-modal notification. The view renders it.</summary>
        public event EventHandler<Notify> Notify;

        /// <summary>Asks the view to show a modal message.</summary>
        public void ShowDialogMessage(string message)
        {
            ShowDialog?.Invoke(this, message);
        }

        /// <summary>Asks the view to show a non-modal notification.</summary>
        public void NotifyMessage(Notify notify)
        {
            Notify?.Invoke(this, notify);
        }

        #endregion Events

        #region Lifecycle

        /// <summary>
        /// Startup. Invoked before any view model or view is attached, so it must stay UI-free.
        /// </summary>
        public virtual void Run() { }

        /// <summary>
        /// Invoked right after the view model has been assigned and the data context is set
        /// (bindings can already consume collections populated here).
        /// </summary>
        public virtual void AfterLoaded() { }

        /// <summary>Invoked when the view reports it is loaded and visible.</summary>
        public virtual void Loaded() { }

        /// <summary>
        /// Invoked when the view is closing or unloading. The last synchronous point
        /// to flush persisted state.
        /// </summary>
        public virtual void Closing() { }

        #endregion Lifecycle

        /// <summary>
        /// Command dispatcher used by <see cref="BaseViewModel.Cmd"/>: XAML passes a method name
        /// (or a <c>List&lt;object&gt;</c> whose first item is a method name) and the method is
        /// resolved by reflection on the concrete model type.
        /// The target method must be public and must not be overloaded.
        /// </summary>
        public void SendCommand(object parameters)
        {
            MethodContainer mc;
            if (parameters is string)
            {
                mc = () => GetType().GetMethod(parameters.ToString())?.Invoke(this, null);
            }
            else
            {
                var list = (List<object>)parameters;
                switch (list.Count)
                {
                    case 2:
                        mc = () => GetType().GetMethod(list[0] as string)?.Invoke(this, new object[] { list[1] });
                        break;
                    case 3:
                        mc = () => GetType().GetMethod(list[0] as string)?.Invoke(this, new object[] { list[1], list[2] });
                        break;
                    case 4:
                        mc = () => GetType().GetMethod(list[0] as string)?.Invoke(this, new object[] { list[1], list[2], list[3] });
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            mc?.Invoke();
        }

        /// <summary>
        /// Assigns <paramref name="value"/> to <paramref name="storage"/> and raises
        /// <see cref="PropertyChanged"/> (also forwarded to <see cref="ViewModel"/> when attached).
        /// </summary>
        /// <returns><c>true</c> when the value changed.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #region INotifyPropertyChanged code

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
            ViewModel?.OnPropertyChanged(prop);
        }

        #endregion
    }

    /// <summary>Payload of the <see cref="BaseModel.Notify"/> event.</summary>
    public class Notify
    {
        public string Message { get; set; }

        public NotifyLevel NotifyLevel { get; set; }
    }
}
