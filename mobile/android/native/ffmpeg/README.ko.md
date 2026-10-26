# Android FFmpeg·ffprobe 빌드

타이머신은 FFmpeg를 별도 실행 파일로 호출해 첫 번째 오디오 트랙을 검사하고,
Android 기본 재생기가 처리하지 못한 파일을 PCM WAV로 준비합니다. 썸네일 생성에도
같은 실행 파일을 사용합니다. 외부 링크의 네트워크 통신과 다운로드는 yt-dlp가 담당합니다.

일반적인 앱 빌드는 `app/src/main/jniLibs/<ABI>/`에 포함된 실행 파일을 그대로 사용합니다.
`native/native-artifacts.lock.json`의 원본·바이너리 해시를
`python scripts/verify_native_binaries.py`로 확인할 수 있습니다. 아래 절차는 네이티브
소스를 수정하거나 실행 파일을 직접 다시 만들 때 사용합니다.

## 원본과 빌드 설정

Windows 1.2.0에서 사용한 FFmpeg 리비전 `9a9b70bb8c`의 원본을
`FFmpeg-9a9b70bb8c.tar.gz`로 함께 제공합니다. SHA-256은 다음과 같으며
`Build-FFmpeg.sh`가 빌드 전에 검사합니다.

```text
afce1d0954ff7b6186f8971790bb5523ecede9aedcc045b36730feb35cdc8116
```

수정 사항은 원본에 패치하지 않고 빌드 스크립트에 기록했습니다. Android NDK
`28.2.13676358`(r28c), Android API 26을 사용하며 `arm64-v8a`,
`armeabi-v7a`, `x86_64`, `x86`을 대상으로 합니다.

FFmpeg의 기본 디코더·디먹서·필터와 zlib를 사용합니다. PC에서 사용한
Game_Music_Emu, libopenmpt, dav1d도 같은 소스 리비전으로 정적 링크합니다.
추가 디코더의 원본·해시·수정된 빌드 정의는 [../deps](../deps/README.ko.md)에 있습니다.
OpenMPT에 포함된 minimp3·stb_vorbis와 NDK의 정적 C++ 런타임도 함께 링크됩니다.

앱에서 사용하는 출력은 PCM WAV와 PNG이므로 인코더는 `pcm_s16le,png`,
먹서는 `wav,pcm_s16le,image2`로 설정했습니다. 프로토콜은
`file,pipe,crypto,data`이며 FFmpeg 자체의 네트워크 기능은 사용하지 않습니다.
`--disable-gpl --disable-version3 --disable-nonfree --disable-autodetect`를
지정해 FFmpeg의 LGPL 2.1 이상 조건으로 빌드합니다. 별도 디코더와 컴파일러 런타임에는
각 구성 요소의 라이선스도 적용됩니다.

x86 빌드에는 위치 독립 실행 파일을 생성하기 위해 `--disable-inline-asm`도 지정합니다.
이 설정은 어셈블리 최적화 사용 여부를 바꾸며 지원 형식은 유지합니다.

## 빌드 순서

명령은 저장소의 `mobile/android` 폴더에서 실행합니다. 먼저 Python 3.12 이상,
NDK, CMake 3.31.6과 같은 폴더에 있는 Ninja, Meson 1.9.2를 준비하고
[추가 디코더 빌드](../deps/README.ko.md)를 완료합니다. 해당 결과는
`native/build-deps/<ABI>/`에 있어야 합니다.

Linux에서는 Bash, make, C 컴파일러, pkg-config, tar, sha256sum도 필요합니다.
다음 예시의 SDK·도구 경로는 설치 위치에 맞게 지정합니다.

```sh
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358"
python3 -m pip install --target /tmp/timerchine-meson meson==1.9.2
python3 native/deps/build_deps.py \
  --ndk "$ANDROID_NDK_HOME" \
  --cmake "$ANDROID_HOME/cmake/3.31.6/bin/cmake" \
  --meson-tools /tmp/timerchine-meson
export TIMERCHINE_NATIVE_JOBS=8
bash native/ffmpeg/Build-FFmpeg.sh
```

Windows에서는 추가 디코더를 PowerShell에서 빌드한 뒤 Git Bash 또는 MSYS2의 Bash로
`Build-FFmpeg.sh`를 실행합니다. FFmpeg 구성 검사에 사용할 Windows 호스트용
C 컴파일러와 pkg-config 호환 도구를 별도로 지정합니다. 배포 준비 빌드는
LLVM-MinGW의 clang과 MSYS2의 pkgconf를 사용했습니다. NDK의 Android용 clang은
Android 실행 파일을 빌드하는 데 사용됩니다.

```sh
export ANDROID_NDK_HOME='C:/Android/Sdk/ndk/28.2.13676358'
export TIMERCHINE_HOST_CC='C:/Toolchains/llvm-mingw/bin/clang.exe'
export TIMERCHINE_PKG_CONFIG='C:/msys64/ucrt64/bin/pkgconf.exe'
export TIMERCHINE_NATIVE_JOBS=8
bash native/ffmpeg/Build-FFmpeg.sh
```

Windows의 make는 NDK에 포함된 실행 파일을 스크립트가 선택합니다. Bash 환경의
tar와 sha256sum도 사용합니다. 소스 및 도구 경로에 공백이 없는 작업 폴더를 사용하면
FFmpeg의 셸 기반 구성 검사를 재현하기 쉽습니다.

첫 인자로 ABI 목록을 지정하면 일부 아키텍처만 빌드할 수 있습니다.

```sh
bash native/ffmpeg/Build-FFmpeg.sh 'arm64-v8a x86_64'
```

`TIMERCHINE_FFMPEG_BUILD_DIR`는 임시 빌드 폴더, `TIMERCHINE_NATIVE_JOBS`는
make 병렬 작업 수를 바꿉니다. 기본 임시 폴더는 `native/ffmpeg/build/<ABI>/`입니다.
빌드 구성과 검사 결과는 각 빌드 폴더의 `FFmpeg-9a9b70bb8c/ffbuild/`에서 확인할 수 있습니다.

완성된 실행 파일은 다음 경로로 복사되고 strip 처리됩니다.

```text
app/src/main/jniLibs/<ABI>/libffmpeg.so
app/src/main/jniLibs/<ABI>/libffprobe.so
```

파일 이름의 `.so`는 Android 패키지 설치 시 실행 가능한 네이티브 파일로 추출하기 위한
이름이며, 실제 파일은 CLI 실행 파일입니다. Gradle의 `useLegacyPackaging` 설정으로
설치된 네이티브 라이브러리 폴더에 추출합니다. Python·QuickJS 실행 파일은 고정된
youtubedl-android AAR에서 별도 제공됩니다.

## 수정과 재배포

FFmpeg의 원본 라이선스는 [LICENSE.md](LICENSE.md), LGPL 원문은
[COPYING.LGPLv2.1](COPYING.LGPLv2.1)에 있습니다. 타이머신의 Android 릴리스 소스에는
원본 압축 파일, 추가 디코더 원본, 이 빌드 스크립트와 고지 원문을 함께 제공합니다.
`native/toolchain/`에는 정적 링크한 NDK 컴파일러 런타임의 고지를 보관합니다.

FFmpeg나 추가 디코더를 수정한 경우 같은 순서로 실행 파일을 다시 만들고 APK를 자체 키로
서명할 수 있습니다. 빌드 도구·NDK와 원 제작자의 서명 개인키는 APK 소스에 포함하지 않습니다.
Android 앱 전체 빌드와 서명 절차는 [빌드 안내](../../docs/BUILD.ko.md)에 있습니다.
