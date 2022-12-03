setlocal

set TargetOS=%1
set BinDir=%~dp0\bin
set AppName=jellyfin
set PublishDir=%BinDir%\Release\net7.0\%TargetOS%-x64\publish
set AppDir=%PublishDir%\app
set Dotnet="C:\Program Files\dotnet\dotnet.exe"
set DotnetAppRoot=\git\runtime6
set DotnetRoot=\git\runtime6
set RunCG2Path=%DotnetAppRoot%\artifacts\bin\runcg2\Debug\runcg2.dll
set TestArtifacts=%DotnetRoot%\artifacts\tests\coreclr\Windows.x64.Release\Tests\Core_Root
set RunCrossgen2=%Dotnet% %DotnetRoot%\artifacts\bin\coreclr\Windows.x64.Release\crossgen2\crossgen2.dll -O 
set CG2Dir=%PublishDir%\CG2
set CG2Dir2=%PublishDir%\CG2b
set CompositeFileList=%3
set CompositeFileCount=%4

if "%TargetOS%" == "win" (
    set OSComponent=Windows
    set LibPrefix=
    set LibSuffix=.dll
    rem set Crossgen2ExtraOptions=--pdb
) else if "%TargetOS%" == "linux" (
    set OSComponent=Linux
    set LibPrefix=lib
    set LibSuffix=.so
    set Crossgen2ExtraOptions=--perfmap
) else (
    echo Invalid target OS: !TargetOS!
    goto :ERROR
)

set RunCrossgen2=%RunCrossgen2% --targetos %OSComponent% %Crossgen2ExtraOptions%

if not "%2" == "init" goto :InitDone

rmdir /s /q %BinDir%

%Dotnet% build -c Release

if %ERRORLEVEL% neq 0 goto :ERROR

%Dotnet% publish -c Release -r %TargetOS%-x64 /p:PublishReadyToRun=true

if %ERRORLEVEL% neq 0 (
    echo Publishing error
    goto :ERROR
)

echo App successfully published to %PublishDir%
exit /b 0

:InitDone

rmdir /s /q %CG2Dir%
rmdir /s /q %CG2Dir2%
rmdir /s /q %AppDir%

mkdir %CG2Dir%
mkdir %CG2Dir2%
mkdir %AppDir%
mkdir %AppDir%\jellyfin-web

xcopy /s /q /y %~dp0\..\jellyfin-web\*.* %AppDir%\jellyfin-web\
xcopy /q /y %PublishDir%\*.* %AppDir%\

rem xcopy /q /y %TestArtifacts%\System.Private.CoreLib.dll %AppDir%\
if defined PatchRuntime (
    xcopy /q /y %TestArtifacts%\*.* %AppDir%\
)

if "%2" == "default-r2r" goto :DefaultR2R
if "%2" == "runtime+asp.net.composite" goto :RuntimeAndASPNETComposite
if "%2" == "runtime.composite" goto :RuntimeComposite
if "%2" == "runtime.composite+asp.net.composite" goto :RuntimeCompositeAndASPNETComposite
if "%2" == "full.composite" goto :FullComposite
if "%2" == "cross-module-inlining" goto :CrossModuleInlining

echo Invalid build mode: '%2'
:ERROR
exit /b 1

:DefaultR2R
echo Compiling in default mode assembly by assembly

%Dotnet% %RunCG2Path% %PublishDir% "%CompositeFileList%" "%CompositeFileCount%" %RunCrossgen2%

goto :DockerBuild

:CrossModuleInlining
echo Compiling with --opt-cross-module and --opt-async-methods

%Dotnet% %RunCG2Path% %PublishDir% %RunCrossgen2% --opt-cross-module:* --opt-async-methods --verify-type-and-field-layout:false

goto :DockerBuild

:RuntimeComposite

xcopy /q %PublishDir%\*.dll %CG2Dir%

del /Q %CG2Dir%\%AppName%.*
del /Q %CG2Dir%\Microsoft*.*

set CrossgenCmd=call %RunCrossgen2%
set CrossgenCmd=%CrossgenCmd% --composite
set CrossgenCmd=%CrossgenCmd% -o %AppDir%\framework-r2r.dll
set CrossgenCmd=%CrossgenCmd% %CG2Dir%\*.dll

echo Building runtime composite^: %CrossgenCmd%

%CrossgenCmd%

goto :DockerBuild

:RuntimeAndASPNETComposite

xcopy /q %PublishDir%\*.dll %CG2Dir%

del /Q %CG2Dir%\%AppName%.*

set CrossgenCmd=call %RunCrossgen2%
set CrossgenCmd=%CrossgenCmd% --composite
set CrossgenCmd=%CrossgenCmd% -o %AppDir%\framework-aspnet-r2r.dll
set CrossgenCmd=%CrossgenCmd% %CG2Dir%\*.dll

echo Building runtime+ASP.NET composite^: %CrossgenCmd%

%CrossgenCmd%

goto :DockerBuild

:RuntimeCompositeAndASPNETComposite

xcopy /q %PublishDir%\*.dll %CG2Dir%
xcopy /q %PublishDir%\Microsoft*.dll %CG2Dir2%

del /Q %CG2Dir%\%AppName%.*
del /Q %CG2Dir%\Microsoft*.*

set CrossgenCmd=call %RunCrossgen2%
set CrossgenCmd=%CrossgenCmd% --composite
set CrossgenCmd=%CrossgenCmd% -o %AppDir%\framework-r2r.dll
set CrossgenCmd=%CrossgenCmd% %CG2Dir%\*.dll

echo Building runtime composite and a separate ASP.NET composite^: %CrossgenCmd%

%CrossgenCmd%

set CrossgenCmd=call %RunCrossgen2%
set CrossgenCmd=%CrossgenCmd% --composite
set CrossgenCmd=%CrossgenCmd% -o %AppDir%\aspnet-r2r.dll
set CrossgenCmd=%CrossgenCmd% %CG2Dir2%\*.dll
set CrossgenCmd=%CrossgenCmd% -r %CG2Dir%\*.dll

echo Building ASP.NET composite^: %CrossgenCmd%

%CrossgenCmd%

goto :DockerBuild

:FullComposite

xcopy /q %PublishDir%\*.dll %CG2Dir%

set CrossgenCmd=call %RunCrossgen2%
set CrossgenCmd=%CrossgenCmd% --composite
set CrossgenCmd=%CrossgenCmd% -o %AppDir%\framework-r2r.dll
set CrossgenCmd=%CrossgenCmd% %CG2Dir%\*.dll

echo Building full composite^: %CrossgenCmd%

%CrossgenCmd%

goto :DockerBuild

:DockerBuild

rem xcopy /Y %TestArtifacts%\coreclr.dll %AppDir%\
rem xcopy /Y %TestArtifacts%\clr*.dll %AppDir%\
rem xcopy /Y %TestArtifacts%\CoreShim.dll %AppDir%\
xcopy /Y %~dp0\runapp.cmd %AppDir%
\util\dos2unix -n %~dp0\runapp.sh %AppDir%\runapp.sh

if "%TargetOS%" == "win" (
    set DOCKER_BUILDKIT=0
    set COMPOSE_DOCKER_CLI_BUILD=0
)

if "%UseContainers%" == "1" (
    docker build -f Dockerfile.%TargetOS% .
)
