@echo off
echo Building MTG Simulator...
dotnet build MTGSimulator/MTGSimulator.csproj --configuration Release

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build successful! Starting application...
echo.
dotnet run --project MTGSimulator/MTGSimulator.csproj --configuration Release

pause

