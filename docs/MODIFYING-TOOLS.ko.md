# 외부 도구 수정과 다시 빌드하기

타이머신 1.2.0은 FFmpeg·ffprobe·yt-dlp·Deno를 리소스로 보관하고 별도 명령줄 프로세스로 실행합니다. 배포본은 도구를 꺼낼 때 SHA256을 확인합니다. 이 검사는 수정한 도구를 사용할 권리를 제한하는 라이선스 조건이 아닙니다.

공개된 앱 소스와 도구 소스를 수정하고 새 해시로 앱을 빌드할 수 있습니다. 원 배포자의 개인키·서명 인증서·승인 서버는 필요하지 않습니다. 외부 도구와 타이머신의 적용 라이선스는 각각 유지됩니다.

## 수정한 도구를 넣어 앱 빌드하기

Windows와 .NET 8 SDK에서 타이머신 소스를 엽니다. 별도 폴더에 사용할 `ffmpeg.exe`, `ffprobe.exe`, `yt-dlp.exe`, `deno.exe`를 준비한 뒤 다음과 같이 실행하세요. 예시의 도구 폴더는 실제 경로로 바꿉니다.

```powershell
$mediaDirectory = 'D:\Timerchine-tools'
$manifestPath = Join-Path $mediaDirectory 'integrity.json'
.\Build\Write-MediaIntegrity.ps1 -MediaDirectory $mediaDirectory -OutputPath $manifestPath
dotnet publish IntervalTimer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true "-p:MediaFilesDirectory=$mediaDirectory" "-p:MediaIntegrityManifest=$manifestPath" -o dist/Timerchine-modified
```

외부 링크 기능을 제외하려면 `ffmpeg.exe`와 `ffprobe.exe`만 준비하고 해시 생성에 `-LocalOnly`, 빌드에 `-p:EnableExternalLinks=false`를 추가합니다.

수정한 도구도 앱에서 사용하는 명령줄 옵션과 입출력을 지원해야 합니다. FFmpeg·ffprobe는 PCM 오디오와 JSON 메타데이터, yt-dlp는 메타데이터·다운로드, Deno는 JavaScript 실행에 사용됩니다. 배포한 수정본에는 변경 내용과 버전을 표시하세요.

## 도구 소스와 빌드 입력

`Timerchine-1.2.0-third-party-source-materials.zip`의 `source-catalog.json`에는 원본 소스 주소·버전·해시가 있습니다. `BUILD-SOURCES.ko.txt`와 `source-coverage.json`에는 각 도구에 연결되는 소스·네이티브 의존성·빌드 레시피가 있습니다.

- FFmpeg·ffprobe: FFmpeg `9a9b70bb8c`, BtbN 레시피 `937868360a21d711ec94a33f5591d345ad9b7754`, `win64/lgpl/9.0` 대상입니다. 소스 자료를 압축 해제한 뒤 Bash와 Docker/buildx 환경에서 `bash Build/Rebuild-FFmpeg.sh <소스자료폴더> <새출력폴더>`를 실행할 수 있습니다. 이 스크립트는 보존된 소스 아카이브를 사용합니다. 원본 `build.sh`의 `git clone --branch`에 커밋 해시를 넘기는 방식을 사용하지 않습니다.
- yt-dlp: 원본 `bundle/`, Windows workflow, 고정 requirements와 PyInstaller 배포자의 소스를 제공합니다. Python 3.10.11의 `PCbuild/get_externals.bat`가 선택하는 네이티브 라이브러리 소스도 포함됩니다.
- Deno: 본체 소스와 Rust/V8 자료를 제공합니다. MPL 적용 부분은 MPL 조건을 유지하며 소스 자료에서 해당 원본을 받을 수 있습니다.

빌드 도구 설치와 원본 빌드 스크립트의 다운로드에는 네트워크 연결이 필요할 수 있습니다. SDK·컴파일러·패키지 버전과 Git 메타데이터에 따라 결과 파일의 해시나 버전 문자열이 달라질 수 있으며, 원 배포 파일과 바이트 단위로 같음을 보장하지 않습니다.
