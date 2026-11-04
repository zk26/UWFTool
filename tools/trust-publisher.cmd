@echo off
rem Trust the UWFTool code-signing certificate, so UAC shows the publisher name.
rem Pure ASCII on purpose: batch files are parsed in the console codepage, and a
rem non-ASCII path here would break on some locales.
setlocal
title UWFTool - trust publisher

rem Works in both layouts: right next to UWFtool.exe (release package) or in tools\ (source tree)
set "EXE=%~dp0UWFtool.exe"
if not exist "%EXE%" set "EXE=%~dp0..\dist\UWFtool.exe"
if not exist "%EXE%" (
  echo [ERROR] UWFtool.exe not found. Put this file in the same folder as UWFtool.exe,
  echo         or keep it in tools\ next to the dist\ folder.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0trust-publisher.ps1" -Exe "%EXE%"
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" (
  echo [FAILED] exit code %RC% - read the messages above.
) else (
  echo [DONE] UAC will show the publisher name now.
)
pause
exit /b %RC%
