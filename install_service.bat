@echo off
:: Check Administrator Privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] This installer must be run as Administrator!
    echo Right-click this script and select 'Run as administrator'.
    pause
    exit /b 1
)

set SERVICE_NAME=AppBlockerService
set SERVICE_EXE=%~dp0AppBlocker.Service\bin\Debug\net9.0\AppBlocker.Service.exe

:: Stop running watchdog companion and UI if present so dotnet build can overwrite binaries
if exist "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" (
    "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe" --signal-shutdown >nul 2>&1
)
taskkill /F /IM AppBlocker.Watchdog.exe >nul 2>&1
taskkill /F /IM AppBlocker.UI.exe >nul 2>&1

:: Stop running service if present so dotnet build can overwrite binaries
sc.exe query "%SERVICE_NAME%" 2>&1 | find /I "RUNNING" >nul
if %errorLevel% equ 0 (
    echo [INFO] Stopping running %SERVICE_NAME% to allow updating binaries...
    sc.exe stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 3 /nobreak >nul
)

echo [INFO] Building latest AppBlocker solution (Core, Service, Watchdog, UI)...
dotnet build "%~dp0AppBlocker.sln" -c Debug
if %errorLevel% neq 0 (
    echo [ERROR] Build failed! Please review compilation errors above.
    pause
    exit /b 1
)

:: Ensure Watchdog binary exists alongside Service binary
copy /Y "%~dp0AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.*" "%~dp0AppBlocker.Service\bin\Debug\net9.0\" >nul 2>&1

echo [1/3] Registering Windows Service: %SERVICE_NAME%...
sc.exe query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% neq 0 (
    sc.exe create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto DisplayName= "AppBlocker Enforcement Engine"
) else (
    sc.exe config "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto
)

echo [2/3] Configuring Watchdog and Crash Auto-Recovery...
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe failureflag "%SERVICE_NAME%" 1

echo [3/4] Configuring Database ACL Permissions for UI/Service Concurrency...
if not exist "%ProgramData%\AppBlocker" mkdir "%ProgramData%\AppBlocker"
icacls "%ProgramData%\AppBlocker" /grant "*S-1-5-32-545:(OI)(CI)F" /T /Q >nul 2>&1
icacls "%ProgramData%\AppBlocker" /grant "*S-1-1-0:(OI)(CI)F" /T /Q >nul 2>&1

echo [4/4] Starting %SERVICE_NAME%...
sc.exe start "%SERVICE_NAME%"

echo.
echo ========================================================
echo  AppBlocker Background Service updated and active!
echo ========================================================
pause
