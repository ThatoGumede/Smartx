@echo off
rem Starts the API, the device simulator and the dashboard, each in its own window.
set ROOT=%~dp0
start "Smart-X API" cmd /k "cd /d %ROOT%src\SmartX.Api && dotnet run"
timeout /t 8 /nobreak > nul
start "Smart-X Simulator" cmd /k "cd /d %ROOT%src\SmartX.DeviceSimulator && dotnet run"
start "Smart-X Client" cmd /k "cd /d %ROOT%src\SmartX.Client && dotnet run"
timeout /t 10 /nobreak > nul
start http://localhost:5100
