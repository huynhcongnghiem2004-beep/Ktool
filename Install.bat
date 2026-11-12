@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Revit2024.ps1"
if errorlevel 1 (echo INSTALL FAILED. Read the error above.) else (echo Installation verified.)
pause
