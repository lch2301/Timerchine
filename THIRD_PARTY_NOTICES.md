# 제3자 구성요소

타이머신 소스의 GPL-3.0-only 선택은 다음 구성요소의 기존 라이선스를 변경하지 않습니다.

| 구성요소 | 사용 버전 | 라이선스 및 자료 |
| --- | --- | --- |
| NAudio | 2.2.1 | MIT, NuGet 패키지 및 원 프로젝트 고지 |
| .NET / Windows Desktop Runtime | 현재 기준 배포본 8.0.32 | MIT 및 포함된 제3자 고지; 새 게시 시 실제 런타임 확인 |
| NanumSquareRound | 번들 Regular/Bold | SIL OFL 1.1, `Assets/Fonts/OFL.txt` |
| Nunito | 고정 소스에서 생성한 Bold 700 | SIL OFL 1.1, `Assets/Fonts/Nunito-OFL.txt`, `Nunito-NOTICE.txt` |
| FFmpeg / ffprobe | n9.0.2-35-g9a9b70bb8c, BtbN LGPL static | LGPLv3-or-later 및 하위 구성요소 조건, `Assets/Media/NOTICE.txt` |
| yt-dlp Windows standalone | 2026.10.21 | 해당 묶음 실행 파일 GPLv3-or-later, `Assets/Media/YT-DLP-THIRD-PARTY.txt` |
| Deno | 2.9.8 | MIT 및 제3자 조건, `Assets/Media/DENO-LICENSE.txt` |

정확한 바이너리 해시는 `Assets/Media/integrity.json`에 있습니다. 소스에서 빌드할 때 도구 EXE는 별도로 복원하지만, 최종 단일 EXE에는 이 도구가 내장될 수 있습니다.

외부 링크를 제외하는 빌드는 yt-dlp와 Deno를 제외합니다. FFmpeg와 ffprobe의 라이선스 조건은 계속 적용됩니다.

## 현재 검토 상태

기존 검토 기록은 대응 소스의 실제 바이너리 포함 범위, 일부 제3자 고지의 연결, 단일 EXE에 내장한 도구의 결합 범위에 대해 미완료 항목을 기록하고 있습니다. 도구를 많이 수집했거나 타이머신 자체를 GPL로 공개했다는 사실만으로 이 항목이 완료되지는 않습니다.

`docs/third-party-review-status.json`과 [공개 절차](docs/PUBLISHING.ko.md)를 참조하세요. 최종 배포에는 실제 포함 구성에 맞는 고지와 완전한 대응 소스·필요한 빌드 자료의 제공 방법을 확정해야 합니다.

공식 자료:

- [yt-dlp licensing](https://github.com/yt-dlp/yt-dlp#licensing)
- [FFmpeg legal information](https://ffmpeg.org/legal.html)
- [GNU GPL version 3](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU LGPL version 3](https://www.gnu.org/licenses/lgpl-3.0.html)
