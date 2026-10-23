using System.Windows;
using System.Windows.Input;
using AppBlocker.Core.Security;

namespace AppBlocker.UI.Views
{
    public partial class PinPromptDialog : Window
    {
        private readonly IPinSecurityService _pinService;

        public PinPromptDialog(IPinSecurityService pinService, string promptText)
        {
            InitializeComponent();
            _pinService = pinService;
            TxtPrompt.Text = promptText;
            Loaded += (s, e) => PbPin.Focus();
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            await ValidateAndSubmitAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void PbPin_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ValidateAndSubmitAsync();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private async System.Threading.Tasks.Task ValidateAndSubmitAsync()
        {
            var pin = PbPin.Password?.Trim();
            if (string.IsNullOrEmpty(pin))
            {
                TxtError.Text = "Please enter your PIN.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            TxtError.Visibility = Visibility.Collapsed;
            var isValid = await _pinService.ValidateMasterPinAsync(pin);
            if (isValid)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                TxtError.Text = "Incorrect PIN. Authorization denied.";
                TxtError.Visibility = Visibility.Visible;
                PbPin.Password = string.Empty;
                PbPin.Focus();
            }
        }
    }
}
