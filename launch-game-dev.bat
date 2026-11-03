@echo off
set "GAME_EXE=C:\Users\liand\Downloads\Cities-Skylines-2-SteamRIP.com\Cities Skylines II\Cities2.exe"

if not exist "%GAME_EXE%" (
    echo Error: Game executable not found at "%GAME_EXE%"
    pause
    exit /b 1
)

echo Starting Cities: Skylines II in Developer Mode (UI Inspector on http://localhost:9444 or custom debug port)...
start "" "%GAME_EXE%" -developerMode --uiDeveloperMode --uiDebugPort=9222
