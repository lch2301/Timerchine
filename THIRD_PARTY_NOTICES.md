# 제3자 구성요소 고지

타이머신은 다음 오픈소스 구성요소를 사용합니다. 타이머신 자체의 GPL-3.0-only 라이선스와 별도로, 각 구성요소에는 원래의 저작권 고지와 라이선스가 적용됩니다.

| 구성요소 | 1.2.0 기준 버전 | 라이선스 및 고지 위치 |
| --- | --- | --- |
| NAudio | 2.2.1 | MIT, `Assets/Media/NAUDIO-LICENSE.txt` |
| .NET / Windows Desktop Runtime | 8.0.32 | MIT 및 제3자 고지, 실행 ZIP의 `Licenses/DotNet` |
| NanumSquareRound | Regular/Bold | SIL OFL 1.1, `Assets/Fonts/OFL.txt` |
| Nunito | 고정 소스에서 생성한 Bold 700 | SIL OFL 1.1, `Assets/Fonts/Nunito-OFL.txt`, `Nunito-NOTICE.txt` |
| FFmpeg / ffprobe | n9.0.2-35-g9a9b70bb8c, BtbN LGPL static | LGPLv3-or-later 및 하위 구성요소 조건, `Assets/Media/NOTICE.txt` |
| yt-dlp Windows standalone | 2026.10.21 | 해당 묶음 실행 파일 GPLv3-or-later, `Assets/Media/YT-DLP-THIRD-PARTY.txt` |
| Deno | 2.9.8 | MIT 및 제3자 조건, `Assets/Media/DENO-LICENSE.txt` |

실행 파일에 포함되는 도구의 SHA256은 `Assets/Media/integrity.json`에 있습니다. 배포 ZIP의 `Licenses` 폴더에는 라이선스 원문과 고지가 포함됩니다. 외부 링크 기능을 제외한 빌드는 yt-dlp와 Deno를 포함하지 않습니다.

## 소스와 수정·재배포

앱 소스와 제3자 원본 소스·빌드 자료의 위치는 [소스 자료 안내](docs/SOURCES.ko.md)를 확인하세요. GitHub의 자동 생성 Source code ZIP에는 저장소에 있는 앱 소스와 리소스가 들어 있으며, 제3자 소스 자료는 별도 파일로 구분됩니다.

프로그램이나 구성요소를 수정·재배포할 때는 해당 라이선스의 소스 제공, 저작권·라이선스 고지 유지 및 변경 표시 조건을 따라야 합니다. 타이머신의 라이선스는 외부 도구나 글꼴의 원래 라이선스를 대체하지 않습니다.

도구의 수정본을 앱에 넣어 다시 빌드하는 방법은 [외부 도구 수정 안내](docs/MODIFYING-TOOLS.ko.md)에 있습니다. 새 도구의 해시와 공개 소스로 빌드할 수 있으며 원 배포자의 개인 서명 키는 필요하지 않습니다. 라이선스가 허용하는 수정과 해당 수정의 디버깅을 제한하는 추가 조건을 두지 않습니다.

MPL-2.0이 적용되는 Certifi 및 해당 Rust 소스는 제3자 소스 ZIP의 `sources/`에 원본으로 제공하며 MPL 조건을 유지합니다. Python에 포함된 Microsoft Distributable Code에는 원래의 Microsoft 고지가 적용되며 `Assets/Media/YT-DLP-THIRD-PARTY.txt`와 실행 ZIP의 같은 고지에 보존되어 있습니다.

라이선스 전문과 공식 안내:

- [GNU GPL version 3](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU LGPL version 3](https://www.gnu.org/licenses/lgpl-3.0.html)
- [FFmpeg license information](https://ffmpeg.org/legal.html)
- [yt-dlp licensing](https://github.com/yt-dlp/yt-dlp#licensing)

## Android 구성요소

이 문서의 Windows 미디어 도구·.NET 구성요소는 Android APK에 포함되지 않습니다. Android 버전의 포함 글꼴, 빌드 도구와 라이선스는 [Android 제3자 고지](mobile/android/THIRD_PARTY_NOTICES.md)를 확인하세요.
