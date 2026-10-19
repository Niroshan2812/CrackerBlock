@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Must be run as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=AppBlockerService

echo [1/5] Stopping %SERVICE_NAME%...
sc.exe stop "%SERVICE_NAME%" >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/5] Deleting %SERVICE_NAME% Windows Service registration...
sc.exe delete "%SERVICE_NAME%" >nul 2>&1

:: Terminate any remaining UI instances
taskkill /F /IM AppBlocker.UI.exe >nul 2>&1

echo [3/5] Restoring Windows System Proxy Settings...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul
reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1

echo [4/5] Removing AppBlocker Root CA Certificate...
powershell -Command "Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like '*AppBlocker*' } | Remove-Item -Force -ErrorAction SilentlyContinue" >nul 2>&1

echo [5/5] Restoring Policies & Flushing DNS...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f >nul 2>&1
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f >nul 2>&1
ipconfig /flushdns >nul

echo.
echo ========================================================
echo  AppBlocker Service uninstalled and all OS settings
echo  cleanly restored to Windows factory defaults!
echo ========================================================
pause
