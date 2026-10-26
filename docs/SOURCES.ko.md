# 소스 자료와 라이선스 안내

타이머신의 소스는 [lch2301/Timerchine](https://github.com/lch2301/Timerchine)에서 확인할 수 있습니다. 버전별 다운로드 자료는 [Releases](https://github.com/lch2301/Timerchine/releases)에 구분되어 있습니다.

## 다운로드 자료의 구분

- `Timerchine-1.3.0-win-x64.zip`: Windows x64용 실행 파일, 사용 안내와 라이선스 고지
- `Timerchine-1.3.0-source.zip`: 타이머신 Windows 앱 소스, 리소스, 빌드·검증 자료
- `Timerchine-1.2.0-third-party-source-materials.zip`: 사용한 외부 도구의 고정 버전 원본 소스와 빌드 자료
- `SHA256SUMS.txt`: 다운로드 파일의 SHA256 목록

GitHub의 자동 생성 Source code ZIP에는 저장소의 파일이 들어 있습니다. 앱 소스와 제3자 소스 자료는 위 파일 이름으로 구분할 수 있습니다.

Windows 1.3.0은 1.2.0과 같은 버전의 외부 미디어 도구를 포함합니다. 해당 도구의 소스 자료는 [1.2.0 릴리스의 제3자 소스 ZIP](https://github.com/lch2301/Timerchine/releases/download/v.1.2.0/Timerchine-1.2.0-third-party-source-materials.zip)에서 받을 수 있습니다. 게임 오버레이와 오버레이 설정은 Windows 기본 API를 사용하며 별도 외부 프로그램을 추가하지 않습니다.

## 구성요소의 원본 자료

- FFmpeg: [고정 소스](https://github.com/FFmpeg/FFmpeg/tree/9a9b70bb8c), [BtbN 빌드 레시피](https://github.com/BtbN/FFmpeg-Builds/tree/9378683)
- yt-dlp: [2026.10.21 소스와 배포 자료](https://github.com/yt-dlp/yt-dlp/tree/2026.10.21)
- Deno: [v2.9.8 소스](https://github.com/denoland/deno/tree/v2.9.8)
- NAudio: [v2.2.1 소스](https://github.com/naudio/NAudio/tree/v2.2.1)
- Nunito: `Assets/Fonts/Source` 및 `Assets/Fonts/Nunito-NOTICE.txt`

사용 버전과 원본 소스의 위치, 파일 해시는 제3자 자료의 목록과 개별 고지에서도 확인할 수 있습니다. 실제 포함 구성과 고지는 실행 ZIP의 `Licenses` 폴더를 참조하세요.

## 제3자 자료의 구성

제3자 소스 ZIP의 `source-catalog.json`에는 원본 아카이브의 주소·리비전·SHA256이 있고, `source-coverage.json`에는 실제 사용 도구와 빌드 레시피·네이티브 의존성의 연결이 있습니다. `notice-coverage.json`과 `notices/`에는 원본 고지 및 패키지의 라이선스·작성자 선언이 보존되어 있습니다. 목록은 빌드·시험·다른 플랫폼용 자료도 포함하므로 모든 항목이 Windows 실행 파일에 들어 있다는 뜻은 아닙니다.

FFmpeg·ffprobe의 고정 소스·패치·BtbN 레시피와 중첩 의존성을 제공합니다. yt-dlp에는 Python 3.10.11의 Windows 네이티브 라이브러리 소스, EJS에 묶인 astring·meriyah의 배포 파일과 원본 소스도 연결되어 있습니다. 도구별 절차는 자료 안의 `BUILD-SOURCES.ko.txt`와 [외부 도구 수정 안내](MODIFYING-TOOLS.ko.md)를 확인하세요.

MPL-2.0 적용 부분은 해당 라이선스 조건으로 제공합니다. Certifi와 해당 Rust 구성요소의 변경하지 않은 원본 소스는 같은 제3자 ZIP의 `sources/`에서 받을 수 있으며, 라이선스·저작권 고지는 원본과 `notices/`에 유지됩니다. 타이머신 전체의 GPL 안내가 이 파일들의 MPL 조건을 대체하지 않습니다.

## 이용 조건

타이머신 자체 소스는 GNU GPL version 3 only로 제공합니다. 외부 구성요소에는 각자의 라이선스가 적용됩니다. 수정하거나 재배포할 때는 해당 라이선스의 소스 제공과 저작권·라이선스 고지 유지 조건을 따라야 합니다.

문서나 다운로드에 문제가 있다면 [저장소 Issues](https://github.com/lch2301/Timerchine/issues)에 해당 버전과 파일 이름을 남겨 주세요.
