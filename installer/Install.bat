@echo off
REM Double-click launcher for the Chill More Todo Text installer GUI.
REM Bypasses execution policy for this one script only (does not change system settings).
setlocal
set "PS1=%~dp0Install-ChillMoreTodoText.ps1"

where pwsh >nul 2>nul
if %errorlevel%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
)
endlocal
