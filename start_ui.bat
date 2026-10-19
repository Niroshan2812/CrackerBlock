@echo off
set UI_EXE=%~dp0AppBlocker.UI\bin\Debug\net9.0-windows\AppBlocker.UI.exe

if not exist "%UI_EXE%" (
    echo Building AppBlocker.UI...
    dotnet build "%~dp0AppBlocker.sln"
)

start "" "%UI_EXE%"
