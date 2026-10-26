# Android 추가 디코더 빌드

PC 1.2.0에서 사용한 소스 리비전을 고정해 Android용 정적 라이브러리를 빌드합니다.
`sources.lock.json`에는 원본 주소, 리비전, SHA-256이 들어 있습니다.

- Game_Music_Emu: 모든 기본 음악 에뮬레이터와 Nuked YM2612, zlib 지원
- libopenmpt: 원본 Android 소스 목록과 minimp3·stb_vorbis를 사용한 모듈 음악 지원
- dav1d: 8·10·12비트 AV1 소프트웨어 디코딩, 플랫폼별 어셈블리 최적화 제외

Python 3.12 이상, Android NDK `28.2.13676358`, CMake `3.31.6`과 Ninja,
Meson `1.9.2`를 사용합니다. 네 가지 ABI 모두 Android API 26을 대상으로 합니다.

```text
python -m pip install --target <meson-설치-폴더> meson==1.9.2
python native/deps/build_deps.py --ndk <NDK-폴더> --cmake <cmake-실행파일> --meson-tools <meson-설치-폴더>
```

배포본에 포함된 `sources/`의 원본 압축 파일을 먼저 사용하며 해시를 검사합니다.
다른 위치에 이미 받은 원본 압축 파일이 있으면 `--source-cache <압축파일-폴더>`를 추가합니다.
원본이 없으면 잠금 파일에 기록된 주소에서 다운로드하고 해시를 검사합니다.
`--abis arm64-v8a`처럼 특정 ABI만 지정할 수도 있습니다.

결과는 `native/build-deps/<ABI>/`의 `include`, `lib`, `lib/pkgconfig`에 생성됩니다.
임시 소스·빌드 파일과 로그는 `native/deps/work/`에 생성됩니다.
libopenmpt는 원본 `build/android_ndk/Android.mk`의 소스 목록을 CMake로 옮겨
정적 라이브러리를 만들며, 수정된 빌드 정의는 생성 폴더에 남습니다.

완성된 세 라이브러리로 테스트용 Android ELF 실행 파일을 링크해 누락된 심볼과
대상 아키텍처도 검사합니다. 실제 기기의 디코딩 검증은 앱 테스트에서 수행합니다.

FFmpeg의 정적 링크에는 생성된 `.pc` 파일과 NDK의 정적 C++ 런타임을 사용합니다.
원본 라이브러리의 라이선스와 저작권 고지는 소스 압축 파일에 포함되어 있습니다.
앱에도 포함되는 고지 원문과 출처·해시는 `notices.lock.json`에 기록되어 있습니다.
`build-report.json`은 배포 준비 시 수행한 네 ABI의 빌드·링크 검사 결과이며,
실제 기기의 재생 검증 결과와는 구분됩니다.
