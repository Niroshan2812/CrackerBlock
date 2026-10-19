@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Must be run as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=AppBlockerService

echo Stopping %SERVICE_NAME%...
sc.exe stop "%SERVICE_NAME%"

echo Deleting %SERVICE_NAME%...
sc.exe delete "%SERVICE_NAME%"

echo ========================================================
echo  AppBlocker Background Service uninstalled.
echo ========================================================
pause
