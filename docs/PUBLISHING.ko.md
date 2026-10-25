# 소스 빌드·수정·재배포 안내

타이머신 소스를 직접 빌드하거나 수정하여 사용할 수 있습니다. 타이머신 소스에는 GNU GPL version 3 only가 적용되며, 외부 도구와 글꼴에는 각자의 라이선스가 적용됩니다.

## 빌드 환경

Windows와 .NET 8 Windows 타깃을 빌드할 수 있는 SDK가 필요합니다.

```powershell
git clone https://github.com/lch2301/Timerchine.git
cd Timerchine
.\Build\Fetch-Media.ps1
dotnet restore IntervalTimer.csproj -r win-x64
dotnet publish IntervalTimer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/Timerchine-1.2.0-win-x64
```

미디어 복원 스크립트는 공식 배포처의 고정 버전 파일을 받아 `Assets/Media/integrity.json`의 SHA256으로 검증합니다. 같은 원본 파일을 이미 보관하고 있다면 `-SourceDirectory`로 복원할 수 있습니다.

공식 배포처에서 고정 파일을 더 이상 제공하지 않으면 복원이 실패할 수 있습니다. 같은 버전·해시의 원본 파일을 준비하거나, 도구 버전과 무결성 목록을 함께 갱신하여 빌드할 수 있습니다.

## 외부 링크를 제외한 빌드

```powershell
.\Build\Fetch-Media.ps1 -IncludeOnlineTools:$false
dotnet publish IntervalTimer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:EnableExternalLinks=false -o dist/Timerchine-1.2.0-local-win-x64
```

이 빌드에서는 yt-dlp와 Deno가 제외됩니다. FFmpeg와 ffprobe는 로컬 오디오 재생에 계속 사용됩니다.

## 수정한 외부 도구 사용

앱 소스의 `MediaFilesDirectory`와 `MediaIntegrityManifest` 빌드 옵션으로 수정한 FFmpeg·ffprobe·yt-dlp·Deno를 넣을 수 있습니다. `Build/Write-MediaIntegrity.ps1`로 새 해시 목록을 만들며 원 배포자의 서명 키는 필요하지 않습니다. 도구별 소스와 빌드 절차는 [외부 도구 수정 안내](MODIFYING-TOOLS.ko.md)를 참조하세요.

## 검증

```powershell
dotnet run --project Verification/DurationInputChecks/DurationInputChecks.csproj -c Release -- Verification/results/duration-input
dotnet run --project Verification/PresetChecks/PresetChecks.csproj -c Release -- Verification/results/presets
```

검증은 Windows에서 실행하며, 출력 경로는 실행마다 새로운 폴더를 사용합니다. 실제 오디오 장치와 외부 링크, 사용하는 게임에서의 단축키 동작은 사용 환경에서 확인할 수 있습니다.

## 수정본의 배포

프로그램과 수정본을 배포할 때는 적용되는 GPL의 소스 제공, 라이선스·저작권 고지 유지, 변경 표시 및 같은 라이선스 조건을 따라야 합니다. 실행 파일에 포함된 외부 구성요소의 소스·빌드 자료와 고지도 해당 조건에 맞게 함께 제공해야 합니다.

원본과 수정본을 구분할 수 있도록 버전과 변경 내역을 표시하세요. GPL 및 제3자 라이선스의 전문은 [LICENSE](../LICENSE)와 [제3자 구성요소 고지](../THIRD_PARTY_NOTICES.md)에서 확인할 수 있습니다.

빌드한 파일은 SDK, 패키지, 소스 경로 및 빌드 옵션에 따라 원본 배포 파일과 바이트 단위로 달라질 수 있습니다. 파일 무결성은 해당 빌드에서 생성한 SHA256으로 확인할 수 있습니다.
