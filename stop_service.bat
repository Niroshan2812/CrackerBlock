@echo off
:: Check Administrator Privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Must be run as Administrator!
    echo Right-click this script and select 'Run as administrator'.
    pause
    exit /b 1
)

set SERVICE_NAME=AppBlockerService

echo [1/4] Signaling Authorized Clean Shutdown to Watchdog...
if exist "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" (
    "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" --signal-shutdown >nul 2>&1
)
taskkill /F /IM AppBlocker.Watchdog.exe >nul 2>&1

echo [2/4] Stopping %SERVICE_NAME%...
sc.exe stop "%SERVICE_NAME%" >nul 2>&1
timeout /t 2 /nobreak >nul

:: Terminate any remaining UI instances
taskkill /F /IM AppBlocker.UI.exe >nul 2>&1

echo [3/4] Restoring Windows System Proxy Settings...
:: Turn off system proxy in Windows registry to ensure 100% normal internet access
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul
reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1

echo [4/4] Flushing Windows DNS Cache...
ipconfig /flushdns >nul

echo.
echo ========================================================
echo  AppBlocker Service stopped successfully!
echo  Windows System Proxy disabled and Internet restored.
echo ========================================================
pause
