using System;
using System.Windows.Input;

namespace RPAWriter.Mvvm
{
    /// <summary>
    /// Minimal <see cref="ICommand"/> implementation.
    /// <para>
    /// Deliberately does not use <c>CommandManager.RequerySuggested</c> (that type lives in
    /// PresentationCore and would make this assembly WPF-only): WPF still re-queries
    /// <see cref="CanExecute"/> on input events, and property-driven bindings can call
    /// <see cref="RaiseCanExecuteChanged"/> explicitly.
    /// </para>
    /// <see cref="ICommand"/> itself comes from System.ObjectModel and is part of netstandard2.0.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return canExecute == null || canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            execute(parameter);
        }

        /// <summary>Tells WPF to re-evaluate <see cref="CanExecute"/>.</summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
