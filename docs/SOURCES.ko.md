# 소스 자료와 라이선스 안내

타이머신의 소스는 [lch2301/Timerchine](https://github.com/lch2301/Timerchine)에서 확인할 수 있습니다. 버전별 다운로드 자료는 [Releases](https://github.com/lch2301/Timerchine/releases)에 구분되어 있습니다.

## 다운로드 자료의 구분

- `Timerchine-1.2.0-win-x64.zip`: Windows x64용 실행 파일, 사용 안내와 라이선스 고지
- `Timerchine-1.2.0-source.zip`: 타이머신 앱 소스, 리소스, 빌드·검증 자료
- `Timerchine-1.2.0-third-party-source-materials.zip`: 사용한 외부 도구의 고정 버전 원본 소스와 빌드 자료
- `SHA256SUMS.txt`: 다운로드 파일의 SHA256 목록

GitHub의 자동 생성 Source code ZIP에는 저장소의 파일이 들어 있습니다. 앱 소스와 제3자 소스 자료는 위 파일 이름으로 구분할 수 있습니다.

## 구성요소의 원본 자료

- FFmpeg: [고정 소스](https://github.com/FFmpeg/FFmpeg/tree/9a9b70bb8c), [BtbN 빌드 레시피](https://github.com/BtbN/FFmpeg-Builds/tree/9378683)
- yt-dlp: [2026.10.21 소스와 배포 자료](https://github.com/yt-dlp/yt-dlp/tree/2026.10.21)
- Deno: [v2.9.8 소스](https://github.com/denoland/deno/tree/v2.9.8)
- NAudio: [v2.2.1 소스](https://github.com/naudio/NAudio/tree/v2.2.1)
- Nunito: `Assets/Fonts/Source` 및 `Assets/Fonts/Nunito-NOTICE.txt`

사용 버전과 원본 소스의 위치, 파일 해시는 제3자 자료의 목록과 개별 고지에서도 확인할 수 있습니다. 실제 포함 구성과 고지는 실행 ZIP의 `Licenses` 폴더를 참조하세요.

## 이용 조건

타이머신 자체 소스는 GNU GPL version 3 only로 제공합니다. 외부 구성요소에는 각자의 라이선스가 적용됩니다. 수정하거나 재배포할 때는 해당 라이선스의 소스 제공과 저작권·라이선스 고지 유지 조건을 따라야 합니다.

문서나 다운로드에 문제가 있다면 [저장소 Issues](https://github.com/lch2301/Timerchine/issues)에 해당 버전과 파일 이름을 남겨 주세요.
