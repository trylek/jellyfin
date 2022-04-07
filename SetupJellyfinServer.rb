# Little script to build and run the Jellyfin Server with the necessary tools
# and dependencies. Used for ASP.NET and .NET Core benchmarking on Windows.

require 'optparse'
require 'fileutils'
require 'find'
require 'pathname'
require 'open-uri'

DOTNET_TARGET = '7.0'

BASE_PATH = Dir.pwd
OUTPUT_PATH = "#{BASE_PATH}/Jellyfin.Server/bin/Release/net#{DOTNET_TARGET}/win-x64"
WINDOTNET_SHARED_PATH = 'C:/Program Files (x86)/dotnet/shared'
NIGHTLY_PATH = "#{BASE_PATH}/DotnetNightly"
RUNTIME_ARTIFACTS_PATH = "#{BASE_PATH}/../runtime/artifacts/bin/coreclr/windows.x64.Checked"

$options = {}
$crossgen2_path = ""
$jellyfin_web_path = ""
$ffmpeg_libs_path = ""


# Clean the repo while keeping the imported prime material.

def clean
  backup_place = "#{BASE_PATH}/../Jellyfin-Resources"
  FileUtils.mkdir_p(backup_place) unless Dir.exist?(backup_place)

  backup_dir("*crossgen2*", backup_place)
  backup_dir("*jellyfin-web*", backup_place)
  backup_dir("*ffmpeg*", backup_place)
  system('git clean -fdx')

  print "\n"
  Dir.children(backup_place).each do |folder|
    puts "Restoring #{folder}..."
    FileUtils.cp_r("#{backup_place}/#{folder}", BASE_PATH)
  end
end


# Backup a directory if it's present.

def backup_dir(name, backup_path)
  puts("\nSearching for #{name} folders to back up...")
  dirs_existing = Dir.glob(name)
  return if dirs_existing.empty?

  dirs_existing.each do |folder|
    puts("Backing up #{folder} to #{backup_path}/#{folder}...")
    FileUtils.cp_r(folder, backup_path, remove_destination: true)
  end
end


# Ensure Crossgen2, Jellyfin-Web, and FFMPEG are present in the jellyfin folder.

def check_and_set_dependecies
  puts "\nChecking for dependencies..."
  sleep(2) if $options[:stepped]

  dirs_existing = Dir.glob("*crossgen2*")
  if (dirs_existing.empty?) then
    puts "\nYour custom build of crossgen2 is needed but could not be found."
    return -1
  end

  $crossgen2_path = "#{dirs_existing[0]}/crossgen2.dll"
  puts "\nFound crossgen2 in #{$crossgen2_path}"

  dirs_existing = Dir.glob("jellyfin-web")
  if (dirs_existing.empty?) then
    puts "\nThe Jellyfin Web Client is required to build the Jellyfin Server."
    return -1
  end

  $jellyfin_web_path = dirs_existing[0]
  puts "Found jellyfin-web in #{$jellyfin_web_path}"

  dirs_existing = Dir.glob("*ffmpeg*")
  if (dirs_existing.empty?) then
    puts "\nThe Jellyfin Server requires the FFMPEG library to work."
    return -1
  end

  $ffmpeg_libs_path = dirs_existing[0]
  puts "Found the FFMPEG libraries and binaries in #{$ffmpeg_libs_path}."
  return 0
end


# Build the Jellyfin Server according to the parameters given to this script.

def build_server
  publish_cmd = 'dotnet publish --configuration Release --runtime win-x64'
  publish_cmd << ' -p:DebugSymbols=false;DebugType=none'

  if ($options[:onebigcomposite]) then
    publish_cmd << ' --self-contained'
  else
    publish_cmd << ' --no-self-contained'
  end

  publish_cmd << " -p:PublishReadyToRun=#{$options[:appr2r].to_s}"
  publish_cmd << " -p:PublishReadyToRunComposite=#{$options[:appcomposite].to_s}"

  if ($options[:appavx2]) then
    publish_cmd << ' -p:PublishReadyToRunCrossgen2ExtraArgs=--instruction-set:avx2%3b--inputbubble'
  end

  puts "\nGoing to build and publish the Jellyfin Web Server with the following command:"
  puts "\n#{publish_cmd}\n\n"
  sleep(2) if $options[:stepped]

  Dir.chdir('Jellyfin.Server') do
    system("cmd.exe /k \"#{publish_cmd} & exit\"")
  end

  do_crossgen2() unless $options[:onebigcomposite]

  puts "\nCopying jellyfin-web and FFMPEG to the server's path..."
  sleep(2) if $options[:stepped]

  FileUtils.cp_r($jellyfin_web_path, OUTPUT_PATH, remove_destination: true)
  FileUtils.cp_r("#{$ffmpeg_libs_path}/.", OUTPUT_PATH, remove_destination: true)
end


# Apply Crossgen2 to build the composite images.

def do_crossgen2
  netcore_path = find_newest_dotnetdll_basepath('System.Private.CoreLib.dll')
  aspnet_path = find_newest_dotnetdll_basepath('Microsoft.AspNetCore.dll')
  # dotnet_resources_path = "#{BASE_PATH}/../MiscellaneousTools/PrimeMaterial/DotnetResources"

  puts "\nInstalled .NET Core Path: #{netcore_path}"
  puts "Installed ASP.NET Core Path: #{aspnet_path}\n"

  # FileUtils.cp_r(netcore_path, dotnet_resources_path)
  # FileUtils.mv("#{dotnet_resources_path}/6.0.3",
  #              "#{dotnet_resources_path}/NetCoreShared",
  #               force: true)

  # FileUtils.cp_r(aspnet_path, dotnet_resources_path)
  # FileUtils.mv("#{dotnet_resources_path}/6.0.3",
  #              "#{dotnet_resources_path}/AspNetCoreShared",
  #               force: true)

  sleep(2) if $options[:stepped]

  if ($options[:netcorecomposite]) then
    puts 'Going to now apply the provided custom Crossgen2 to build the composite images...'
    print "\n"

    netcore_cmd = "dotnet #{$crossgen2_path} --composite"
    netcore_cmd << ' --targetos Windows'
    netcore_cmd << ' --targetarch x64'

    netcore_cmd << ' --instruction-set avx2 --inputbubble' if $options[:appavx2]

    # netcore_cmd << " #{dotnet_resources_path}/NetCoreShared/*.dll"
    netcore_cmd << " \"#{netcore_path}/*.dll\""
    composite_file = 'framework'

    if ($options[:includeaspnet]) then
      puts 'Also going to compile ASP.NET...'
      # netcore_cmd << " #{dotnet_resources_path}/AspNetCoreShared/*.dll"
      netcore_cmd << " \"#{aspnet_path}/*.dll\""
      composite_file = 'framework-aspnet'
    end
    netcore_cmd << " --out #{OUTPUT_PATH}/#{composite_file}.r2r.dll"

    puts "\nBuilding .NET Core Composite Images with the following command:\n"
    puts "#{netcore_cmd}\n"
    sleep(2) if $options[:stepped]
    system("cmd.exe /k \"#{netcore_cmd} & exit\"")

  else
    puts "\nNo .NET Core Composites requested..."
    puts "Copying the DLL's from #{netcore_path}..."
    sleep(2) if $options[:stepped]
    FileUtils.cp_r("#{netcore_path}/.", OUTPUT_PATH, remove_destination: true)
  end

  if ($options[:aspnetcomposite] and (not $options[:includeaspnet]))
    puts "Going to compile ASP.NET...\n"
    aspnet_cmd = "dotnet #{$crossgen2_path} --out #{OUTPUT_PATH}/aspnetcore.r2r.dll"
    aspnet_cmd << ' --composite'
    aspnet_cmd << ' --targetos Windows'
    aspnet_cmd << ' --targetarch x64'

    aspnet_cmd << ' --instruction-set avx2 --inputbubble' if $options[:appavx2]

    aspnet_cmd << " #{aspnet_path}/*.dll"
    aspnet_cmd << " --reference #{netcore_path}/*.dll"

    puts "\nBuilding ASP.NET Composite Images with the following command:\n"
    puts "#{aspnet_cmd}\n"
    sleep(2) if $options[:stepped]
    system("cmd.exe /k \"#{aspnet_cmd} & exit\"")
  end
end


# Search for the shared dotnet directories where the given dll is.

def find_newest_dotnetdll_basepath(dllname)
  paths = []
  Find.find(WINDOTNET_SHARED_PATH) do |path|
    paths << path if path.match?(dllname)
  end
  return Pathname.new(paths[-1]).dirname
end


# Download the latest nightly build of the runtime.

def fetch_and_prepare_daily_runtime(version)
  return if Dir.exist?(NIGHTLY_PATH)
  FileUtils.mkdir_p(NIGHTLY_PATH)

  url = "https://aka.ms/dotnet/#{version}/daily/dotnet-sdk-win-x64.zip"
  dl_name = ""
  # puts "\nDownloading the nightly runtime build to #{NIGHTLY_PATH}..."

  URI.open(url) do |download|
    dl_name = download.base_uri.to_s.split('/')[-1]
    IO.copy_stream(download, "#{NIGHTLY_PATH}/#{dl_name}")
  end

  # puts "Extracting #{dl_name} now..."
  Dir.chdir(NIGHTLY_PATH) do
    system("cmd.exe /k \"tar -xf #{dl_name} & exit\"")
  end
end


# For analyzing the methods where R2R Resolution failed, we require a Checked
# version of the runtime.

def patch_checked_clr
  Dir.chdir(RUNTIME_ARTIFACTS_PATH) do
    FileUtils.cp_r(%w(coreclr.dll System.Private.CoreLib.dll),
                   "#{NIGHTLY_PATH}/shared/Microsoft.NETCore.App/7.0.0-preview.4.22201.3",
                    remove_destination: true)
  end
end


# Run the Jellyfin Server!

def run_server
  runcmd = ""
  ready_to_run = $options[:readytorun] ? '1' : '0'
  tiered_compilation = $options[:tieredcompilation] ? '1' : '0'

  runcmd << "set COMPlus_ReadyToRun=#{ready_to_run}"
  runcmd << " && set COMPlus_TieredCompilation=#{tiered_compilation}"
  runcmd << " && #{NIGHTLY_PATH}/dotnet.exe"
  runcmd << " #{OUTPUT_PATH}/jellyfin.dll"
  # runcmd << "#{NIGHTLY_PATH}/dotnet.exe"
  # runcmd << " #{OUTPUT_PATH}/jellyfin.dll"

  puts "\nAbout to run: #{runcmd}\n"
  sleep(2)
  Dir.chdir(OUTPUT_PATH) do
    system("cmd.exe /k \"#{runcmd} & exit\"")
    # system(runcmd)
  end
end


# Script

opts_parser = OptionParser.new do |opts|
  $options[:clean] = false
  opts.on('--clean', 'Clean the entire repository without losing the prime material.') do
    $options[:clean] = true
  end

  $options[:stepped] = false
  opts.on('--stepped', 'Wait 2 seconds before executing next command.') do
    $options[:stepped] = true
  end

  $options[:build] = false
  opts.on('--build', 'Build the Jellyfin Server and required composites.') do
    $options[:build] = true
  end

  $options[:run] = false
  opts.on('--run TYPE', 'Run the previously built Jellyfin Server.') do |v|
    # $options[:run] = true
    if ['BENCHMARK', 'RESOLUTION'].include?(v.upcase) then
      $options[:run] = v
    else
      puts "\nThe currently supported Run Types are: BENCHMARK, RESOLUTION"
      exit(-3)
    end
  end

  $options[:appr2r] = false
  opts.on('--appr2r', 'APP_R2R flag help goes here.') do
    $options[:appr2r] = true
  end

  $options[:appcomposite] = false
  opts.on('--appcomposite', 'APP_COMPOSITE flag help goes here.') do
    $options[:appcomposite] = true
  end

  $options[:appavx2] = false
  opts.on('--appavx2', 'APP_AVX2 flag help goes here.') do
    $options[:appavx2] = true
  end

  $options[:netcorecomposite] = false
  opts.on('--netcorecomposite', 'NETCORE_COMPOSITE flag help goes here.') do
    $options[:netcorecomposite] = true
  end

  $options[:includeaspnet] = false
  opts.on('--includeaspnet', 'NETCORE_INCLUDE_ASPNET flag help goes here.') do
    $options[:includeaspnet] = true
  end

  $options[:aspnetcomposite] = false
  opts.on('--aspnetcomposite', 'ASPNET_COMPOSITE flag help goes here.') do
    $options[:aspnetcomposite] = true
  end

  $options[:onebigcomposite] = false
  opts.on('--onebigcomposite', 'ONE_BIG_COMPOSITE flag help goes here.') do
    $options[:onebigcomposite] = true
  end

  $options[:readytorun] = false
  opts.on('--readytorun', 'Sets COMPlus_ReadyToRun env variable.') do
    $options[:readytorun] = true
  end

  $options[:tieredcompilation] = false
  opts.on('--tieredcompilation', 'Sets COMPlus_TieredCompilation env variable.') do
    $options[:tieredcompilation] = true
  end

  $options[:tieredcompilation] = false
  opts.on('--tieredcompilation', 'Sets COMPlus_TieredCompilation env variable.') do
    $options[:tieredcompilation] = true
  end

end
opts_parser.parse!

# puts $options
# exit(0)

# Perform repo cleaning if its flag is passed.
if ($options[:clean]) then
  clean()
  exit(0)
end

# Assert only building or running is set at once.
if ($options[:build] and $options[:run]) then
  puts "You cannot build and run at the same time. It has to be done by steps :)"
  exit(-2)
end

# Building the server was requested.
if ($options[:build]) then
  ready = check_and_set_dependecies()

  if (ready == -1) then
    puts "\nExiting...\n"
    exit(-1)
  end
  build_server()
  exit(0)
end

# Running the server was requested.
if ($options[:run]) then
  dotnet_version = '7.0.1xx'
  fetch_and_prepare_daily_runtime(dotnet_version)
  patch_checked_clr() if $options[:run].match?('RESOLUTION')
  run_server()
  exit(143)
end

