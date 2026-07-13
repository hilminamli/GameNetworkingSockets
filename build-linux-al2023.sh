#!/usr/bin/env bash
# Build libGameNetworkingSockets.so inside an Amazon Linux 2023 container so it links
# against AL2023's glibc (2.34) — the exact runtime of the EC2 rendezvous host. A .so
# built on Ubuntu 24.04 (glibc 2.39) fails there with "GLIBC_2.38 not found".
#
# Run from the fork root on the Docker host:
#     docker run --rm -v "$PWD":/src -w /work amazonlinux:2023 bash /src/build-linux-al2023.sh
#
# Output: /src/bindings/csharp/native/linux-x64/libGameNetworkingSockets.so
# (also copied to /src/build-out/ for convenience).
# NOTE: no `pipefail` — commands like `ldd --version | head -1` make the left side
# get SIGPIPE when head closes early, which under pipefail aborts the whole script
# right after the first echo (the "stops after glibc" symptom). Keep -e -u only.
set -eu

echo "=== glibc of this build environment (must be <= EC2's 2.34) ==="
ldd --version | head -1 || true

echo "=== install toolchain (AL2023 packages) ==="
# No -q / no >/dev/null: a hidden dnf failure under `set -e` would silently abort
# the whole script right here (that was the earlier "stops after glibc" symptom).
dnf -y install \
    gcc gcc-c++ make cmake ninja-build git \
    pkgconfig autoconf automake libtool \
    perl-core zip unzip tar which \
    openssl-devel protobuf-devel protobuf-compiler
echo "=== toolchain installed OK ==="

# vcpkg needs a writable checkout; build deps from source so they also target glibc 2.34.
echo "=== bootstrap vcpkg ==="
export VCPKG_ROOT=/work/vcpkg
if [ ! -x "$VCPKG_ROOT/vcpkg" ]; then
    git clone --quiet https://github.com/microsoft/vcpkg.git "$VCPKG_ROOT"
    "$VCPKG_ROOT/bootstrap-vcpkg.sh" -disableMetrics >/dev/null
fi

# Clone the source INTO the container fs instead of cp -r from the Windows bind mount
# (/src). A recursive copy of the fork over the Docker Desktop file-sharing layer hangs
# on the webrtc/abseil submodules (thousands of files) — clone is fast and stays on the
# fast container fs. The mount is used only to write the output .so back out at the end.
echo "=== clone source into container (avoids slow bind-mount copy) ==="
rm -rf /work/gns
git clone --quiet --depth 1 \
    https://github.com/hilminamli/GameNetworkingSockets.git /work/gns
cd /work/gns
# Native ICE (USE_STEAMWEBRTC=OFF) needs abseil + vjson but NOT the huge webrtc
# submodule — init only what's required so the clone stays small and fast.
git submodule update --init --depth 1 src/external/abseil src/external/vjson 2>/dev/null || \
    git submodule update --init --depth 1 2>/dev/null || true
rm -rf build-linux vcpkg_installed

# Pin vcpkg baseline to this checkout's HEAD (avoids "failed to git show baseline").
HEAD=$(cd "$VCPKG_ROOT" && git rev-parse HEAD)
sed -i "s/\"builtin-baseline\": \".*\"/\"builtin-baseline\": \"$HEAD\"/" vcpkg.json
echo "vcpkg baseline pinned to $HEAD"

echo "=== configure (shared .so, ICE on, deps static-linked in) ==="
cmake -S . -B build-linux -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake" \
    -DVCPKG_TARGET_TRIPLET=x64-linux \
    -DBUILD_SHARED_LIB=ON \
    -DBUILD_STATIC_LIB=OFF \
    -DBUILD_EXAMPLES=OFF \
    -DBUILD_TESTS=OFF \
    -DBUILD_TOOLS=OFF \
    -DENABLE_ICE=ON \
    -DUSE_STEAMWEBRTC=OFF \
    -DProtobuf_USE_STATIC_LIBS=ON \
    -DCMAKE_BUILD_TYPE=Release

echo "=== build ==="
cmake --build build-linux

SO=$(find build-linux -name 'libGameNetworkingSockets.so' -type f 2>/dev/null | { head -n1 || true; })
if [ -z "$SO" ] || [ ! -f "$SO" ]; then
    echo "!!! BUILD FAILED: no libGameNetworkingSockets.so produced under build-linux"
    exit 1
fi
echo "=== built: $SO ==="
ls -la "$SO"

echo "=== max glibc this .so requires (must be <= 2.34 for EC2) ==="
MAXGLIBC=$(objdump -T "$SO" | grep -oE 'GLIBC_[0-9.]+' | sort -V | { tail -1 || true; })
echo "max GLIBC: ${MAXGLIBC:-none}"

echo "=== external deps (should be ONLY libc/libstdc++/libm/libgcc — no libssl/libprotobuf) ==="
ldd "$SO" || true

echo "=== copy out ==="
mkdir -p /src/bindings/csharp/native/linux-x64 /src/build-out
cp "$SO" /src/bindings/csharp/native/linux-x64/libGameNetworkingSockets.so
cp "$SO" /src/build-out/libGameNetworkingSockets.so
echo "DONE → /src/build-out/libGameNetworkingSockets.so"
