@echo off
set Iteration=0
:Loop
echo Iteration %Iteration%
"c:\Program Files\dotnet\dotnet.exe" "%~dp0\jellyfin.dll"
if not defined TotalIterations (
    exit /b 0
)
set /a Iteration=%Iteration%+1
if %Iteration% lss %TotalIterations% goto :Loop
