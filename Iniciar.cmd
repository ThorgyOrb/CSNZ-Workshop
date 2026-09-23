@echo off
cd /d "%~dp0"
dotnet run --project CSNZWorkshop\CSNZWorkshop.csproj -c Release
if errorlevel 1 pause
