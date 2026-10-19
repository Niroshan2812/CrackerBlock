using System.Threading.Tasks;

namespace AppBlocker.Core.Security
{
    public interface IPinSecurityService
    {
        string HashPin(string pin);
        bool VerifyPin(string pin, string hash);
        Task<bool> HasMasterPinAsync();
        Task<bool> SetMasterPinAsync(string newPin);
        Task<bool> ValidateMasterPinAsync(string enteredPin);
        Task<bool> ChangeMasterPinAsync(string currentPin, string newPin);
    }
}
