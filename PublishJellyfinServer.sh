#!/usr/bin/env bash

# Build configuration
NETCORE_COMPOSITE=false
NETCORE_INCLUDE_ASPNET=false
ASPNET_COMPOSITE=false
APP_R2R=true
APP_COMPOSITE=false
SELF_CONTAINED=true

OUTPUT_DIR=/jellyfin
rm -rf $OUTPUT_DIR
mkdir $OUTPUT_DIR

# Identify .NET Core and ASP.NET framework locations
DOTNET_ROOT=/usr/share/dotnet
find $DOTNET_ROOT -name System.Private.CoreLib.dll
SPC_PATH=`find $DOTNET_ROOT/shared -name System.Private.CoreLib.dll`
ASP_PATH=`find $DOTNET_ROOT/shared -name Microsoft.AspNetCore.dll`
NETCORE_PATH=$(dirname "${SPC_PATH}")
ASPNETCORE_PATH=$(dirname "${ASP_PATH}")

echo "Dotnet root:             $DOTNET_ROOT"
echo "Using .NET Core path:    $NETCORE_PATH"
echo "Using ASP.NET Core path: $ASPNETCORE_PATH"
echo "Output directory:        $OUTPUT_DIR"
echo ".NET Core composite:     $NETCORE_COMPOSITE"
echo ".NET Core + ASP.NET:     $NETCORE_INCLUDE_ASPNET"
echo "ASP.NET composite:       $ASPNET_COMPOSITE"
echo "Jellyfin composite:      $APP_COMPOSITE"

#  /p:PublishReadyToRunCrossgen2ExtraArgs=--inputbubble%3b--instruction-set:avx2

# First publish the app as non-self-contained; we'll inject
# the compiled framework to it later.
PUBLISH_CMD="dotnet publish Jellyfin.Server -p:PublishReadyToRun=true"
# because of changes in docker and systemd we need to not build in parallel at the moment
# see https://success.docker.com/article/how-to-reserve-resource-temporarily-unavailable-errors-due-to-tasksmax-setting
# PUBLISH_CMD+=" --disable-parallel"
PUBLISH_CMD+=" --configuration Release"
PUBLISH_CMD+=" --runtime linux-x64"
PUBLISH_CMD+=" -p:DebugSymbols=false;DebugType=none"
PUBLISH_CMD+=" --output $OUTPUT_DIR"
PUBLISH_CMD+=" --self-contained $SELF_CONTAINED"
# PUBLISH_CMD+=" -p:PublishReadyToRun=$APP_R2R"
# PUBLISH_CMD+=" -p:PublishReadyToRunComposite=$APP_COMPOSITE"

echo "Publishing Jellyfin.Server: $PUBLISH_CMD"
$PUBLISH_CMD
