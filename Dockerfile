ARG DOTNET_VERSION=7.0

FROM node:lts-alpine as web-builder
ARG JELLYFIN_WEB_VERSION=master
RUN apk add curl git zlib zlib-dev autoconf g++ make libpng-dev gifsicle alpine-sdk automake libtool make gcc musl-dev nasm python3 \
 && curl -L https://github.com/jellyfin/jellyfin-web/archive/${JELLYFIN_WEB_VERSION}.tar.gz | tar zxf - \
 && cd jellyfin-web-* \
 && npm ci --no-audit --unsafe-perm \
 && mv dist /dist

FROM debian:buster-slim as app

# https://askubuntu.com/questions/972516/debian-frontend-environment-variable
ARG DEBIAN_FRONTEND="noninteractive"
# http://stackoverflow.com/questions/48162574/ddg#49462622
ARG APT_KEY_DONT_WARN_ON_DANGEROUS_USAGE=DontWarn
# https://github.com/NVIDIA/nvidia-docker/wiki/Installation-(Native-GPU-Support)
ENV NVIDIA_DRIVER_CAPABILITIES="compute,video,utility"

# https://github.com/intel/compute-runtime/releases
ARG GMMLIB_VERSION=20.3.2
ARG IGC_VERSION=1.0.5435
ARG NEO_VERSION=20.46.18421
ARG LEVEL_ZERO_VERSION=1.0.18421

# Install dependencies:
# mesa-va-drivers: needed for AMD VAAPI. Mesa >= 20.1 is required for HEVC transcoding.
RUN apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -y ca-certificates gnupg wget apt-transport-https \
 && wget -O - https://repo.jellyfin.org/jellyfin_team.gpg.key | apt-key add - \
 && echo "deb [arch=$( dpkg --print-architecture )] https://repo.jellyfin.org/$( awk -F'=' '/^ID=/{ print $NF }' /etc/os-release ) $( awk -F'=' '/^VERSION_CODENAME=/{ print $NF }' /etc/os-release ) main" | tee /etc/apt/sources.list.d/jellyfin.list \
 && apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -y \
   mesa-va-drivers \
   jellyfin-ffmpeg \
   openssl \
   locales \
# Intel VAAPI Tone mapping dependencies:
# Prefer NEO to Beignet since the latter one doesn't support Comet Lake or newer for now.
# Do not use the intel-opencl-icd package from repo since they will not build with RELEASE_WITH_REGKEYS enabled.
 && mkdir intel-compute-runtime \
 && cd intel-compute-runtime \
 && wget https://github.com/intel/compute-runtime/releases/download/${NEO_VERSION}/intel-gmmlib_${GMMLIB_VERSION}_amd64.deb \
 && wget https://github.com/intel/intel-graphics-compiler/releases/download/igc-${IGC_VERSION}/intel-igc-core_${IGC_VERSION}_amd64.deb \
 && wget https://github.com/intel/intel-graphics-compiler/releases/download/igc-${IGC_VERSION}/intel-igc-opencl_${IGC_VERSION}_amd64.deb \
 && wget https://github.com/intel/compute-runtime/releases/download/${NEO_VERSION}/intel-opencl_${NEO_VERSION}_amd64.deb \
 && wget https://github.com/intel/compute-runtime/releases/download/${NEO_VERSION}/intel-ocloc_${NEO_VERSION}_amd64.deb \
 && wget https://github.com/intel/compute-runtime/releases/download/${NEO_VERSION}/intel-level-zero-gpu_${LEVEL_ZERO_VERSION}_amd64.deb \
 && dpkg -i *.deb \
 && cd .. \
 && rm -rf intel-compute-runtime \
 && apt-get remove gnupg wget apt-transport-https -y \
 && apt-get clean autoclean -y \
 && apt-get autoremove -y \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /cache /config /media \
 && chmod 777 /cache /config /media \
 && sed -i -e 's/# en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen && locale-gen

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8
ENV LANGUAGE en_US:en

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} as builder
WORKDIR /repo
COPY . .
COPY crossgen2 /root/crossgen2
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
# because of changes in docker and systemd we need to not build in parallel at the moment
# see https://success.docker.com/article/how-to-reserve-resource-temporarily-unavailable-errors-due-to-tasksmax-setting
# RUN dotnet publish Jellyfin.Server --disable-parallel --configuration Release --output="/jellyfin" --self-contained --runtime linux-x64 "-p:DebugSymbols=false;DebugType=none" /p:PublishReadyToRun=true

ARG APP_R2R=false
ARG APP_COMPOSITE=false
ARG APP_AVX2=false
ARG NETCORE_COMPOSITE=false
ARG NETCORE_INCLUDE_ASPNET=false
ARG ASPNET_COMPOSITE=false
ARG ONE_BIG_COMPOSITE=false

ENV APP_R2R_VALUE=$APP_R2R
ENV APP_COMPOSITE_VALUE=$APP_COMPOSITE
ENV APP_AVX2_VALUE=$APP_AVX2
ENV NETCORE_COMPOSITE_VALUE=$NETCORE_COMPOSITE
ENV NETCORE_INCLUDE_ASPNET_VALUE=$NETCORE_INCLUDE_ASPNET
ENV ASPNET_COMPOSITE_VALUE=$ASPNET_COMPOSITE
ENV ONE_BIG_COMPOSITE_VALUE=$ONE_BIG_COMPOSITE

RUN ./PublishJellyfinServer.sh

FROM app

COPY --from=builder /jellyfin /jellyfin
COPY --from=web-builder /dist /jellyfin/jellyfin-web
# COPY Core_Root /Core_Root

RUN apt-get update -y \
 && apt-get install -y wget \
 && cd / \
 && mkdir dotnet7p \
 && cd dotnet7p \
 && wget https://aka.ms/dotnet/7.0.1xx/daily/dotnet-sdk-linux-x64.tar.gz \
 && tar -xf dotnet-sdk-linux-x64.tar.gz \
 && cd ..

EXPOSE 8096
VOLUME /cache /config /media
# RUN ls ./jellyfin
ENTRYPOINT ["./dotnet7p/dotnet", "/jellyfin/jellyfin.dll", \
    "--datadir", "/config", \
    "--cachedir", "/cache", \
    "--ffmpeg", "/usr/lib/jellyfin-ffmpeg/ffmpeg"]

# wget https://aka.ms/dotnet/7.0/daily/dotnet-runtime-deps-x64.deb
# wget https://aka.ms/dotnet/7.0/daily/dotnet-host-x64.deb
# wget https://aka.ms/dotnet/7.0/daily/dotnet-apphost-pack-x64.deb
# wget https://aka.ms/dotnet/7.0/daily/dotnet-hostfxr-x64.deb
# wget https://aka.ms/dotnet/7.0/daily/dotnet-targeting-pack-x64.deb
# wget https://aka.ms/dotnet/7.0/daily/dotnet-runtime-x64.deb
# wget https://aka.ms/dotnet/7.0.1xx/daily/dotnet-sdk-x64.deb

