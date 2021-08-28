#!/usr/bin/env bash

# Build configuration
SELF_CONTAINED=true
OUTPUT_DIR=/jellyfin
rm -rf $OUTPUT_DIR
mkdir $OUTPUT_DIR

# Unpack pre-recorded nuget cache to speed up project restorations
cd /
tar -xvf /repo/nuget-cache.tar >/etc/null
cd /repo

# Identify .NET Core and ASP.NET framework locations
DOTNET_ROOT=/usr/share/dotnet
find $DOTNET_ROOT -name System.Private.CoreLib.dll
SPC_PATH=`find $DOTNET_ROOT/shared -name System.Private.CoreLib.dll`
ASP_PATH=`find $DOTNET_ROOT/shared -name Microsoft.AspNetCore.dll`
NETCORE_PATH=$(dirname "${SPC_PATH}")
ASPNETCORE_PATH=$(dirname "${ASP_PATH}")

if [ "${ONE_BIG_COMPOSITE_VALUE,,}" == "true" ]; then
    export APP_R2R_VALUE=true
    export APP_COMPOSITE_VALUE=true
fi

echo "Dotnet root:             $DOTNET_ROOT"
echo "Using .NET Core path:    $NETCORE_PATH"
echo "Using ASP.NET Core path: $ASPNETCORE_PATH"
echo "Output directory:        $OUTPUT_DIR"
echo ".NET Core composite:     $NETCORE_COMPOSITE_VALUE"
echo ".NET Core + ASP.NET:     $NETCORE_INCLUDE_ASPNET_VALUE"
echo "ASP.NET composite:       $ASPNET_COMPOSITE_VALUE"
echo "Jellyfin ReadyToRun:     $APP_R2R_VALUE"
echo "Jellyfin composite:      $APP_COMPOSITE_VALUE"
echo "One big composite:       $ONE_BIG_COMPOSITE_VALUE"
echo "Compile for AVX2:        $APP_AVX2_VALUE"

# First publish the app as non-self-contained; we'll inject
# the compiled framework to it later.
PUBLISH_CMD="dotnet publish Jellyfin.Server"
# because of changes in docker and systemd we need to not build in parallel at the moment
# see https://success.docker.com/article/how-to-reserve-resource-temporarily-unavailable-errors-due-to-tasksmax-setting
PUBLISH_CMD+=" --disable-parallel"
PUBLISH_CMD+=" --configuration Release"
PUBLISH_CMD+=" --runtime linux-x64"
PUBLISH_CMD+=" -p:DebugSymbols=false;DebugType=none"
PUBLISH_CMD+=" --output $OUTPUT_DIR"
if [ "${ONE_BIG_COMPOSITE_VALUE,,}" == "true" ]; then
    PUBLISH_CMD+=" --self-contained"
fi
PUBLISH_CMD+=" -p:PublishReadyToRun=$APP_R2R_VALUE"
PUBLISH_CMD+=" -p:PublishReadyToRunComposite=$APP_COMPOSITE_VALUE"

if [ "${APP_AVX2_VALUE,,}" == "true" ]; then
    PUBLISH_CMD+=" -p:PublishReadyToRunCrossgen2ExtraArgs=--instruction-set:avx2%3b--inputbubble"
fi

echo "Publishing Jellyfin.Server: $PUBLISH_CMD"
$PUBLISH_CMD

if [ "${ONE_BIG_COMPOSITE_VALUE,,}" != "true" ]; then
    # echo "Building an arbitrary tiny app to make dotnet download the Crossgen2 package"
    # dotnet new console -o /testapp
    # dotnet publish /testapp -p:PublishReadyToRun=true -r linux-x64
    # rm -rf /testapp
    
    # Locate crossgen2
    # CROSSGEN2_PATH=`find / -name crossgen2`
    CROSSGEN2_PATH=/root/crossgen2/crossgen2
    
    echo "Using Crossgen2 path: $CROSSGEN2_PATH"
    
    if [ "${NETCORE_COMPOSITE_VALUE,,}" == "true" ]; then
        echo "About to compile fx"
        NETCORE_CMD="$CROSSGEN2_PATH"
        NETCORE_CMD+=" --composite"
        NETCORE_CMD+=" --targetos:Linux"
        NETCORE_CMD+=" --targetarch:x64"
        if [ "${APP_AVX2_VALUE,,}" == "true" ]; then
            NETCORE_CMD+=" --instruction-set:avx2"
            NETCORE_CMD+=" --inputbubble"
        fi
        NETCORE_CMD+=" $NETCORE_PATH/*.dll"
        COMPOSITE_FILE="framework";
        if [ "${NETCORE_INCLUDE_ASPNET_VALUE,,}" == "true" ]; then
            echo "Will also compile asp.net"
            NETCORE_CMD+=" $ASPNETCORE_PATH/*.dll"
            COMPOSITE_FILE="framework-aspnet";
        fi
        NETCORE_CMD+=" -o:$OUTPUT_DIR/$COMPOSITE_FILE.r2r.dll"
        echo "Compiling framework: $NETCORE_CMD"
        $NETCORE_CMD
        ls -l $OUTPUT_DIR/$COMPOSITE_FILE.r2r.dll
    else
        cp $NETCORE_PATH/*.dll $OUTPUT_DIR
    fi
    
    if [[ "${ASPNET_COMPOSITE_VALUE,,}" == "true" && "${NETCORE_INCLUDE_ASPNET_VALUE,,}" != "true" ]]; then
        echo "About to compile asp.net"
        ASPNET_CMD="$CROSSGEN2_PATH"
        ASPNET_CMD+=" -o:$OUTPUT_DIR/aspnetcore.r2r.dll"
        ASPNET_CMD+=" --composite"
        ASPNET_CMD+=" --targetos:Linux"
        ASPNET_CMD+=" --targetarch:x64"
        if [ "${APP_AVX2_VALUE,,}" == "true" ]; then
            ASPNET_CMD+=" --instruction-set:avx2"
            ASPNET_CMD+=" --inputbubble"
        fi
        ASPNET_CMD+=" $ASPNETCORE_PATH/*.dll"
        ASPNET_CMD+=" -r:$NETCORE_PATH/*.dll"
        echo "Compiling ASP.NET Core: $ASPNET_CMD"
        $ASPNET_CMD
        ls -l $OUTPUT_DIR/aspnetcore.r2r.dll
    fi
fi
