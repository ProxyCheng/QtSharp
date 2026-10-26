# The Linux build/test environment for the QtSharp CI jobs.
#
# The GitHub-hosted Ubuntu runner has none of the X11/GL development headers or the audio/codec
# runtime libraries, and `apt-get install` against its mirror stalls for minutes on individual
# packages often enough to dominate the pipeline. Baking the dependencies into an image moves that
# cost to one image build; every job then starts with the toolchain ready and runs no apt at all.
#
# Ubuntu 24.04 specifically: Qt's Linux kit ships an ffmpeg backend plugin linked against the
# distro's libav* ABI, and 24.04 is the image whose libavcodec60 matches it. PowerShell is the
# repository's driver language on both platforms, so the official tarball is installed.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ARG PS_VERSION=7.4.6

RUN apt-get -o Acquire::Retries=10 -o Acquire::http::Timeout=15 update \
 && apt-get -o Acquire::Retries=10 -o Acquire::http::Timeout=15 install -y --no-install-recommends \
      ca-certificates curl git \
      g++ p7zip-full patchelf xvfb \
      libgl1-mesa-dri libglx-mesa0 libegl1 libopengl0 libfontconfig1 libfreetype6 \
      libdbus-1-3 libx11-xcb1 libxkbcommon-x11-0 libxkbcommon0 \
      libxcb-cursor0 libxcb-icccm4 libxcb-image0 libxcb-keysyms1 libxcb-shape0 \
      libxcb-xinerama0 libxcb-randr0 libxcb-render-util0 libxcb-sync1 libxcb-xfixes0 \
      libxcb-util1 libxcb-xtest0 libxcb-xkb1 libxcb-glx0 libxcb-shm0 \
      libgl1-mesa-dev libegl1-mesa-dev libglib2.0-dev libfontconfig1-dev libfreetype-dev \
      libxkbcommon-dev \
      ffmpeg libpulse0 libasound2t64 \
      libicu74 libgssapi-krb5-2 zlib1g libssl3t64 \
 && curl -fsSL -o /tmp/pwsh.tar.gz \
      "https://github.com/PowerShell/PowerShell/releases/download/v${PS_VERSION}/powershell-${PS_VERSION}-linux-x64.tar.gz" \
 && mkdir -p /opt/microsoft/powershell/7 \
 && tar -xzf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/7 \
 && chmod +x /opt/microsoft/powershell/7/pwsh \
 && ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh \
 && rm /tmp/pwsh.tar.gz \
 && rm -rf /var/lib/apt/lists/*

# Xvfb + software GL is what the GUI suite runs on; the Qt kit is staged by the job, not baked in.
ENV LIBGL_ALWAYS_SOFTWARE=1
