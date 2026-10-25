using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AppBlocker.UI.ViewModels;

namespace AppBlocker.UI.Views
{
    public partial class LockScreenView : UserControl
    {
        private bool _isUpdating;

        public LockScreenView()
        {
            InitializeComponent();

            MasterPasswordBox.PasswordChanged += (s, e) =>
            {
                if (_isUpdating || DataContext is not LockScreenViewModel vm) return;
                _isUpdating = true;
                vm.PinInput = MasterPasswordBox.Password;
                _isUpdating = false;
            };

            ConfirmPasswordBox.PasswordChanged += (s, e) =>
            {
                if (_isUpdating || DataContext is not LockScreenViewModel vm) return;
                _isUpdating = true;
                vm.ConfirmPinInput = ConfirmPasswordBox.Password;
                _isUpdating = false;
            };

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is LockScreenViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is LockScreenViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateBoxes(newVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isUpdating || sender is not LockScreenViewModel vm) return;

            if (e.PropertyName == nameof(LockScreenViewModel.PinInput))
            {
                _isUpdating = true;
                if (MasterPasswordBox.Password != vm.PinInput)
                {
                    MasterPasswordBox.Password = vm.PinInput;
                }
                _isUpdating = false;
            }
            else if (e.PropertyName == nameof(LockScreenViewModel.ConfirmPinInput))
            {
                _isUpdating = true;
                if (ConfirmPasswordBox.Password != vm.ConfirmPinInput)
                {
                    ConfirmPasswordBox.Password = vm.ConfirmPinInput;
                }
                _isUpdating = false;
            }
        }

        private void UpdateBoxes(LockScreenViewModel vm)
        {
            _isUpdating = true;
            MasterPasswordBox.Password = vm.PinInput;
            ConfirmPasswordBox.Password = vm.ConfirmPinInput;
            _isUpdating = false;
        }
    }
}
