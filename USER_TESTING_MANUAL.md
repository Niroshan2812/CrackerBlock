# 🛡️ CrackerBlock v1.0 — User & End-User Testing Manual
*(පරිශීලක සහ පරීක්ෂණ මාර්ගෝපදේශය)*

---

## 📋 Welcome to CrackerBlock v1.0 Release
**CrackerBlock** is a high-security, distraction-blocking and parental control application for Windows. It combines a Windows Background Service daemon (`LocalSystem`), mutual anti-tamper watchdog, HTTPS/TLS interception engine, and a WPF desktop dashboard.

This manual provides end-users and QA testers with step-by-step instructions to install, test all core features, switch languages (English & සිංහල), verify anti-cheat security, and cleanly uninstall.

---

## 🚀 1. Quick Start & Installation (පළමු වර පිහිටුවීම)

### Prerequisites
* **Operating System**: Windows 10 (Build 19041+) or Windows 11 (64-bit).
* **Permissions**: Local Administrator privileges (required once to register the background service).
* **Runtime**: .NET 9.0 Runtime (x64) installed on the machine.

### Package Structure
Inside this `Release_v1.0` folder, you will find:
```text
Release_v1.0/
├── CrackerBlock.exe              <-- 🚀 MAIN 1-CLICK LAUNCHER (Double-click to start!)
├── Start_CrackerBlock.bat        <-- ⚡ Quick 1-click launcher script (Alternative)
├── Uninstall_CrackerBlock.bat    <-- 🗑️ Clean Uninstaller (Auto-elevates & restores system)
├── USER_TESTING_MANUAL.md        <-- User testing guide
├── Dashboard/                    <-- Application Dashboard components
└── Service/                      <-- Background Guardian Engine & Watchdog
```

### 🚀 How to Run CrackerBlock:
1. Open the `Release_v1.0` folder.
2. Simply double-click **`CrackerBlock.exe`** (or `Start_CrackerBlock.bat`).
3. If this is your first time launching, Windows will display a standard prompt:
   *"Do you want to allow this app to make changes to your device?"*
4. Click **"Yes"**.
5. That's all! CrackerBlock automatically installs, starts the background engine, and immediately opens the modern dashboard.

> [!TIP]
> **Zero Prompts on Daily Use:** Once installed, CrackerBlock runs automatically in the background. Subsequent double-clicks open the dashboard in under 1 second without showing any UAC prompts!

---

## 🌐 2. Multi-Language Support (භාෂාව තේරීම: English / සිංහල)

CrackerBlock supports **English** and **Sinhala (සිංහල)** with on-the-fly dynamic switching.

### How to Switch Languages:
* **Option A (Lock Screen)**: Click the `[ 🌐 🇱🇰 සිංහල ]` / `[ 🌐 🇺🇸 English ]` toggle pill at the top of the authentication card before logging in.
* **Option B (Top Header)**: Click the `[ 🌐 සිංහල ]` button in the top-left sidebar header at any time.
* **Option C (Settings Tab)**: Go to **Settings & Defense** ➔ scroll to the **Interface Language / අතුරුමුහුණත් භාෂාව** card ➔ click **English** or **සිංහල**.

*The selected language is permanently saved to `%ProgramData%\AppBlocker\ui_language.txt` and remembered every time the app launches.*

---

## ⏳ 3. 3-Day Evaluation Period & License Activation (3-දින පරීක්ෂණ කාලය)

To protect testing distributions, CrackerBlock v1.0 includes an automatic **3-Day (72-Hour) Evaluation Period**:

* **Full Functionality During Testing**: For 3 days from initial installation, all features (Applications, Websites, Schedules, Safe Child Mode, Anti-Tamper) are 100% active.
* **Live Countdown Badge**: A live evaluation timer is displayed in the bottom sidebar:
  * e.g., `⏱️ 2d 18h remaining` (or `⏱️ දින 2, පැය 18 ඉතිරිව ඇත` in Sinhala).
* **Automatic Expiration Lockdown**: Once the 72 hours elapse, the application locks down completely and presents the **Evaluation Period Expired** screen. The user cannot access dashboard tabs or configure rules without an activation key.
* **Tester Safety**: When the evaluation expires, background process termination and web blocking automatically pause so the tester's computer is never locked out of regular apps.
* **Anti-Clock-Tampering**: Setting the system clock backwards triggers anti-tamper detection and locks the evaluation.
* **How to Unlock / Activate**:
  * On the lock screen, enter a license key:
    * **Master Evaluation Key**: `CB-TEST-2026-UNLK`
    * Or a device-specific key generated from the displayed **Device ID**.
  * Click **"🔑 Activate License"** to permanently lift the restriction.

---

## 🧪 4. End-User Testing Scenarios & Checklist

Use this checklist to test each feature systematically:

### Test Case 1: Initial Master PIN Setup & Lock Screen
* **Action**: Launch the app for the first time.
* **Expected Result**: 
  * The screen displays `"INITIAL SETUP: Create Master PIN (4-8 digits)"` (or `"ප්‍රධාන PIN අංකයක් සකසන්න"` in Sinhala).
  * Type a PIN (e.g. `1234`). Click the eye icon (👁️/🙈) to verify PIN masking and unmasking.
  * Enter confirm PIN and click **Save & Activate**.
  * Dashboard unlocks immediately.
* **Re-test Lock**: Click **"🔒 Lock Dashboard"** in the sidebar. Enter an incorrect PIN (`9999`) ➔ verify error message. Enter correct PIN (`1234`) ➔ dashboard unlocks.

---

### Test Case 2: Application Blocking & Temporary Unlock
* **Action**:
  1. Go to the **Applications** (යෙදුම්) tab.
  2. In the "Process Name" field, enter `notepad` or `calc`, or click **"⚡ Pick Running App"** and choose an open process.
  3. Leave schedule as `"Always Active"` and click **"+ Add Application Rule"**.
  4. Launch Notepad or Calculator from Windows.
* **Expected Result**: 
  * The restricted process is terminated instantly within milliseconds.
  * A Windows toast notification appears informing that the application was blocked.
* **Test Temporary Unlock**:
  * In the active rules list, click **"🔓 Unlock 15m"** next to the rule.
  * Launch Notepad again ➔ it now opens and runs freely!
  * Verify the button turns into **"🔒 Relock Now"** with an active countdown.
  * Click **"🔒 Relock Now"** ➔ the app is immediately locked again.

---

### Test Case 3: Website Filtering & Domain Redirects
* **Action**:
  1. Go to the **Websites** (වෙබ් අඩවි) tab.
  2. In "Domain to Block", enter `facebook.com`.
  3. In "Redirect Target URL (Optional)", enter `wikipedia.org`.
  4. Click **"+ Add Website Rule"**.
  5. Open Google Chrome, Microsoft Edge, or Firefox and navigate to `https://www.facebook.com`.
* **Expected Result**:
  * The browser immediately redirects to Wikipedia (`https://www.wikipedia.org`).
* **Test Direct Block**:
  * Add a domain without a redirect (e.g., `tiktok.com`).
  * Visit `https://www.tiktok.com` ➔ The browser displays the CrackerBlock Access Denied page (HTTP 403).
* **Test Rule Deletion**:
  * Click the 🗑️ (Delete) button next to `facebook.com`.
  * Refresh `https://www.facebook.com` in your browser ➔ Facebook loads normally again.

---

### Test Case 4: Pomodoro Focus Timer & Nuclear Mode
* **Action**:
  1. Go to the **Schedules & Focus** (කාලසටහන්) tab.
  2. Select preset **"🍅 25m Pomodoro"** or **"⚡ 50m Deep Work"**.
  3. Check the **"⚡ Strict Nuclear Mode"** box.
  4. Click **"▶️ Start Focus Sprint"**.
* **Expected Result**:
  * The hero card turns into active sprint mode with glowing purple/red accents.
  * The circular timer begins counting down in real-time.
  * All distracting apps and websites are strictly blocked.
  * In Strict Nuclear Mode, premature cancellation is disabled to prevent cheating until the sprint finishes.

---

### Test Case 5: Safe Child Mode (Parental Shield)
* **Action**:
  1. Go to the **Settings & Defense** (සැකසුම්) tab.
  2. Locate the **Safe Child Mode (Parental Protection Shield)** card.
  3. Click **"👶 Enable Child Shield"**.
* **Expected Result**:
  * The card highlights with a glowing purple shield and displays **"🛡️ SHIELD ACTIVE"**.
  * The 5 protection modules are automatically activated:
    * 🔞 **Adult Websites & Gambling**: Pornographic and betting websites blocked.
    * 📱 **Social Media**: TikTok, Instagram, Facebook, Snapchat, Reddit blocked.
    * 🎮 **Addictive Games**: Roblox, Steam, Epic Games, Minecraft, Poki blocked.
    * 🔍 **Strict SafeSearch**: Google, Bing, YouTube SafeSearch forced.
    * 🛡️ **VPN & Proxy Circumvention**: ProtonVPN, NordVPN, Tor, and web unblockers blocked.
* **Disabling Child Shield**:
  * Click **"🛡️ SHIELD ACTIVE (Click to Unlock)"** ➔ Enter Master PIN ➔ Shield unlocks.

---

### Test Case 6: 1-Click Diagnostics & Self-Healing Wizard
* **Action**:
  1. In the **Settings & Defense** tab, locate the **System Diagnostics & Self-Healing Wizard** card.
  2. Click **"🔍 Run Health Check"**.
* **Expected Result**:
  * A progress bar scans 9 system subsystems:
    1. Background Service Daemon
    2. Named Pipe IPC Channel
    3. Watchdog Companion Process
    4. SQLite Database Integrity
    5. Root CA Certificate
    6. System Proxy Configuration
    7. Browser Hardening Policies
    8. DNS Resolution & Loopback
    9. System Security Policies
  * A Health Score Badge appears (e.g. `100% HEALTHY (9/9 Checks Passed)`).
  * If any service was stopped or proxy was altered, clicking **"🔧 Auto-Repair All Issues"** repairs them automatically in 1 click!

---

### Test Case 7: Anti-Tamper & Watchdog Defense
* **Action**:
  1. Open Windows Task Manager (`Ctrl + Shift + Esc`).
  2. Locate `AppBlocker.Service.exe` in the Details tab.
  3. Attempt to click **"End Task"** (`taskkill /F`).
* **Expected Result**:
  * The companion `AppBlocker.Watchdog.exe` detects the unauthorized termination in real-time.
  * The service is automatically revived within 1 second!
  * An entry is logged in the **Logs & Analytics** tab under event type `"Tamper Detected"`.

---

## 🧹 4. Clean Uninstallation & System Restore

To completely uninstall CrackerBlock and restore your Windows system to its original factory state:

1. Open the `Release_v1.0` folder.
2. Double-click **`Uninstall_CrackerBlock.bat`**.
3. Windows will request UAC elevation: click **"Yes"**.
4. The uninstaller will automatically:
   * Safely signal and terminate the Watchdog and UI.
   * Stop and unregister the `AppBlockerService` Windows Service.
   * Reset Windows Internet Settings proxy to direct (`ProxyEnable = 0`).
   * Remove the AppBlocker Root CA security certificate.
   * Delete browser hardening policies and flush Windows DNS cache.
   * Ask whether you want to delete all stored rules and PIN data from `%ProgramData%\AppBlocker` (Press Enter or Y to erase, N to keep).

---

## 🇱🇰 5. සිංහල කෙටි මාර්ගෝපදේශය (Sinhala Quick Guide)

| පියවර | විස්තරය |
| :--- | :--- |
| **1. පුවරුව ඇරඹීම** | **`CrackerBlock.exe`** මත Double Click කරන්න (නැතහොත් `Start_CrackerBlock.bat`). පළමු වරට අසන UAC පණිවිඩයේ **"Yes"** ක්ලික් කළ විට පසුබිම් ආරක්ෂණ සේවාව ස්වයංක්‍රීයව ස්ථාපනය වී පාලන පුවරුව විවෘත වේ. |
| **2. භාෂාව තේරීම** | තිරයේ ඉහළ දකුණු කෙළවරේ ඇති `[ 🌐 🇱🇰 සිංහල ]` බොත්තම ක්ලික් කරන්න. සියලුම විධාන සිංහලෙන් දිස්වේ. |
| **3. යෙදුම් අගුළු දැමීම** | **"යෙදුම්"** ටැබයට ගොස් අවහිර කළ යුතු මෘදුකාංගයේ නම (උදා: `chrome`, `steam`) ඇතුළත් කර **"+ යෙදුම් නීතිය එක් කරන්න"** ක්ලික් කරන්න. |
| **4. වෙබ් අඩවි පෙරීම** | **"වෙබ් අඩවි"** ටැබයට ගොස් අවහිර කළ යුතු අඩවිය (උදා: `facebook.com`) සහ අවශ්‍ය නම් යළි-යොමු කළ යුතු අඩවිය (උදා: `wikipedia.org`) ඇතුළත් කරන්න. |
| **5. ළමා ආරක්ෂණය** | **"සැකසුම්"** ටැබයේ **"ළමා ආරක්ෂණ පලිහ"** සක්‍රීය කරන්න. වැඩිහිටි අඩවි, සූදු, ඇබ්බැහි වන ක්‍රීඩා සහ සමාජ මාධ්‍ය ක්ෂණිකව අවහිර වේ. |
| **6. ඉවත් කිරීම** | **`Uninstall_CrackerBlock.bat`** මත Double Click කර අවශ්‍ය නම් සැකසුම් මකා දමන්න. පද්ධතිය පෙර තිබූ මුල් තත්ත්වයටම පත් වේ. |

---

*CrackerBlock v1.0 — High-Performance Focused Productivity & Parental Defense*
