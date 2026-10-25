@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Must be run as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=AppBlockerService

echo [1/6] Signaling Authorized Clean Shutdown & Terminating Watchdog...
if exist "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" (
    "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" --signal-shutdown >nul 2>&1
)
taskkill /F /IM AppBlocker.Watchdog.exe >nul 2>&1

echo [2/6] Stopping %SERVICE_NAME%...
sc.exe stop "%SERVICE_NAME%" >nul 2>&1
timeout /t 2 /nobreak >nul

echo [3/6] Deleting %SERVICE_NAME% Windows Service registration...
sc.exe delete "%SERVICE_NAME%" >nul 2>&1

:: Terminate any remaining UI instances
taskkill /F /IM AppBlocker.UI.exe >nul 2>&1

echo [4/6] Restoring Windows System Proxy Settings...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul
reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1

echo [5/6] Removing AppBlocker Root CA Certificate...
powershell -Command "Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like '*AppBlocker*' } | Remove-Item -Force -ErrorAction SilentlyContinue" >nul 2>&1

echo [6/7] Restoring Policies & Flushing DNS...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f >nul 2>&1
reg delete "HKLM\Software\Policies\Google\Chrome" /v IncognitoModeAvailability /f >nul 2>&1
reg delete "HKLM\Software\Policies\Google\Chrome" /v DnsOverHttpsMode /f >nul 2>&1
reg delete "HKLM\Software\Policies\Google\Chrome" /v BuiltInDnsClientEnabled /f >nul 2>&1
reg delete "HKLM\Software\Policies\Google\Chrome" /v ProxyMode /f >nul 2>&1
reg delete "HKLM\Software\Policies\Microsoft\Edge" /v InPrivateModeAvailability /f >nul 2>&1
reg delete "HKLM\Software\Policies\Microsoft\Edge" /v DnsOverHttpsMode /f >nul 2>&1
reg delete "HKLM\Software\Policies\Microsoft\Edge" /v BuiltInDnsClientEnabled /f >nul 2>&1
reg delete "HKLM\Software\Policies\Microsoft\Edge" /v ProxyMode /f >nul 2>&1
ipconfig /flushdns >nul

echo [7/7] Purging AppBlocker Database & Master Credentials (%ProgramData%\AppBlocker)...
if exist "%ProgramData%\AppBlocker" (
    rmdir /S /Q "%ProgramData%\AppBlocker" >nul 2>&1
)

echo.
echo ========================================================
echo  AppBlocker Service uninstalled, database cleanly purged,
echo  and all OS settings restored to Windows factory defaults!
echo ========================================================
pause
