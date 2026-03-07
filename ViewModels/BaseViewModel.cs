using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BmsLightBridge.ViewModels
{
    // =========================================================================
    // VIEWMODEL BASE CLASSES
    // =========================================================================
    // WPF uses the MVVM (Model-View-ViewModel) pattern.
    // - Model:     the data (Models folder)
    // - View:      the UI (XAML files)
    // - ViewModel: the logic connecting View and Model
    //
    // BaseViewModel implements INotifyPropertyChanged so the UI
    // updates automatically whenever a property value changes.
    // =========================================================================

    /// <summary>
    /// Base class for all ViewModels.
    /// Implements INotifyPropertyChanged for automatic UI updates.
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Call this in a property setter to notify the UI.
        /// [CallerMemberName] automatically fills in the calling property's name.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Helper: stores the value and notifies the UI if it changed.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    // =========================================================================
    // RELAY COMMAND
    // =========================================================================
    // WPF buttons bind to ICommand. RelayCommand makes it easy to wire up
    // methods to buttons from within the ViewModel.
    // =========================================================================

    /// <summary>
    /// Simple ICommand implementation for use in ViewModels.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        // Shortcut constructor for parameter-less actions
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter)     => _execute(parameter);
    }

    /// <summary>
    /// Typed RelayCommand for commands with a strongly-typed parameter.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
        public void Execute(object? parameter)     => _execute((T?)parameter);
    }
}
