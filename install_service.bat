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

if not exist "%SERVICE_EXE%" (
    echo [INFO] Building solution first...
    dotnet build "%~dp0AppBlocker.sln" -c Debug
)

echo [1/3] Creating Windows Service: %SERVICE_NAME%...
sc.exe create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto DisplayName= "AppBlocker Enforcement Engine"

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
echo  AppBlocker Background Service installed and active!
echo ========================================================
pause
