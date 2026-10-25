using System.Collections.Generic;
using AppBlocker.UI.Services;
using Xunit;

namespace AppBlocker.Tests
{
    public class LocalizationManagerTests
    {
        [Fact]
        public void LocalizationManager_CanToggleBetweenEnglishAndSinhala()
        {
            var loc = LocalizationManager.Instance;

            // Set to English
            loc.SetLanguage(AppLanguage.English);
            Assert.True(loc.IsEnglish);
            Assert.False(loc.IsSinhala);
            Assert.Equal(AppLanguage.English, loc.CurrentLanguage);
            Assert.Equal("Applications", loc["Nav_Applications"]);

            // Toggle to Sinhala
            loc.ToggleLanguage();
            Assert.False(loc.IsEnglish);
            Assert.True(loc.IsSinhala);
            Assert.Equal(AppLanguage.Sinhala, loc.CurrentLanguage);
            Assert.Equal("යෙදුම්", loc["Nav_Applications"]);

            // Toggle back to English
            loc.ToggleLanguage();
            Assert.True(loc.IsEnglish);
            Assert.False(loc.IsSinhala);
            Assert.Equal(AppLanguage.English, loc.CurrentLanguage);
            Assert.Equal("Applications", loc["Nav_Applications"]);
        }

        [Fact]
        public void LocalizationManager_NotifiesPropertyChangedOnLanguageChange()
        {
            var loc = LocalizationManager.Instance;
            var changedProperties = new List<string>();
            loc.PropertyChanged += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.PropertyName))
                {
                    changedProperties.Add(args.PropertyName);
                }
            };

            loc.SetLanguage(AppLanguage.Sinhala);

            Assert.Contains("Item[]", changedProperties);
            Assert.Contains(nameof(LocalizationManager.IsEnglish), changedProperties);
            Assert.Contains(nameof(LocalizationManager.IsSinhala), changedProperties);

            // Reset back to English
            loc.SetLanguage(AppLanguage.English);
        }

        [Fact]
        public void LocalizationManager_ReturnsFallbackWhenKeyMissing()
        {
            var loc = LocalizationManager.Instance;
            loc.SetLanguage(AppLanguage.English);

            var result = loc["NonExistent_Test_Key_1234"];
            Assert.Equal("NonExistent_Test_Key_1234", result);
        }

        [Fact]
        public void LocalizationManager_HasAllKeySectionsCoveredInBothLanguages()
        {
            var loc = LocalizationManager.Instance;

            // English check
            loc.SetLanguage(AppLanguage.English);
            Assert.Equal("Applications", loc["Nav_Applications"]);
            Assert.Equal("Websites", loc["Nav_Websites"]);
            Assert.Equal("Schedules & Focus", loc["Nav_Schedules"]);
            Assert.Equal("Settings & Defense", loc["Nav_Settings"]);
            Assert.Equal("Enter Master PIN", loc["Lock_EnterPinTitle"]);
            Assert.Equal("Managed Restricted Applications", loc["Apps_ManagedRules"]);
            Assert.Equal("Active Blocked Domains & Redirects", loc["Web_ActiveRules"]);
            Assert.Equal("Custom Time Windows", loc["Sched_CustomWindows"]);
            Assert.Equal("Safe Child Shield (Parental Protection)", loc["SafeChild_Title"]);

            // Sinhala check
            loc.SetLanguage(AppLanguage.Sinhala);
            Assert.Equal("යෙදුම්", loc["Nav_Applications"]);
            Assert.Equal("වෙබ් අඩවි", loc["Nav_Websites"]);
            Assert.Equal("කාලසටහන් සහ අවධානය", loc["Nav_Schedules"]);
            Assert.Equal("සැකසුම් සහ ආරක්ෂාව", loc["Nav_Settings"]);
            Assert.Equal("ප්‍රධාන PIN අංකය ඇතුළත් කරන්න", loc["Lock_EnterPinTitle"]);
            Assert.Equal("කළමනාකරණය කරන සීමිත යෙදුම්", loc["Apps_ManagedRules"]);
            Assert.Equal("අවහිර කළ වෙබ් අඩවි සහ යළි-යොමුකිරීම්", loc["Web_ActiveRules"]);
            Assert.Equal("අභිරුචි කාල පරාසයන්", loc["Sched_CustomWindows"]);
            Assert.Equal("ළමා ආරක්ෂණ පලිහ (Safe Child Shield)", loc["SafeChild_Title"]);

            // Reset
            loc.SetLanguage(AppLanguage.English);
        }
    }
}
