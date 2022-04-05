# Little script to build and run the Jellyfin Server with the necessary tools
# and dependencies. Used for ASP.NET and .NET Core benchmarking on Windows.

[CmdletBinding(PositionalBinding=$false)]
Param(
  [switch] $clean,
  [switch] $appr2r,
  [switch] $appcomposite,
  [switch] $appavx2,
  [switch] $netcorecomposite,
  [switch] $includeaspnet,
  [switch] $aspnetcomposite,
  [switch] $onebigcomposite
)
Set-Variable BasePath -option Constant -value (Get-Location).ToString()
Set-Variable OutputPath -option Constant -value "${BasePath}\Jellyfin.Server\bin\Release\net6.0\win-x64"
Set-Variable WinDotnetSharedPath -option Constant -value "C:\Program Files (x86)\dotnet\shared"

$crossgen2Path   = ""
$jellyfinWebPath = ""
$ffmpegLibPath   = ""


# Clean the repository without losing your already copied prime material.

function Clean-Repo()
{
}

# Function to ensure Crossgen2, Jellyfin-Web, and FFMPEG are present.

function Check-And-SetDependencies()
{
  $tempLookup = Get-ChildItem -Path "${BasePath}\*crossgen2*"
  if ($tempLookup -eq $null)
  {
    Write-Host "Your custom build of crossgen2 is needed but could not be found."
    exit(-1)
  }
  $crossgen2Dir = tempLookup[0]
  $crossgen2Path = "${crossgen2Dir}\crossgen2.dll"

  $tempLookup = Get-ChildItem -Path "${BasePath}\jellyfin-web"
  if ($tempLookup -eq $null)
  {
    Write-Host "The Jellyfin Web Client is required to build the Jellyfin Server."
    exit(-1)
  }
  $jellyfinWebPath = $tempLookup[0]

  $tempLookup = Get-ChildItem -Path "${BasePath}\*ffmpeg*"
  if ($tempLookup -eq $null)
  {
    Write-Host "The Jellyfin Server requires the FFMPEG library to work."
    exit(-1)
  }
  $ffmpegLibPath = $tempLookup[0]
}


# Build the Jellyfin Server according to the parameters given to this script.

function Build-Server()
{
  $publishCmdSB = [System.Text.StringBuilder]('dotnet publish --configuration Release')
  $publishCmdSB.Append(' --runtime win-x64')
  $publishCmdSB.Append(' -p:DebugSymbols=false;DebugType=none')

  if ($onebigcomposite)
  {
    $publishCmdSB.Append(' --self-contained')
  }
  else
  {
    $publishCmdSB.Append(' --no-self-contained')
  }

  $publishCmdSB.Append(" -p:PublishReadyToRun=${appr2r}")
  $publishCmdSB.Append(" -p:PublishReadyToRunComposite=${appcomposite}")

  if ($appavx2)
  {
    $publishCmdSB.Append(' -p:PublishReadyToRunCrossgen2ExtraArgs=--instruction-set:avx2%3b--inputbubble')
  }

  $publishCmd = $publishCmdSB.ToString()
  cd .\Jellyfin.Server\
  # Write-Host "`n$publishCmd`n"
  Invoke-Expression "cmd.exe /k `"$publishCmd && exit`""
  cd ..\

  if (-not $onebigcomposite) { Do-Crossgen2 }
  Copy-Item -Path $jellyfinWebPath -Destination $OutputPath -Recurse
  Copy-Item -Path "${ffmpegLibPath}\*" -Destination $OutputPath -Recurse
}


# Apply Crossgen2 to build the composite images.

function Do-Crossgen2()
{
  $NetCorePath = (Get-ChildItem -Path $WinDotnetSharedPath -Filter "System.Private.CoreLib.dll" -Recurse)[-1].DirectoryName
  $AspNetCorePath = (Get-ChildItem -Path $WinDotnetSharedPath -Filter "Microsoft.AspNetCore.dll" -Recurse)[-1].DirectoryName

  # Write-Host $NetCorePath
  # Write-Host $AspNetCorePath

  if ($netcorecomposite)
  {
    $netcoreCmdSB = [System.Text.StringBuilder]("dotnet ${crossgen2Path}")
    $netcoreCmdSB.Append(' --composite')
    $netcoreCmdSB.Append(' --targetos:Windows')
    $netcoreCmdSB.Append(' --targetarch:x64')

    if ($appavx2)
    {
      $netcoreCmdSB.Append(' --instruction-set:avx2')
      $netcoreCmdSB.Append(' --inputbubble')
    }

    $netcoreCmdSB.Append(" ${NetCorePath}\*.dll")
    $compositeFile = 'framework'

    if ($includeaspnet)
    {
      $netcoreCmdSB.Append(" ${AspNetCorePath}\*.dll")
      $compositeFile = 'framework-aspnet'
    }

    $netcoreCmdSB.Append(" -o:${OutputPath}\${compositeFile}.r2r.dll")
    $netcoreCmd = $netcoreCmdSB.ToString()
    # Write-Host "`n$netcoreCmd`n"
    Invoke-Expression "cmd.exe /k `"$netcoreCmd && exit`""
  }
  else
  {
    Copy-Item -Path "${NetCorePath}\*" -Destination $OutputPath -Include "*.dll"
  }

  if ($aspnetcomposite -and (-not $includeaspnet))
  {
    $aspnetCmdSB = [System.Text.StringBuilder]("dotnet ${crossgen2Path}")
    $aspnetCmdSB.Append(" -o:${OutputPath}\aspnetcore.r2r.dll")
    $aspnetCmdSB.Append(' --composite')
    $aspnetCmdSB.Append(' --targetos:Windows')
    $aspnetCmdSB.Append(' --targetarch:x64')

    if ($appavx2)
    {
      $aspnetCmdSB.Append(' --instruction-set:avx2')
      $aspnetCmdSB.Append(' --inputbubble')
    }

    $aspnetCmdSB.Append(" ${AspNetCorePath}\*.dll")
    $aspnetCmdSB.Append(" -r:${NetCorePath}\*.dll")
    $aspnetCmd = $aspnetCmdSB.ToString()
    Write-Host "`n$aspnetCmd`n"
    # Invoke-Expression "cmd.exe /k `"$aspnetCmd && exit`""
  }
}


# Script

# Check-And-SetDependencies
# Build-Server
# Do-Crossgen2


# Testing and Debugging Stuff

# Write-Host BasePath

# $s = Get-Location
# $s.ToString()
# 
# $t = Get-ChildItem -Path "$s\*server*" -Directory
# if ($t -eq $null) { Write-Host "1 - No dirs found." }
# 
# $u = Get-ChildItem -Path "$s\*serverlol*" -Directory
# if ($u -eq $null) { Write-Host "2 - No dirs found." }

