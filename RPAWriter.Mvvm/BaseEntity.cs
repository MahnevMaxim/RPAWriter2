using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RPAWriter.Mvvm
{
    /// <summary>
    /// Base class for view models. Adds <see cref="INotifyPropertyChanged"/> so that
    /// WPF bindings refresh when a view model or a model raises a change.
    /// </summary>
    public abstract class BaseEntity : INotifyPropertyChanged
    {
        /// <summary><c>true</c> when nobody listens to <see cref="PropertyChanged"/>.</summary>
        public bool IsPropertyChangedNull => PropertyChanged == null;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        /// <summary>
        /// Assigns <paramref name="value"/> to <paramref name="storage"/> and raises
        /// <see cref="PropertyChanged"/> when the value actually changed.
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
    }
}
