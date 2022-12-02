#!/usr/bin/env bash

Iteration=0

while [ $Iteration -lt $TotalIterations ]
do
    echo Iteration $Iteration
    export Iteration
    /usr/share/dotnet/dotnet /app/jellyfin.dll
    ((Iteration++))
done

status=$?