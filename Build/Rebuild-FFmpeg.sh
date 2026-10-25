#!/usr/bin/env bash
# Build the retained FFmpeg source with the original BtbN target/variant recipes.
set -euo pipefail
if [[ $# != 2 ]]; then
    echo "Usage: $0 /path/to/third-party-source-materials /path/to/new-output" >&2
    exit 2
fi
materials=$(realpath "$1")
source_tar="$materials/sources/FFmpeg-9a9b70bb8c.tar.gz"
[[ -f "$source_tar" && -d "$materials/ffmpeg-builds/scripts.d" ]] || {
    echo "The extracted 1.2.0 third-party source materials are required." >&2; exit 2;
}
[[ ! -e "$2" ]] || { echo "Output directory must not already exist." >&2; exit 2; }
actual=$(sha256sum "$source_tar" | cut -d ' ' -f 1)
[[ "$actual" == afce1d0954ff7b6186f8971790bb5523ecede9aedcc045b36730feb35cdc8116 ]] || {
    echo "Unexpected FFmpeg source archive SHA256." >&2; exit 2;
}
mkdir -p "$2"
output=$(realpath "$2")
cp -a "$materials/ffmpeg-builds" "$output/recipes"
mkdir -p "$output/build/ffmpeg"
tar -xf "$source_tar" -C "$output/build/ffmpeg" --strip-components=1
# makeimage builds pinned dependencies from the retained recipes. It requires
# Docker/buildx and network access to upstream build tools and dependency mirrors.
(
    cd "$output/recipes"
    ./makeimage.sh win64 lgpl 9.0
)
cat > "$output/build-source.sh" <<'BUILD'
set -euo pipefail
cd /ffbuild/ffmpeg
./configure --prefix=/ffbuild/prefix --pkg-config-flags=--static \
    $FFBUILD_TARGET_FLAGS $FF_CONFIGURE \
    --extra-cflags="${FF_CFLAGS:-}" --extra-cxxflags="${FF_CXXFLAGS:-}" \
    --extra-libs="${FF_LIBS:-}" --extra-ldflags="${FF_LDFLAGS:-}" \
    --extra-ldexeflags="${FF_LDEXEFLAGS:-}" \
    --cc="$CC" --cxx="$CXX" --ar="$AR" --ranlib="$RANLIB" --nm="$NM" \
    --extra-version=20261024
make -j"$(nproc)" V=1
make install install-doc
BUILD
image=${TIMERCHINE_FFMPEG_IMAGE:-ghcr.io/btbn/ffmpeg-builds/win64-lgpl-9.0:latest}
docker run --rm -i -u "$(id -u):$(id -g)" \
    --mount "type=bind,source=$output/build,target=/ffbuild" \
    --mount "type=bind,source=$output/build-source.sh,target=/build-source.sh,readonly" \
    "$image" bash /build-source.sh
echo "Built tools: $output/build/prefix/bin"
