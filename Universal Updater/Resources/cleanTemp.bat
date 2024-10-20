@echo off
set tempPath=%1
set outputLog=%2

icacls "%~dp0..\%tempPath%" /grant Everyone:F /T
rmdir /S /Q "%~dp0..\%tempPath%"
