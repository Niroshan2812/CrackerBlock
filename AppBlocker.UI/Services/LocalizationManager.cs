using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Markup;

namespace AppBlocker.UI.Services
{
    public enum AppLanguage
    {
        English = 0,
        Sinhala = 1
    }

    public class LocalizationManager : INotifyPropertyChanged
    {
        private static readonly Lazy<LocalizationManager> _lazyInstance = new(() => new LocalizationManager());
        public static LocalizationManager Instance => _lazyInstance.Value;

        private AppLanguage _currentLanguage = AppLanguage.English;
        private readonly string _settingsFilePath;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<AppLanguage>? LanguageChanged;

        public AppLanguage CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    SaveLanguagePreference();
                    NotifyLanguageChanged();
                }
            }
        }

        public bool IsEnglish => _currentLanguage == AppLanguage.English;
        public bool IsSinhala => _currentLanguage == AppLanguage.Sinhala;

        public string CurrentLanguageCode => _currentLanguage == AppLanguage.Sinhala ? "si" : "en";
        public string CurrentLanguageDisplay => _currentLanguage == AppLanguage.Sinhala ? "🇱🇰 සිංහල" : "🇺🇸 English";
        public string SwitchLanguageTooltip => _currentLanguage == AppLanguage.Sinhala ? "Switch to English" : "සිංහල භාෂාවට මාරු වන්න";

        /// <summary>
        /// Indexer for WPF dynamic binding: {Binding [Key], Source={x:Static services:LocalizationManager.Instance}}
        /// or via {loc:Loc Key} markup extension.
        /// </summary>
        public string this[string key] => Get(key);

        public LocalizationManager()
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dir = Path.Combine(programData, "AppBlocker");
            _settingsFilePath = Path.Combine(dir, "ui_language.txt");

            LoadLanguagePreference();
        }

        public void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
        }

        public void ToggleLanguage()
        {
            CurrentLanguage = _currentLanguage == AppLanguage.English ? AppLanguage.Sinhala : AppLanguage.English;
        }

        public string Get(string key, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            var dict = _currentLanguage == AppLanguage.Sinhala ? SinhalaTranslations : EnglishTranslations;
            if (!dict.TryGetValue(key, out var translation))
            {
                // Fallback to English if missing in Sinhala
                if (!EnglishTranslations.TryGetValue(key, out translation))
                {
                    translation = key;
                }
            }

            if (args != null && args.Length > 0)
            {
                try
                {
                    return string.Format(translation, args);
                }
                catch
                {
                    return translation;
                }
            }

            return translation;
        }

        private void NotifyLanguageChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageCode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SwitchLanguageTooltip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSinhala)));

            LanguageChanged?.Invoke(_currentLanguage);
        }

        private void LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var text = File.ReadAllText(_settingsFilePath).Trim().ToLowerInvariant();
                    if (text == "si" || text == "sinhala")
                    {
                        _currentLanguage = AppLanguage.Sinhala;
                    }
                    else
                    {
                        _currentLanguage = AppLanguage.English;
                    }
                }
            }
            catch
            {
                _currentLanguage = AppLanguage.English;
            }
        }

        private void SaveLanguagePreference()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_settingsFilePath, _currentLanguage == AppLanguage.Sinhala ? "si" : "en");
            }
            catch
            {
                // Non-critical persistence failure ignored
            }
        }

        #region Translation Dictionaries

        private static readonly Dictionary<string, string> EnglishTranslations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Navigation & Brand
            ["Nav_Applications"] = "Applications",
            ["Nav_Websites"] = "Websites",
            ["Nav_Schedules"] = "Schedules & Focus",
            ["Nav_Logs"] = "Analytics & Logs",
            ["Nav_Settings"] = "Settings & Defense",
            ["Nav_LockDashboard"] = "Lock Dashboard",
            ["Brand_Title"] = "APPBLOCKER",
            ["Brand_Subtitle"] = "Desktop & Web Control",
            ["Status_ServiceActive"] = "System Guardian Active",
            ["Status_ServiceOffline"] = "Daemon Disconnected",
            ["Btn_StartService"] = "Start Protection",
            ["Tip_StartEngine"] = "Click to install and start background protection engine (UAC)",
            ["Tip_ToggleSidebar"] = "Toggle Sidebar (Expand / Collapse)",
            ["Trial_EvaluationBadge"] = "3-Day Evaluation",
            ["Trial_ExpiredTitle"] = "Evaluation Period Expired",
            ["Trial_ExpiredDesc"] = "Your 3-day evaluation testing period for CrackerBlock has concluded. Thank you for participating in user testing. To continue using all features without interruption, please enter a license activation key.",
            ["Trial_DeviceId"] = "Device ID:",
            ["Trial_KeyPrompt"] = "Enter License Activation Key (e.g. CB-TEST-2026-UNLK):",
            ["Trial_ActivateBtn"] = "Activate License",
            ["Trial_ExitBtn"] = "Exit CrackerBlock",
            ["Trial_ActiveStatus"] = "3-Day Testing Trial Active",
            ["Lang_English"] = "English",
            ["Lang_Sinhala"] = "සිංහල",
            ["Lang_Selector"] = "Language / භාෂාව",

            // Lock Screen
            ["Lock_WelcomeBack"] = "Welcome Back",
            ["Lock_EnterMasterPin"] = "Enter Master PIN to Unlock Dashboard",
            ["Lock_EnterPinTitle"] = "Enter Master PIN",
            ["Lock_PinLabel"] = "Master PIN",
            ["Lock_SetupTitle"] = "INITIAL SETUP: Create Master PIN (4-8 digits)",
            ["Lock_MasterPin"] = "Master PIN",
            ["Lock_ConfirmPin"] = "Confirm Master PIN",
            ["Lock_UnlockBtn"] = "Unlock Dashboard",
            ["Lock_SetupBtn"] = "Save & Activate Guardian",
            ["Lock_SecurityNotice"] = "Protected by OS-Level Tamper Defense and Anti-Cheat Watchdog",
            ["Lock_ShowPin"] = "Show PIN",
            ["Lock_HidePin"] = "Hide PIN",
            ["Lock_PinRequirements"] = "Master PIN must be 4 to 8 digits.",

            // Apps View
            ["Apps_Title"] = "Restricted Applications",
            ["Apps_Subtitle"] = "Block distracting executables, games, and background processes.",
            ["Apps_AddNew"] = "Add New Application Rule",
            ["Apps_ProcessName"] = "Process Name (.exe):",
            ["Apps_ProcessHint"] = "e.g. discord.exe, steam.exe, chrome.exe",
            ["Apps_PickRunning"] = "⚡ Pick Running App",
            ["Apps_BrowseExe"] = "📁 Browse .exe",
            ["Apps_PopularShortcuts"] = "Popular Apps:",
            ["Apps_Schedule"] = "Schedule:",
            ["Apps_BtnAdd"] = "+ Add Application Rule",
            ["Apps_ManagedRules"] = "Managed Restricted Applications",
            ["Apps_ActiveCount"] = "Active Executable Rules",
            ["Apps_NoRules"] = "No application rules configured yet. Add an executable above to start locking.",
            ["Apps_ColProcess"] = "Process Name",
            ["Apps_ColSchedule"] = "Schedule",
            ["Apps_ColStatus"] = "Status",
            ["Apps_ColActions"] = "Actions",
            ["Apps_Unlock15m"] = "🔓 Unlock 15m",
            ["Apps_Relock"] = "🔒 Relock Now",
            ["Apps_Unlock30m"] = "30m",
            ["Apps_Unlock60m"] = "1h",
            ["Apps_Delete"] = "Delete",
            ["Apps_RunningTitle"] = "Select Running Application",
            ["Apps_SearchPlaceholder"] = "Filter processes by name...",
            ["Apps_Cancel"] = "Cancel",
            ["Apps_ActiveState"] = "Active",
            ["Apps_DisabledState"] = "Disabled",

            // Websites View
            ["Web_Title"] = "Web Filtering & Redirects",
            ["Web_Subtitle"] = "Intercept un-productive domains and optionally redirect to focus sites.",
            ["Web_AddNew"] = "Add Domain Restriction",
            ["Web_DomainName"] = "Domain to Block:",
            ["Web_DomainHint"] = "e.g. facebook.com, youtube.com, reddit.com",
            ["Web_RedirectUrl"] = "Redirect Target URL (Optional):",
            ["Web_RedirectHint"] = "e.g. wikipedia.org, google.com",
            ["Web_Schedule"] = "Schedule:",
            ["Web_BtnAdd"] = "+ Add Website Rule",
            ["Web_ActiveRules"] = "Active Blocked Domains & Redirects",
            ["Web_NoRules"] = "No website rules configured yet. Add a domain above to start filtering.",
            ["Web_ColDomain"] = "Blocked Domain",
            ["Web_ColRedirect"] = "Redirect Destination",
            ["Web_ColSchedule"] = "Schedule",
            ["Web_ColStatus"] = "Status",
            ["Web_ColActions"] = "Actions",
            ["Web_DirectBlock"] = "Direct Access Denied (HTTP 403)",
            ["Web_Chain"] = "🔗 Chain",
            ["Web_ActiveState"] = "Active",
            ["Web_DisabledState"] = "Disabled",

            // Schedules View
            ["Sched_Title"] = "Schedules & Deep Focus Sessions",
            ["Sched_Subtitle"] = "Harness structured high-intensity focus sprints with Strict Nuclear Lock, or automate recurring time windows.",
            ["Sched_HeroCardTitle"] = "🍅 Interactive Focus Timer & Nuclear Mode",
            ["Sched_FocusTitle"] = "⚡ Deep Work Focus Sprint",
            ["Sched_FocusDesc"] = "Initiate a timed lockdown sprint. Restrict access to distracting apps and websites.",
            ["Sched_Nuclear"] = "Strict Nuclear Mode (Disable PIN override during session, no cheating)",
            ["Sched_StrictNuclear"] = "⚡ Strict Nuclear Mode",
            ["Sched_StrictNuclearDesc"] = "Completely disables Master PIN pause and bypasses until the countdown finishes. No cheating.",
            ["Sched_SelectPreset"] = "Select Sprint Preset:",
            ["Sched_StartSprint"] = "Start Focus Sprint",
            ["Sched_StartSprintBtn"] = "▶️ Start Focus Sprint",
            ["Sched_EndSprint"] = "End Focus Session",
            ["Sched_NuclearActive"] = "🔒 Nuclear Mode Active (No Cheating)",
            ["Sched_CustomWindows"] = "Custom Time Windows",
            ["Sched_CustomSubtitle"] = "Create specialized blocking windows tailored to your work, study blocks, and evening routines.",
            ["Sched_HowItWorks"] = "💡 How Schedules Work",
            ["Sched_Explanation"] = "Custom schedules defined here become selectable in the 'Active Schedule' dropdowns when adding App or Web rules.",
            ["Sched_AddWindow"] = "+ Add Time Window",
            ["Sched_AddWindowBtn"] = "➕ Add Time Window",
            ["Sched_Name"] = "Schedule Name:",
            ["Sched_Days"] = "Days Active:",
            ["Sched_Hours"] = "Time Range:",
            ["Sched_StartPrompt"] = "Start (HH:mm)",
            ["Sched_EndPrompt"] = "End (HH:mm)",
            ["Sched_StandardPresets"] = "Standard Preset Schedules",
            ["Sched_StandardPresetsDesc"] = "Built-in business and focus presets evaluated by the guardian engine.",

            // Settings View
            ["Settings_Title"] = "Settings & Tamper Defense",
            ["Settings_Subtitle"] = "Manage background service daemon, anti-tamper policies, and Master PIN credentials.",
            ["Diag_Title"] = "System Diagnostics & Self-Healing Wizard",
            ["Diag_RunBtn"] = "🔍 Run Health Check",
            ["Diag_AutoRepairBtn"] = "🔧 Auto-Repair All Issues",
            ["SafeChild_Title"] = "Safe Child Shield (Parental Protection)",
            ["SafeChild_Desc"] = "One-click protection shielding children from adult content, gambling, and addictive gaming.",
            ["SafeChild_Enable"] = "👶 Enable Child Shield",
            ["SafeChild_Active"] = "🛡️ SHIELD ACTIVE (Click to Unlock)",
            ["SafeChild_Adult"] = "🔞 Block Adult Websites & Online Gambling",
            ["SafeChild_AdultDesc"] = "Restricts adult entertainment sites, NSFW forums, sexually explicit content, and gambling/betting platforms.",
            ["SafeChild_Social"] = "📱 Restrict Social Media Browsing",
            ["SafeChild_SocialDesc"] = "Blocks distracting social feeds including TikTok, Instagram, Facebook, Snapchat, Reddit, and Twitter/X.",
            ["SafeChild_Gaming"] = "🎮 Block Addictive Games & Launchers",
            ["SafeChild_GamingDesc"] = "Automatically terminates Roblox, Steam, Epic Games, Valorant, Riot Client, Minecraft, and blocks web games.",
            ["SafeChild_SafeSearch"] = "🔍 Enforce Strict Search Engine SafeSearch",
            ["SafeChild_SafeSearchDesc"] = "Forces family-safe strict filtering on Google, Bing, YouTube, and DuckDuckGo search queries.",
            ["SafeChild_Vpn"] = "🛡️ Block VPNs, Tor & Web Unblockers",
            ["SafeChild_VpnDesc"] = "Terminates VPN clients (ProtonVPN, NordVPN, Windscribe, WireGuard, Tor) and blocks proxy unblocker websites.",
            ["Settings_DaemonTitle"] = "Background Worker Service Daemon",
            ["Settings_PinTitle"] = "Master PIN Authentication",
            ["Settings_PinCurrent"] = "Current PIN",
            ["Settings_PinNew"] = "New PIN",
            ["Settings_PinConfirm"] = "Confirm New PIN",
            ["Settings_PinUpdateBtn"] = "Update PIN",
            ["Settings_LanguageTitle"] = "Interface Language / අතුරුමුහුණත් භාෂාව",
            ["Settings_LanguageDesc"] = "Select your preferred language. All views update instantly without restarting.",
            ["Settings_LockTitle"] = "Lock Session",
            ["Settings_LockDesc"] = "Return to the authentication gate to prevent unauthorized configuration changes.",
            ["Settings_LockBtn"] = "🔒 Lock Dashboard",

            // Logs View
            ["Logs_Title"] = "Productivity & Distraction Analytics",
            ["Logs_Subtitle"] = "Live distraction telemetry, time saved, top intercepted apps/domains, and forensic audit trail.",
            ["Logs_RangeToday"] = "Today",
            ["Logs_RangeWeek"] = "Past 7 Days",
            ["Logs_RangeAllTime"] = "All Time",
            ["Logs_TotalBlocks"] = "TOTAL BLOCKS",
            ["Logs_TimePreserved"] = "TIME PRESERVED",
            ["Logs_TimePreservedDesc"] = "Est. 5 mins / distraction saved",
            ["Logs_FocusScore"] = "FOCUS SCORE",
            ["Logs_FocusScoreDesc"] = "Based on sprints & policy adherence",
            ["Logs_TamperIntercepts"] = "TAMPER INTERCEPTS",
            ["Logs_SearchHint"] = "Filter logs by event or process...",
            ["Logs_Export"] = "📥 Export CSV",
            ["Logs_Refresh"] = "🔄 Refresh",
            ["Logs_Clear"] = "🗑️ Clear Logs",
            ["Logs_ColTime"] = "Timestamp (UTC)",
            ["Logs_ColEvent"] = "Event Category",
            ["Logs_ColDesc"] = "Details & Parameters"
        };

        private static readonly Dictionary<string, string> SinhalaTranslations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Navigation & Brand
            ["Nav_Applications"] = "යෙදුම්",
            ["Nav_Websites"] = "වෙබ් අඩවි",
            ["Nav_Schedules"] = "කාලසටහන් සහ අවධානය",
            ["Nav_Logs"] = "විශ්ලේෂණ සහ සටහන්",
            ["Nav_Settings"] = "සැකසුම් සහ ආරක්ෂාව",
            ["Nav_LockDashboard"] = "පුවරුව අගුළු දමන්න",
            ["Brand_Title"] = "ඇප්බ්ලොකර්",
            ["Brand_Subtitle"] = "පරිගණක සහ වෙබ් පාලනය",
            ["Status_ServiceActive"] = "පද්ධති ආරක්ෂණය සක්‍රීයයි",
            ["Status_ServiceOffline"] = "සේවාව විසන්ධි වී ඇත",
            ["Btn_StartService"] = "ආරක්ෂාව ආරම්භ කරන්න",
            ["Tip_StartEngine"] = "පසුබිම් ආරක්ෂණ එන්ජිම ස්ථාපනය කර ආරම්භ කිරීමට ක්ලික් කරන්න (UAC)",
            ["Tip_ToggleSidebar"] = "පැති තීරුව හකුලන්න / විහිදන්න",
            ["Trial_EvaluationBadge"] = "3-දින පරීක්ෂණ කාලය",
            ["Trial_ExpiredTitle"] = "පරීක්ෂණ කාලය අවසන් වී ඇත",
            ["Trial_ExpiredDesc"] = "CrackerBlock මෘදුකාංගය සඳහා වූ 3-දින පරිශීලක පරීක්ෂණ ඇගයීම් කාලය සාර්ථකව අවසන් විය. පරීක්ෂා කිරීම පිළිබඳව ස්තූතියි. යෙදුම අඛණ්ඩව භාවිතා කිරීමට කරුණාකර වලංගු සක්‍රිය කිරීමේ යතුරක් (License Key) ඇතුළත් කරන්න.",
            ["Trial_DeviceId"] = "උපාංග අංකය (Device ID):",
            ["Trial_KeyPrompt"] = "බලපත්‍ර සක්‍රිය කිරීමේ යතුර ඇතුළත් කරන්න (උදා: CB-TEST-2026-UNLK):",
            ["Trial_ActivateBtn"] = "බලපත්‍රය සක්‍රිය කරන්න",
            ["Trial_ExitBtn"] = "යෙදුමෙන් පිටවන්න",
            ["Trial_ActiveStatus"] = "3-දින පරීක්ෂණ කාලය සක්‍රීයයි",
            ["Lang_English"] = "English",
            ["Lang_Sinhala"] = "සිංහල",

            // Lock Screen
            ["Lock_EnterPinTitle"] = "ප්‍රධාන PIN අංකය ඇතුළත් කරන්න",
            ["Lock_EnterPinDesc"] = "ක්‍රැකර්බ්ලොක් පාලන පුවරුවට පිවිසීමට ඔබේ ඉලක්කම් 4-8 ප්‍රධාන PIN අංකය ඇතුළත් කරන්න.",
            ["Lock_SetupTitle"] = "ප්‍රධාන PIN අංකයක් සකසන්න",
            ["Lock_SetupDesc"] = "යෙදුම් සහ වෙබ් අඩවි ආරක්ෂා කිරීමට ඉලක්කම් 4-8 ක නව ප්‍රධාන PIN අංකයක් සාදන්න.",
            ["Lock_PinLabel"] = "ප්‍රධාන PIN අංකය",
            ["Lock_ShowPin"] = "PIN අංකය පෙන්වන්න",
            ["Lock_HidePin"] = "PIN අංකය සඟවන්න",
            ["Lock_ConfirmPinLabel"] = "PIN අංකය නැවත ඇතුළත් කරන්න",
            ["Lock_UnlockButton"] = "අගුළු හරින්න",
            ["Lock_SetPinButton"] = "PIN අංකය සුරකින්න",
            ["Lock_SecurityNote"] = "🔒 ආරක්ෂිත මාදිලිය: වැරදි PIN උත්සාහයන් සටහන් වන අතර ආරක්ෂක එන්ජිම මඟින් නිරීක්ෂණය කෙරේ.",

            // Applications View
            ["Apps_Title"] = "යෙදුම් අගුළු දැමීම",
            ["Apps_Subtitle"] = "පසුබිම් සේවාව මඟින් නිරීක්ෂණය කරන ක්‍රියාකාරී යෙදුම් සීමා කරන්න.",
            ["Apps_AddNew"] = "නව යෙදුම් සීමාවක් එක් කරන්න",
            ["Apps_ProcessName"] = "ක්‍රියාවලි නාමය (Process Name):",
            ["Apps_ProcessHint"] = "උදා: chrome, discord, telegram, steam, notepad",
            ["Apps_BrowseExe"] = "ගවේෂණය (.exe)",
            ["Apps_PickRunning"] = "ක්‍රියාත්මක වන යෙදුමක් තෝරන්න",
            ["Apps_Schedule"] = "කාලසටහන:",
            ["Apps_Always"] = "සැමවිටම සක්‍රීයයි",
            ["Apps_BtnAdd"] = "+ යෙදුම් නීතිය එක් කරන්න",
            ["Apps_ManagedRules"] = "කළමනාකරණය කරන සීමිත යෙදුම්",
            ["Apps_ActiveCount"] = "ක්‍රියාකාරී ක්‍රියාත්මක කළ හැකි නීති",
            ["Apps_NoRules"] = "තවමත් යෙදුම් නීති සකසා නැත. අගුළු දැමීම ඇරඹීමට ඉහතින් යෙදුමක් එක් කරන්න.",
            ["Apps_ColProcess"] = "ක්‍රියාවලි නාමය",
            ["Apps_ColSchedule"] = "කාලසටහන",
            ["Apps_ColStatus"] = "තත්ත්වය",
            ["Apps_ColActions"] = "ක්‍රියාමාර්ග",
            ["Apps_Unlock15m"] = "🔓 විනාඩි 15කට අගුළු හරින්න",
            ["Apps_Relock"] = "🔒 නැවත අගුළු දමන්න",
            ["Apps_Unlock30m"] = "විනාඩි 30",
            ["Apps_Unlock60m"] = "පැය 1",
            ["Apps_Delete"] = "මකන්න",
            ["Apps_RunningTitle"] = "ක්‍රියාත්මක වන යෙදුමක් තෝරන්න",
            ["Apps_SearchPlaceholder"] = "නමෙන් යෙදුම් සොයන්න...",
            ["Apps_Cancel"] = "අවලංගු කරන්න",
            ["Apps_ActiveState"] = "සක්‍රීයයි",
            ["Apps_DisabledState"] = "අක්‍රීයයි",

            // Websites View
            ["Web_Title"] = "වෙබ් පෙරීම සහ යළි-යොමුකිරීම්",
            ["Web_Subtitle"] = "ඵලදායී නොවන වෙබ් අඩවි අවහිර කරන්න හෝ අවධානය යොමු කළ හැකි අඩවි වෙත යොමු කරන්න.",
            ["Web_AddNew"] = "නව වෙබ් අඩවි සීමාවක් එක් කරන්න",
            ["Web_DomainName"] = "අවහිර කළ යුතු වෙබ් අඩවිය (Domain):",
            ["Web_DomainHint"] = "උදා: facebook.com, youtube.com, reddit.com",
            ["Web_RedirectUrl"] = "යළි-යොමු කළ යුතු අඩවිය (විකල්ප):",
            ["Web_RedirectHint"] = "උදා: wikipedia.org, google.com",
            ["Web_Schedule"] = "කාලසටහන:",
            ["Web_BtnAdd"] = "+ වෙබ් අඩවි නීතිය එක් කරන්න",
            ["Web_ActiveRules"] = "අවහිර කළ වෙබ් අඩවි සහ යළි-යොමුකිරීම්",
            ["Web_NoRules"] = "තවමත් වෙබ් අඩවි නීති සකසා නැත. පෙරීම ඇරඹීමට ඉහතින් වෙබ් අඩවියක් එක් කරන්න.",
            ["Web_ColDomain"] = "අවහිර කළ වෙබ් අඩවිය",
            ["Web_ColRedirect"] = "යළි-යොමු කරන අඩවිය",
            ["Web_ColSchedule"] = "කාලසටහන",
            ["Web_ColStatus"] = "තත්ත්වය",
            ["Web_ColActions"] = "ක්‍රියාමාර්ග",
            ["Web_DirectBlock"] = "සෘජුවම අවහිර කර ඇත (HTTP 403)",
            ["Web_Chain"] = "🔗 දාමය",
            ["Web_ActiveState"] = "සක්‍රීයයි",
            ["Web_DisabledState"] = "අක්‍රීයයි",

            // Schedules View
            ["Sched_Title"] = "බලාත්මක කිරීමේ කාලසටහන් සහ පොමෝඩෝරෝ අවධානය",
            ["Sched_Subtitle"] = "දැඩි න්‍යෂ්ටික අගුලක් සමඟ ඉහළ අවධානයක් රඳවාගන්න, නැතහොත් ස්වයංක්‍රීය කාල පරාසයන් සකසන්න.",
            ["Sched_HeroCardTitle"] = "🍅 අන්තර්ක්‍රියාකාරී අවධානය යොමුකිරීමේ ටයිමරය සහ න්‍යෂ්ටික මාදිලිය",
            ["Sched_FocusTitle"] = "⚡ දැඩි අවධානය යොමු කිරීමේ සැසිය (Focus Sprint)",
            ["Sched_FocusDesc"] = "කාලරාමුවක් සහිත අගුළු දැමීමේ සැසියක් අරඹන්න. සියලු බාධාවන් වළක්වන්න.",
            ["Sched_Nuclear"] = "දැඩි න්‍යෂ්ටික මාදිලිය (සැසිය අවසන් වන තුරු PIN අක්‍රීය වේ, වංචා කළ නොහැක)",
            ["Sched_StrictNuclear"] = "⚡ දැඩි න්‍යෂ්ටික මාදිලිය (Strict Nuclear Mode)",
            ["Sched_StrictNuclearDesc"] = "ගණනය අවසන් වන තුරු ප්‍රධාන PIN අංකයෙන් නවතා දැමීම හෝ වෙනස් කිරීම සම්පූර්ණයෙන්ම වළක්වයි. කිසිදු වංචාවක් කළ නොහැක.",
            ["Sched_SelectPreset"] = "කාල පරාසය තෝරන්න:",
            ["Sched_StartSprint"] = "අවධානය යොමු කිරීමේ සැසිය අරඹන්න",
            ["Sched_StartSprintBtn"] = "▶️ අවධානය යොමු කිරීමේ සැසිය අරඹන්න",
            ["Sched_EndSprint"] = "සැසිය අවසන් කරන්න",
            ["Sched_NuclearActive"] = "🔒 න්‍යෂ්ටික මාදිලිය සක්‍රීයයි (වංචා කළ නොහැක)",
            ["Sched_CustomWindows"] = "අභිරුචි කාල පරාසයන්",
            ["Sched_CustomSubtitle"] = "ඔබගේ අධ්‍යයන හෝ කාර්යාල වේලාවන්ට ගැලපෙන විශේෂිත අවහිර කිරීමේ කාලසීමාවන් සකසන්න.",
            ["Sched_HowItWorks"] = "💡 කාලසටහන් ක්‍රියාත්මක වන ආකාරය",
            ["Sched_Explanation"] = "මෙහි සාදන කාලසටහන් යෙදුම් හෝ වෙබ් නීති සාදන විට 'ක්‍රියාත්මක කාලසටහන' ලෙස තෝරාගත හැක.",
            ["Sched_AddWindow"] = "+ කාල පරාසයක් එක් කරන්න",
            ["Sched_AddWindowBtn"] = "➕ කාල පරාසයක් එක් කරන්න",
            ["Sched_Name"] = "කාලසටහනේ නම:",
            ["Sched_Days"] = "සක්‍රීය දින:",
            ["Sched_Hours"] = "කාල පරාසය:",
            ["Sched_StartPrompt"] = "ආරම්භය (HH:mm)",
            ["Sched_EndPrompt"] = "අවසානය (HH:mm)",
            ["Sched_StandardPresets"] = "පද්ධතියේ සම්මත කාලසටහන්",
            ["Sched_StandardPresetsDesc"] = "ආරක්ෂක එන්ජිම මඟින් සක්‍රීය කෙරෙන පෙරනිමි කාර්යාලීය සහ අවධානය යොමුකිරීමේ කාලසටහන්.",

            // Settings View
            ["Settings_Title"] = "සැකසුම් සහ ආරක්ෂාව",
            ["Settings_Subtitle"] = "පසුබිම් සේවාව, අනවසර වෙනස්කම් වැළැක්වීමේ ප්‍රතිපත්ති සහ ප්‍රධාන PIN අංකය කළමනාකරණය කරන්න.",
            ["Diag_Title"] = "ස්වයං-සුවකිරීම් සහ පද්ධති රෝග විනිශ්චය",
            ["Diag_RunBtn"] = "🔍 පද්ධති පරීක්ෂාව අරඹන්න",
            ["Diag_AutoRepairBtn"] = "🔧 සියලු ගැටලු ස්වයංක්‍රීයව පිළිසකර කරන්න",
            ["SafeChild_Title"] = "ළමා ආරක්ෂණ පලිහ (Safe Child Shield)",
            ["SafeChild_Desc"] = "වැඩිහිටි අඩවි, සූදු සහ ඇබ්බැහි වන ක්‍රීඩා වලින් දරුවන් ආරක්ෂා කිරීම.",
            ["SafeChild_Enable"] = "👶 ළමා ආරක්ෂණ පලිහ සක්‍රීය කරන්න",
            ["SafeChild_Active"] = "🛡️ පලිහ සක්‍රීයයි (අක්‍රීය කිරීමට ක්ලික් කරන්න)",
            ["SafeChild_Adult"] = "🔞 වැඩිහිටි සහ සූදු වෙබ් අඩවි අවහිර කරන්න",
            ["SafeChild_AdultDesc"] = "වැඩිහිටි විනෝදාස්වාද අඩවි, අසභ්‍ය සංසද සහ ඔට්ටු ඇල්ලීමේ වෙබ් අඩවි සම්පූර්ණයෙන්ම වළක්වයි.",
            ["SafeChild_Social"] = "📱 සමාජ මාධ්‍ය අවහිර කරන්න",
            ["SafeChild_SocialDesc"] = "TikTok, Instagram, Facebook, Snapchat, Reddit සහ X (Twitter) ඇතුළු සමාජ මාධ්‍ය අවහිර කරයි.",
            ["SafeChild_Gaming"] = "🎮 ඇබ්බැහි වන පරිගණක ක්‍රීඩා අවහිර කරන්න",
            ["SafeChild_GamingDesc"] = "Roblox, Steam, Epic Games, Valorant, Minecraft ඇතුළු ක්‍රීඩා යෙදුම් සහ ඔන්ලයින් ක්‍රීඩා අඩවි අවහිර කරයි.",
            ["SafeChild_SafeSearch"] = "🔍 දැඩි SafeSearch සක්‍රීය කරන්න",
            ["SafeChild_SafeSearchDesc"] = "Google, Bing, YouTube සහ DuckDuckGo සෙවුම් වල දැඩි පවුල් හිතකාමී SafeSearch බලාත්මක කරයි.",
            ["SafeChild_Vpn"] = "🛡️ VPN, Tor සහ Proxy Unblocker අඩවි අවහිර කරන්න",
            ["SafeChild_VpnDesc"] = "ProtonVPN, NordVPN, WireGuard, Tor වැනි VPN මෘදුකාංග සහ බයිපාස් වෙබ් අඩවි අවහිර කරයි.",
            ["Settings_DaemonTitle"] = "පසුබිම් සේවා ඩීමනය (Worker Daemon)",
            ["Settings_PinTitle"] = "ප්‍රධාන PIN සත්‍යාපනය",
            ["Settings_PinCurrent"] = "වත්මන් PIN අංකය",
            ["Settings_PinNew"] = "නව PIN අංකය",
            ["Settings_PinConfirm"] = "නව PIN අංකය තහවුරු කරන්න",
            ["Settings_PinUpdateBtn"] = "PIN යාවත්කාලීන කරන්න",
            ["Settings_LanguageTitle"] = "Interface Language / අතුරුමුහුණත් භාෂාව",
            ["Settings_LanguageDesc"] = "ඔබ කැමති භාෂාව තෝරන්න. නැවත ආරම්භ කිරීමකින් තොරව සියලු තිර ක්ෂණිකව යාවත්කාලීන වේ.",
            ["Settings_LockTitle"] = "සැසිය අගුළු දමන්න",
            ["Settings_LockDesc"] = "අනවසර වෙනස්කම් වැළැක්වීමට නැවත PIN තිරයට යන්න.",
            ["Settings_LockBtn"] = "🔒 පුවරුව අගුළු දමන්න",

            // Logs View
            ["Logs_Title"] = "ඵලදායිතා සහ බාධා විශ්ලේෂණය",
            ["Logs_Subtitle"] = "තත්‍ය කාලීන බාධා කිරීම්, ඉතිරි කළ කාලය, අවහිර කළ යෙදුම්/වෙබ් අඩවි සහ විගණන වාර්තා.",
            ["Logs_RangeToday"] = "අද දින",
            ["Logs_RangeWeek"] = "පසුගිය දින 7",
            ["Logs_RangeAllTime"] = "සම්පූර්ණ කාලය",
            ["Logs_TotalBlocks"] = "මුළු අවහිර කිරීම්",
            ["Logs_TimePreserved"] = "ඉතිරි කළ කාලය",
            ["Logs_TimePreservedDesc"] = "එක් බාධාවකට විනාඩි ~5 ක් ඉතිරි කර ඇත",
            ["Logs_FocusScore"] = "අවධානය ලකුණු",
            ["Logs_FocusScoreDesc"] = "අවධානය යොමුකිරීමේ සැසි අනුව ගණනය කෙරේ",
            ["Logs_TamperIntercepts"] = "වංචා වැළැක්වීම්",
            ["Logs_SearchHint"] = "සිදුවීම් හෝ යෙදුම් නමින් සොයන්න...",
            ["Logs_Export"] = "📥 CSV ලෙස බාගත කරන්න",
            ["Logs_Refresh"] = "🔄 නැවුම් කරන්න",
            ["Logs_Clear"] = "🗑️ සටහන් මකන්න",
            ["Logs_ColTime"] = "වේලාව (UTC)",
            ["Logs_ColEvent"] = "සිදුවීම් වර්ගය",
            ["Logs_ColDesc"] = "විස්තරය සහ පරාමිතීන්"
        };

        #endregion
    }

    /// <summary>
    /// Markup extension for XAML to enable clean dynamic localized strings: {loc:Loc KeyName}
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
