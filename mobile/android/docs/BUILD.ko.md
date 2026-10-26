# 타이머신 Android 소스 빌드

## 도구와 소스

이 소스는 JDK 17, Gradle 8.13, Android Gradle Plugin 8.13.2, Android SDK Platform 36과 Build Tools 36.0.0을 사용합니다. 최소 실행 환경은 Android 8.0(API 26)이며 대상 API는 36입니다. 앱 코드는 Java로 작성되며 외부 링크를 위해 youtubedl-android 0.18.1의 Python·QuickJS와 전이 의존성을 사용합니다. 범용 오디오 디코딩용 FFmpeg·ffprobe의 네 ABI 실행 파일과 대응하는 원본·빌드 스크립트도 함께 제공합니다.

빌드에는 Python 3.12 이상도 필요합니다. `TIMERCHINE_BUILD_PYTHON` 환경변수에 실행 파일 경로를 지정하거나 `python`이 실행되도록 설정하세요. `scripts/strip_python_runtime.py`는 공식 Maven AAR의 SHA-256을 확인하고 타이머신이 사용하지 않는 대화형 readline·gdbm 모듈과 태그 기록용 Mutagen을 제외합니다. yt-dlp의 추출기·네트워크·암호화·JavaScript 기능은 유지합니다. 원본 AAR에 들어 있던 과거 yt-dlp 리소스는 사용하지 않는 안내 리소스로 바꾸며, 앱은 별도로 고정한 `assets/media/yt-dlp.bin`을 먼저 설치한 뒤 Python을 초기화합니다.

Gradle Wrapper가 Gradle 버전과 다운로드 해시를 지정합니다. 첫 빌드는 Gradle 배포본과 플러그인·테스트 의존성을 Google Maven 및 Maven Central 등 설정된 저장소에서 내려받습니다. SDK는 [Android Studio](https://developer.android.com/studio) 또는 Android 명령줄 도구로 설치할 수 있습니다.

저장소의 `mobile/android` 폴더를 Android Studio에서 열거나, 그 폴더에서 아래 명령을 실행하세요. JDK 경로는 `JAVA_HOME`, Android SDK 경로는 `ANDROID_HOME` 또는 로컬 `local.properties`의 `sdk.dir`로 지정할 수 있습니다. `local.properties`는 기기별 설정이므로 소스에 포함하지 않습니다.

## 네이티브 도구 준비

기본 앱 빌드는 저장소에 포함된 `app/src/main/jniLibs/<ABI>/libffmpeg.so`와 `libffprobe.so`를 사용합니다. `native/native-artifacts.lock.json`에 대응하는 원본과 바이너리 해시가 기록되어 있습니다. 빌드 전 다음 명령으로 포함된 네이티브 실행 파일을 검사할 수 있습니다.

```text
python scripts/verify_native_binaries.py
```

기존 네이티브 실행 파일을 사용할 때는 아래 재빌드 과정을 생략하고 Gradle 테스트·APK 빌드로 진행합니다. 네이티브 소스를 수정하거나 실행 파일을 직접 다시 만들 때는 **추가 디코더 → FFmpeg·ffprobe → Gradle** 순서로 빌드합니다. 원본 압축 파일과 해시는 `native/ffmpeg`와 `native/deps/sources`에 포함되어 있습니다.

### 네이티브 실행 파일을 다시 만드는 경우

Android SDK에 NDK `28.2.13676358`과 CMake `3.31.6`을 설치합니다. CMake 패키지에 포함된 Ninja도 사용합니다. 다음은 Android 명령줄 도구의 `sdkmanager`가 실행되는 환경의 예입니다.

```text
sdkmanager "platforms;android-36" "build-tools;36.0.0" "ndk;28.2.13676358" "cmake;3.31.6"
```

Windows PowerShell에서 추가 디코더를 빌드하는 예입니다. Meson 설치 폴더는 작업 폴더 밖의 도구 폴더로 지정합니다.

```powershell
$env:ANDROID_NDK_HOME = "$env:ANDROID_HOME\ndk\28.2.13676358"
$mesonTools = 'C:\AndroidTools\meson-1.9.2'
python -m pip install --target $mesonTools meson==1.9.2
python native/deps/build_deps.py --ndk $env:ANDROID_NDK_HOME --cmake "$env:ANDROID_HOME\cmake\3.31.6\bin\cmake.exe" --meson-tools $mesonTools
```

그다음 Git Bash 또는 MSYS2의 Bash에서 FFmpeg를 빌드합니다. 호스트용 C 컴파일러와 pkg-config 호환 도구의 실제 경로를 지정하세요. 배포 준비 빌드는 LLVM-MinGW clang과 MSYS2 pkgconf를 사용했습니다.

```sh
export ANDROID_NDK_HOME='C:/Android/Sdk/ndk/28.2.13676358'
export TIMERCHINE_HOST_CC='C:/Toolchains/llvm-mingw/bin/clang.exe'
export TIMERCHINE_PKG_CONFIG='C:/msys64/ucrt64/bin/pkgconf.exe'
export TIMERCHINE_NATIVE_JOBS=8
bash native/ffmpeg/Build-FFmpeg.sh
```

Linux에서는 Bash, make, 호스트 C 컴파일러, pkg-config, tar, sha256sum을 설치한 환경에서 실행합니다.

```sh
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358"
python3 -m pip install --target /tmp/timerchine-meson meson==1.9.2
python3 native/deps/build_deps.py \
  --ndk "$ANDROID_NDK_HOME" \
  --cmake "$ANDROID_HOME/cmake/3.31.6/bin/cmake" \
  --meson-tools /tmp/timerchine-meson
bash native/ffmpeg/Build-FFmpeg.sh
export TIMERCHINE_BUILD_PYTHON="$(command -v python3)"
```

기본값은 네 ABI 모두를 빌드합니다. 추가 디코더 결과는 `native/build-deps/<ABI>/`, 최종 실행 파일은 `app/src/main/jniLibs/<ABI>/libffmpeg.so`와 `libffprobe.so`에 생성됩니다. 중간 생성물은 `native/deps/work/`, `native/ffmpeg/build/`에 남으며 소스 관리 대상에서 제외됩니다. 이후 Gradle은 해당 실행 파일과 Python·QuickJS 런타임을 APK에 패키징하고 구성 요소의 라이선스 고지를 생성합니다.

부분 ABI 빌드, 병렬 작업 수, 소스 해시, 디코더 구성과 라이선스는 [추가 디코더 안내](../native/deps/README.ko.md), [FFmpeg 안내](../native/ffmpeg/README.ko.md), [Python 런타임 안내](../native/python/README.ko.md)에서 확인할 수 있습니다. 현재 스크립트의 전체 네이티브 빌드 경로는 Windows와 Linux를 대상으로 합니다.

## 테스트와 디버그 APK

Windows PowerShell:

```powershell
.\gradlew.bat testDebugUnitTest lintDebug assembleDebug
```

macOS·Linux:

```sh
bash ./gradlew testDebugUnitTest lintDebug assembleDebug
```

디버그 APK는 `app/build/outputs/apk/debug/app-debug.apk`에 생성됩니다. 패키지 이름은 `io.github.lch2301.timerchine.debug`, 표시 버전은 `1.2.0-debug`이며 개발용 키로 서명합니다. 정식 앱과 별도로 설치되고 저장소도 분리되어 정식 앱의 데이터에 영향을 주지 않습니다.

Android 기기나 에뮬레이터가 연결된 환경에서는 다음 명령으로 플랫폼 계측 테스트를 실행할 수 있습니다. 이 테스트는 Android 프레임워크 기반의 전용 러너를 사용하며 AndroidX 테스트 라이브러리를 포함하지 않습니다.

```powershell
.\gradlew.bat connectedDebugAndroidTest
```

테스트 APK는 `.debug` 앱을 대상으로 실행됩니다. 로컬 단위 테스트 `testDebugUnitTest`와 기기 계측 테스트 `connectedDebugAndroidTest`는 별도 작업입니다.

## 릴리스 APK와 서명

```powershell
.\gradlew.bat assembleRelease
```

다음 환경변수 **네 개가 모두 제공되면** 해당 키로 릴리스 APK에 서명합니다. 값은 빌드 파일이나 Git에 저장하지 않습니다.

| 환경변수 | 값 |
| --- | --- |
| `TIMERCHINE_KEYSTORE` | 키 저장소의 절대 경로 |
| `TIMERCHINE_KEYSTORE_PASSWORD` | 키 저장소 비밀번호 |
| `TIMERCHINE_KEY_ALIAS` | 서명 키 별칭 |
| `TIMERCHINE_KEY_PASSWORD` | 키 비밀번호 |

서명 환경변수가 없으면 `app/build/outputs/apk/release/app-release-unsigned.apk`가 생성됩니다. 이 파일은 서명 전에는 설치할 수 없습니다. 일부 변수만 제공하면 구성 오류로 빌드를 중단합니다. 서명된 릴리스의 기본 출력은 `app/build/outputs/apk/release/app-release.apk`입니다.

수정판은 자체 키를 사용해 서명할 수 있습니다. 다른 서명의 앱은 기존 설치에 바로 업데이트할 수 없으므로, 별도의 `applicationId`를 사용하거나 기존 앱을 삭제한 뒤 설치해야 합니다. 삭제 시 앱 데이터도 지워집니다. 같은 앱의 후속 버전은 동일한 키로 서명하고 `versionCode`를 올려야 Android에서 업데이트로 인식합니다.

앱 내부의 빌드 식별자는 진단용 메타데이터이며 초기화 조건으로 사용하지 않습니다. 동일한 `applicationId`와 서명 키로 업데이트하면 타이머·앱 설정·재생 목록·프리셋과 선택 상태, 저장된 재생 위치·순서 및 캐시를 유지합니다. 업데이트 수신기는 저장된 타이머 상태를 바탕으로 알람 예약을 다시 조정합니다. 새 저장 항목은 기본값으로 보완하고 기존 항목은 보존하세요. 서로 다른 빌드로 업데이트한 뒤 전체 저장값과 백업 복구를 검사할 수 있습니다. 디버그·릴리스 앱은 패키지 이름이 다르므로 서로의 데이터를 가져오지 않습니다.

PowerShell 보조 스크립트도 제공합니다.

```powershell
.\scripts\Build-Android.ps1 -Configuration Release -RunTests -RequireSigning
```

`-RequireSigning`은 서명 환경변수가 모두 있는지 확인합니다. 디버그 빌드는 `-Configuration Debug`로 지정합니다.

## 자동 빌드와 배포 자료

저장소의 Android 워크플로는 변경 사항과 Pull Request에서 포함된 네이티브 실행 파일의 해시를 검사하고 단위 테스트·린트·디버그 빌드를 실행해 결과를 Actions 아티팩트로 보관합니다. 계측 테스트 APK도 컴파일하지만, 기기를 사용하는 계측 실행은 별도 작업입니다. 워크플로는 공개 릴리스를 만들거나 공식 서명 키를 사용하지 않습니다.

Android 공개 버전 태그는 `android-v1.2.0`처럼 플랫폼을 표시합니다. Windows 1.2.0의 기존 `v.1.2.0` 태그와 별개입니다. 공개 릴리스에는 서명된 APK, 그 APK의 소스와 빌드 스크립트·라이선스, 파일 해시를 제공합니다. 같은 버전의 소스를 확인하려면 해당 Android 태그를 선택하세요.

관련 문서: [Android Gradle Plugin 호환성](https://developer.android.com/build/releases/agp-8-13-0-release-notes), [앱 서명](https://developer.android.com/studio/publish/app-signing), [GitHub의 Gradle 빌드](https://docs.github.com/en/actions/tutorials/build-and-test-code/java-with-gradle).
