# 타이머신 Android 제3자 구성 요소 안내

타이머신 Android 자체 소스의 적용 라이선스는 저장소 루트 [LICENSE](../../LICENSE)의 GNU GPL v3, **GPL-3.0-only**입니다. 다음 구성 요소의 저작권과 라이선스도 각각 적용됩니다.

## 앱에 포함되는 구성 요소

| 구성 요소 | 용도 | 라이선스·자료 |
| --- | --- | --- |
| NanumSquareRound | 한글 표시용 글꼴 | SIL Open Font License 1.1. [네이버 나눔글꼴](https://hangeul.naver.com/font)에서 제공하는 글꼴과 고지 |
| Nunito | 숫자·라틴 문자 표시용 글꼴 | SIL Open Font License 1.1. [원본 글꼴 저장소](https://github.com/googlefonts/nunito) |
| yt-dlp | 2026.10.21, 외부 링크 추출·다운로드 | Unlicense 및 포함 EJS의 MIT·ISC 고지. [공식 원본](https://github.com/yt-dlp/yt-dlp/tree/2026.10.21) |
| youtubedl-android | 0.18.1, Android 런타임 초기화 | GPL v3. [원본](https://github.com/yausername/youtubedl-android/tree/0.18.1) |
| CPython | 3.12.11 | Python Software Foundation License와 함께 제공되는 고지 |
| QuickJS | 2025-04-26, JavaScript 처리 | MIT |
| FFmpeg·ffprobe | 리비전 9a9b70bb8c, 범용 오디오 디코딩·분석·썸네일 | LGPL 2.1 이상. [원본과 빌드 설정](native/ffmpeg/README.ko.md) |
| Game_Music_Emu | 게임 음악 디코딩, Nuked YM2612 포함 | LGPL 2.1 이상 및 포함 구성 요소 고지. [원본·빌드 설정](native/deps/README.ko.md) |
| libopenmpt | 모듈 음악 디코딩, minimp3·stb_vorbis 포함 | BSD 3-Clause·Boost Software License 및 포함 구성 요소 고지. [원본·빌드 설정](native/deps/README.ko.md) |
| dav1d | AV1 소프트웨어 디코딩 | BSD 2-Clause. [원본·빌드 설정](native/deps/README.ko.md) |

Python 런타임은 OpenSSL 3.5.2, PyCryptodome 3.23.0, Expat 2.7.3, zlib 1.3.1, libffi, liblzma 5.8.1, SQLite 3.50.4, bzip2 1.0.8, ncurses와 Termux의 Android 호환 라이브러리·C++ 런타임을 포함합니다. 각 구성 요소의 저작권·라이선스 원문은 `app/src/main/assets/licenses/external/`에 있습니다. Java 실행 의존성인 AndroidX, Kotlin 표준 라이브러리, Jackson, Apache Commons에도 각각의 Apache·MIT 등의 조건이 적용됩니다.

FFmpeg에 정적 링크한 추가 디코더의 고지 원문도 같은 `external/` 폴더에 포함합니다. NDK r28c의 libc++·libc++abi·libunwind 등 컴파일러 런타임 고지는 APK와 `native/toolchain/`에 보관합니다. 빌드 시 Java 실행 의존성의 실제 라이선스·NOTICE를 수집해 `licenses/THIRD_PARTY_NOTICES.txt`로 합치며, 앱의 라이선스 화면에서 확인할 수 있습니다. 네이티브 원본 압축 파일·해시·빌드 스크립트는 Android 릴리스의 소스 자료에 함께 제공합니다.

AndroidX, Kotlin 표준 라이브러리 1.7.22, JetBrains annotations 13.0, Guava listenablefuture 1.0에는 Apache License 2.0이 적용됩니다. 이 구성 요소처럼 원본 AAR·JAR에 라이선스 파일이 없는 경우에도 공식 라이선스 원문을 `external/`에 별도로 보관해 APK 고지에 포함합니다. 원문 출처와 해시는 [고지 출처 목록](docs/licenses/runtime-license-sources.json)에 기록되어 있습니다. 전체 실행 의존성의 모듈·버전 목록은 빌드 시 생성되는 APK의 `licenses/dependencies/runtime-dependencies.json`에서 확인할 수 있습니다.

포함된 글꼴의 원문 고지는 `app/src/main/assets/licenses/`의 `OFL.txt`, `Nunito-OFL.txt`, `Nunito-NOTICE.txt`에 보관되어 APK에도 함께 들어갑니다. 앱 자체의 GPL 원문은 같은 폴더의 `LICENSE.txt`로 포함됩니다. 글꼴을 수정하거나 별도로 재배포할 때는 OFL의 글꼴 이름과 라이선스 유지 조건을 따라 주세요. 타이머신 전체의 GPL 조건이 해당 글꼴의 OFL을 대체하지 않습니다.

`Nunito-NOTICE.txt`에는 Windows 1.2.0용 숫자 글꼴을 정적 글꼴로 생성한 경위를 보존했습니다. Android에서도 이 글꼴 파일을 그대로 사용합니다. 고지 안의 `Assets/Fonts/Source/Nunito-variable.ttf`는 **저장소 루트 기준 경로**이며, [원본 가변 글꼴](../../Assets/Fonts/Source/Nunito-variable.ttf)은 Android 릴리스의 소스 자료에도 같은 경로로 포함됩니다. Android에 실제 포함되는 글꼴은 `app/src/main/assets/fonts/`에 있습니다.

Android 버전은 Android 시스템의 미디어·알림·파일 선택 API와 Android용 Python·QuickJS·FFmpeg·yt-dlp를 사용합니다. Windows용 NAudio·Deno·.NET 런타임 실행 파일은 포함하지 않습니다. Python 번들에서 앱이 사용하지 않는 readline·history·gdbm과 Mutagen 태그 기록 모듈은 `scripts/strip_python_runtime.py`로 제외합니다. 이 변환의 원본 AAR은 SHA-256으로 고정되며, 추출기·네트워크·암호화 기능과 심볼릭 링크 정보는 보존합니다.

## 소스를 빌드할 때 사용하는 도구

| 구성 요소 | 버전 | 라이선스 |
| --- | --- | --- |
| Gradle Wrapper | 8.13 | Apache License 2.0. [포함된 라이선스 원문](docs/licenses/Gradle-LICENSE.txt), [NOTICE](docs/licenses/Gradle-NOTICE.txt), [Gradle 원본](https://github.com/gradle/gradle/tree/v8.13.0) |
| Android Gradle Plugin | 8.13.2 | Apache License 2.0. [Android 빌드 도구 소스](https://android.googlesource.com/platform/tools/base/) |
| JUnit | 4.13.2 | Eclipse Public License 1.0. [JUnit 라이선스](https://github.com/junit-team/junit4/blob/r4.13.2/LICENSE-junit.txt) |
| Hamcrest Core | 1.3 | BSD 3-Clause. [Hamcrest 라이선스](https://github.com/hamcrest/JavaHamcrest/blob/hamcrest-java-1.3/LICENSE.txt) |

JUnit과 Hamcrest 및 Robolectric은 테스트에만 사용되며 APK의 실행 의존성에 포함되지 않습니다. Gradle Wrapper를 제외한 빌드 도구와 테스트 의존성은 빌드 시 각 공식 저장소에서 내려받습니다. Android SDK와 JDK는 빌드 환경에 별도로 설치합니다.

소스, 빌드 스크립트와 라이선스 원문은 같은 Android 릴리스의 소스 자료에서 확인할 수 있습니다. 수정판을 배포할 경우 수령자가 해당 수정판의 소스를 받고 빌드할 수 있도록 GPL에서 요구하는 소스와 수정·빌드 정보를 함께 제공해 주세요. 원 제작자의 APK 서명 개인키는 배포 자료에 포함되지 않으며, 수정판은 자체 키로 서명할 수 있습니다.
