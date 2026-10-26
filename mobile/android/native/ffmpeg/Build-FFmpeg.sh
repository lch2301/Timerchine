#!/usr/bin/env bash
set -euo pipefail

# Static LGPL FFmpeg CLI: the desktop app uses decoding/probing, not external encoders.
here=$(cd "$(dirname "$0")" && pwd)
project=$(cd "$here/../.." && pwd)
ndk=${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME to Android NDK r28c (28.2.13676358)}
archive="$here/FFmpeg-9a9b70bb8c.tar.gz"
source_directory=FFmpeg-9a9b70bb8c
expected=afce1d0954ff7b6186f8971790bb5523ecede9aedcc045b36730feb35cdc8116
actual=$(sha256sum "$archive" | cut -d' ' -f1)
test "$actual" = "$expected" || { echo 'FFmpeg source checksum mismatch' >&2; exit 1; }
case "$(uname -s)" in
    MINGW*|MSYS*) host=windows-x86_64; ndk=$(cygpath -m "$ndk"); executable=.exe; make_command="$ndk/prebuilt/windows-x86_64/bin/make.exe"; export PATH="/usr/bin:$PATH"; export MSYS2_ARG_CONV_EXCL='*' ;;
    Darwin*) host=darwin-x86_64; executable=; make_command=make ;;
    *) host=linux-x86_64; executable=; make_command=make ;;
esac
toolchain="$ndk/toolchains/llvm/prebuilt/$host"
if test "$host" = windows-x86_64; then
    mkdir -p "$here/build/tmp"
    export TMPDIR=$(cygpath -m "$here/build/tmp")
fi
jobs=${TIMERCHINE_NATIVE_JOBS:-8}
abis=${1:-"arm64-v8a armeabi-v7a x86_64 x86"}
for abi in $abis; do
    assembly_options=(--disable-x86asm)
    case "$abi" in
        arm64-v8a) arch=aarch64; target=aarch64-linux-android26; cpu=armv8-a ;;
        armeabi-v7a) arch=arm; target=armv7a-linux-androideabi26; cpu=armv7-a ;;
        x86_64) arch=x86_64; target=x86_64-linux-android26; cpu=x86-64 ;;
        x86) arch=x86; target=i686-linux-android26; cpu=i686; assembly_options+=(--disable-inline-asm) ;;
        *) echo "Unsupported ABI: $abi" >&2; exit 1 ;;
    esac
    work="${TIMERCHINE_FFMPEG_BUILD_DIR:-$here/build}/$abi"
    prefix="$project/native/build-deps/$abi"
    if test "$host" = windows-x86_64; then prefix=$(cygpath -m "$prefix"); fi
    export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
    export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
    mkdir -p "$work"
    if ! test -f "$work/$source_directory/configure"; then tar -xzf "$archive" -C "$work"; fi
    cd "$work/$source_directory"
    bash ./configure \
        --target-os=android --enable-cross-compile --arch="$arch" --cpu="$cpu" \
        --cc="$toolchain/bin/clang$executable --target=$target" \
        --host-cc="${TIMERCHINE_HOST_CC:-cc}" \
        --cxx="$toolchain/bin/clang++$executable --target=$target" \
        --ld="$toolchain/bin/clang$executable --target=$target" \
        --ar="$toolchain/bin/llvm-ar$executable" \
        --ranlib="$toolchain/bin/llvm-ranlib$executable" \
        --nm="$toolchain/bin/llvm-nm$executable" \
        --strip="$toolchain/bin/llvm-strip$executable" \
        --sysroot="$toolchain/sysroot" \
        --pkg-config="${TIMERCHINE_PKG_CONFIG:-pkg-config}" --pkg-config-flags=--static \
        --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib -static-libstdc++" \
        --extra-libs="-lc++_static -lc++abi -lunwind -lz -lm -ldl" \
        --enable-libgme --enable-libopenmpt --enable-libdav1d \
        --extra-version=9a9b70bb8c-timerchine-android \
        --enable-static --disable-shared --enable-pic --enable-small \
        --disable-gpl --disable-version3 --disable-nonfree --disable-autodetect --enable-zlib \
        --disable-debug --disable-doc --disable-network --disable-avdevice \
        "${assembly_options[@]}" --disable-programs --enable-ffmpeg --enable-ffprobe \
        --disable-encoders --enable-encoder=pcm_s16le,png \
        --disable-muxers --enable-muxer=wav,pcm_s16le,image2 \
        --disable-protocols --enable-protocol=file,pipe,crypto,data
    "$make_command" -j"$jobs"
    destination="$project/app/src/main/jniLibs/$abi"
    mkdir -p "$destination"
    cp ffmpeg "$destination/libffmpeg.so"
    cp ffprobe "$destination/libffprobe.so"
    if test "$host" = windows-x86_64; then
        "$toolchain/bin/llvm-strip$executable" "$(cygpath -m "$destination/libffmpeg.so")" "$(cygpath -m "$destination/libffprobe.so")"
    else
        "$toolchain/bin/llvm-strip$executable" "$destination/libffmpeg.so" "$destination/libffprobe.so"
    fi
done
