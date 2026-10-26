# Android Python·QuickJS 런타임

이 앱은 Maven Central의 `io.github.junkfood02.youtubedl-android:library:0.18.1`과 `common:0.18.1`을 사용합니다. 원본 AAR의 고정 SHA-256은 `579b5fb480892b1abc2b218c2089699d52759cc8d7ba256bf876453f0365faef`입니다.

`upstream-wrapper`는 원 제작자의 `0.18.1` 태그, 커밋 `d725d5c9a18c3a99a13ee0308bf78275dc310760`에서 가져온 실제 래퍼·common 코드와 관련 빌드 스크립트입니다. 원본 저작권·GPL 전문을 함께 보존합니다. 전체 원본 저장소는 <https://github.com/yausername/youtubedl-android/tree/0.18.1>입니다.

타이머신의 빌드는 `strip-python-runtime.gradle`과 `scripts/strip_python_runtime.py`로 다음 변경을 적용합니다.

- 사용하지 않는 readline·history·gdbm 라이브러리, Python 확장 모듈과 Mutagen 태그 기록 모듈 제외
- 과거 yt-dlp 리소스를 안내 리소스로 교체하고, 앱이 고정·검증한 2026.10.21 엔진을 먼저 설치
- Python ZIP의 Unix 심볼릭 링크와 원본 엔트리 속성 유지, 출력 타임스탬프 고정

Python의 네트워크·암호화 기능과 yt-dlp의 모든 추출기·EJS·QuickJS 실행 기능은 유지합니다. `app/src/main/assets/media/extractor-bootstrap.py`는 추출기를 독립된 프로세스 그룹으로 실행하며, 취소 시 해당 추출기와 JavaScript 자식 프로세스를 함께 종료합니다.

네이티브 구성 요소는 CPython 3.12.11, QuickJS 2025-04-26과 원본 Python 번들의 보조 라이브러리입니다. 원문 고지는 `app/src/main/assets/licenses/external`에 보관하며, APK 빌드 시 실제 Java 의존성 고지와 함께 포함합니다. FFmpeg는 이 AAR의 FFmpeg 패키지를 사용하지 않고 별도의 `native/ffmpeg` 소스에서 빌드합니다.
