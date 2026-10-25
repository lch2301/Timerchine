# 타이머신 · Timerchine

**알람이 울리는 순간도, 음악이 멈추는 순간도 시간의 신호로.**

타이머신은 시간이 끝나면 소리가 울리는 기본 알람을 제공하는 Windows용 멀티 타이머입니다.

정해진 시간 동안 무언가를 하거나, 때가 될 때까지 기다려야 한다면 **역알람 모드**를 선택하세요. 타이머가 작동하는 동안 좋아하는 음악을 들으며 다른 일을 하고, 음악이 끊기는 순간 시간이 됐음을 떠올릴 수 있습니다.

역알람은 음악이 멈추는 변화를 알아차릴 수 있는 상황에 어울립니다. 수면 중처럼 그 변화를 놓치기 쉬운 상황에서는 소리가 울리는 기본 알람을 사용하세요.

## 주요 기능

- 여러 타이머와 개별·전체 시작, 중단, 재시작
- 기본 알람과 선택형 역알람, 타이머별 음량 및 단축키
- 로컬 파일 및 재생 목록, 무작위 재생, 이름을 지정하는 재생 목록 프리셋
- 기본 프리셋 자동 생성, 새 프리셋 생성 즉시 선택
- 선택한 프리셋은 재생 목록 창의 저장 버튼을 눌렀을 때 갱신
- 모래시계 표시, 트레이 최소화
- 대기·중단 중에는 시간 입력칸 배경 표시, 실행 중에는 숨김

## 현재 상태와 실행

기준 버전은 **1.2.0**입니다. 이 저장소는 타이머신의 소스 공개를 위한 저장소입니다. 실행 파일의 공개 배포 상태와 준비 절차는 [공개 절차](docs/PUBLISHING.ko.md)를 확인하세요.

Windows 10 22H2 이상 또는 Windows 11의 x64 환경을 대상으로 합니다. 자체 포함 배포본은 .NET 런타임을 별도로 설치하지 않고 실행할 수 있습니다. 현재 실행 매니페스트는 관리자 권한을 요청합니다.

실행 파일을 공개할 때는 Releases에 ZIP, SHA256 및 제3자 라이선스·대응 소스 자료를 함께 제공합니다. 이 저장소의 자동 생성 Source code ZIP은 내장 외부 도구의 완전한 대응 소스 묶음을 대신하지 않습니다. 배포 준비 상태는 [공개 절차](docs/PUBLISHING.ko.md)를 확인하세요.

## 사용 방법

1. 시·분·초 숫자칸을 눌러 시간을 입력합니다. 엔터 또는 다른 영역 클릭으로 저장합니다.
2. 알람 설정에서 기본 알람 또는 역알람과 재생할 소리를 선택합니다.
3. 시작을 누릅니다. 중단 후 재시작할 수 있으며, 역알람은 중단한 재생 위치를 이어 사용합니다.
4. 재생 목록을 반복 사용할 때는 프리셋을 만들거나 불러오고 재생 목록 창에서 저장합니다.

외부 링크 기능은 설정에서 이용 권한 확인이 필요합니다. 콘텐츠의 권리와 제공 사이트의 조건을 확인하고 사용할 수 있는 자료만 등록하세요. 외부 링크를 제외한 빌드도 가능합니다.

## 소스에서 빌드

Windows와 .NET 8 Windows 타깃을 빌드할 수 있는 SDK가 필요합니다.

Git 저장소에는 외부 도구 EXE를 넣지 않습니다. 다음 스크립트가 고정 버전의 도구를 공식 배포처에서 받아 `Assets/Media/integrity.json`의 SHA256으로 검증합니다. 원래 도구를 이미 보관하고 있다면 `-SourceDirectory`로 복원할 수 있습니다.

```powershell
.\Build\Fetch-Media.ps1
dotnet restore IntervalTimer.csproj -r win-x64
dotnet publish IntervalTimer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/Timerchine-1.2.0-win-x64
```

외부 링크 기능을 제외하려면 복원과 게시에 다음 옵션을 사용합니다. FFmpeg/ffprobe는 로컬 오디오에 필요하므로 계속 포함됩니다.

```powershell
.\Build\Fetch-Media.ps1 -IncludeOnlineTools:$false
dotnet publish IntervalTimer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:EnableExternalLinks=false -o dist/Timerchine-1.2.0-local-win-x64
```

외부 배포처가 고정 파일을 더 이상 제공하지 않으면 스크립트는 실패합니다. 버전이나 해시를 임의로 건너뛰지 말고 같은 원본 도구를 복원하거나 의존성을 명시적으로 갱신하세요.

## 검증

```powershell
dotnet run --project Verification/DurationInputChecks/DurationInputChecks.csproj -c Release -- Verification/results/duration-input
dotnet run --project Verification/PresetChecks/PresetChecks.csproj -c Release -- Verification/results/presets
```

위 검증은 Windows에서 실행합니다. 테스트 출력 폴더는 실행할 때마다 비어 있는 새 경로를 사용하세요. 실제 오디오 장치, 외부 링크 다운로드, 게임 단축키와 다른 PC에서의 실행은 별도 확인 항목입니다.

## 사용자 데이터

설정과 프리셋은 `%LOCALAPPDATA%\IntervalTimer`에 보관합니다. 사용자 설정, 다운로드 파일과 섬네일 캐시는 공개 소스와 배포 ZIP에 포함하지 않습니다.

현재는 실행 파일의 빌드 ID가 달라지면 타이머 설정이 초기값으로 시작하며, 같은 빌드를 재실행하면 설정을 복원합니다. 프리셋 라이브러리는 별도 파일로 유지됩니다.

## 라이선스

타이머신 자체 소스는 **GNU General Public License version 3 only (`GPL-3.0-only`)**로 제공합니다. 전문은 [LICENSE](LICENSE)에 있습니다. 외부 도구·라이브러리·글꼴은 각자의 라이선스를 유지하며, 자세한 내용은 [제3자 구성요소](THIRD_PARTY_NOTICES.md)를 확인하세요.

GPL에 따라 프로그램과 수정본을 배포할 때 적용되는 소스 제공, 라이선스·저작권 고지 및 변경 표시 조건을 지켜야 합니다. 소스와 재배포 자료는 실제 배포한 바이너리에 맞춰 제공해야 합니다.
