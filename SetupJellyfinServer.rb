# Little script to build and run the Jellyfin Server with the necessary tools
# and dependencies. Used for ASP.NET and .NET Core benchmarking on Windows.

require 'optparse'
require 'fileutils'
require 'find'
require 'pathname'

BASE_PATH = Dir.pwd
OUTPUT_PATH = "#{BASE_PATH}/Jellyfin.Server/bin/Release/net6.0/win-x64"
WINDOTNET_SHARED_PATH = 'C:/Program Files (x86)/dotnet/shared'

$options = {}
$crossgen2_path = ""
$jellyfin_web_path = ""
$ffmpeg_libs_path = ""

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
  puts "Found crossgen2 in #{$crossgen2_path}"

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
  puts "#{publish_cmd}\n\n"
  sleep(2) if $options[:stepped]

  Dir.chdir('Jellyfin.Server') do
    system("cmd /k \"#{publish_cmd} & exit\"")
  end

  # do_crossgen2() unless $options[:onebigcomposite]

  puts "\nCopying jellyfin-web and FFMPEG to the server's path..."
  sleep(2) if $options[:stepped]

  FileUtils.cp_r($jellyfin_web_path, OUTPUT_PATH, remove_destination: true)
  FileUtils.cp_r("#{$ffmpeg_libs_path}/.", OUTPUT_PATH, remove_destination: true)
end


# Apply Crossgen2 to build the composite images.

def do_crossgen2
  netcore_path = find_newest_dotnetdll_basepath('System.Private.CoreLib.dll')
  aspnet_path = find_newest_dotnetdll_basepath('Microsoft.AspNetCore.dll')
end


def find_newest_dotnetdll_basepath(dllname)
  paths = []
  Find.find(WINDOTNET_SHARED_PATH) do |path|
    paths << path if path.match?(dllname)
  end
  return Pathname.new(paths[-1]).dirname
end


# Script

opts_parser = OptionParser.new do |opts|
  $options[:stepped] = false
  opts.on('--stepped', 'Wait 2 seconds before executing next command.') do |v|
    $options[:stepped] = true
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
end
opts_parser.parse!

# ready = check_and_set_dependecies()
# if (ready == -1) then
#   puts "\nExiting...\n"
#   exit(-1)
# end
# 
# build_server()
do_crossgen2()

