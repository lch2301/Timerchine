# Timerchine 1.2.0 GitHub 공개 절차

작성 기준: 2026-10-25. 확정한 1.2.0 백업은 그대로 보존합니다.

## 1. 공개 조건

타이머신 자체 소스의 라이선스는 소유자가 선택한 **GPL-3.0**을 `GPL-3.0-only`로 명시했습니다. GPL 제3판에 따른 상업적 이용도 허용되며, 배포할 때 소스 제공과 같은 라이선스 유지 등의 조건이 적용됩니다. 외부 도구와 글꼴은 원래 라이선스를 유지합니다.

저장소 이름과 소스 권리자의 공개 표기를 정합니다. Git 커밋 작성자 이메일은 GitHub의 비공개 이메일 설정을 사용할 수 있습니다. 비밀번호나 인증 토큰을 소스·문서에 넣지 않습니다.

## 2. 올릴 파일

저장소에는 앱 소스, 프로젝트, 글꼴/아이콘과 라이선스, 빌드·검증 소스 및 공개 문서를 올립니다.

다음 항목은 제외합니다.

- `bin`, `obj`, `.dotnet`, `dist`, `backups`, 개인 설정과 다운로드 캐시
- 실행 파일, 디버깅 파일, 배포/백업 ZIP, 코드 서명 개인 키
- 공개용 소스 구성과 관계없는 과거 진단 결과와 대용량 외부 소스 수집 캐시

FFmpeg EXE는 각각 약 128MiB이고, 현재 앱 EXE는 약 232MiB입니다. 일반 Git 파일은 100MiB를 초과할 수 없으므로 이 파일들을 그대로 커밋하지 않습니다. `Build/Fetch-Media.ps1`로 공식 고정 버전과 해시를 복원하고, 사용자용 EXE ZIP은 Releases 첨부 파일로 제공합니다.

GitHub가 자동 생성하는 저장소 Source code ZIP에는 Git에 추적하지 않은 외부 도구 소스가 포함되지 않습니다. 해당 ZIP 하나가 GPL/LGPL 도구의 대응 소스 제공을 모두 해결한다고 안내하지 않습니다.

## 3. GitHub 저장소 만들기

1. [새 저장소 만들기](https://github.com/new)를 열고 소유자(Owner)를 `lch2301`, 저장소 이름(Repository name)을 `Timerchine`으로 지정합니다.
2. 소스를 바로 공개하려면 Public을 선택합니다. 처음 검토용으로 Private을 선택하고 나중에 Public으로 바꿀 수도 있습니다. 소스 공개와 실행 파일 릴리스 공개는 따로 진행할 수 있습니다.
3. 로컬 문서가 이미 준비되어 있으므로 GitHub에서 README·라이선스·gitignore를 중복 생성하지 않습니다.
4. `Create repository`를 누릅니다. 생성 예정 주소는 `https://github.com/lch2301/Timerchine`입니다.
5. 웹으로 올리려면 초기 화면의 `uploading an existing file`을 누르고, 준비된 소스 ZIP을 로컬에서 푼 뒤 폴더 안의 내용물을 올립니다. ZIP 자체를 올리지 않습니다. 프로젝트/README/LICENSE 파일이 저장소 최상위에 오도록 합니다. 파일 선택이 끝나면 `Commit changes`를 누릅니다.
6. 이후에는 GitHub Desktop으로 저장소를 복제하거나 아래 Git 절차를 사용하면 변경 이력을 관리하기 쉽습니다.

Git 사용 시 계정/저장소 주소를 실제 값으로 바꾸어 실행합니다. 다음은 로컬 준비 후 실행할 예시이며 자동 업로드한 상태가 아닙니다.

```powershell
git init -b main
git add .
git status --short
git commit -m "Initial open-source release: Timerchine 1.2.0"
git remote add origin https://github.com/lch2301/Timerchine.git
git push -u origin main
```

인증은 GitHub Desktop 또는 Git Credential Manager의 브라우저 로그인 흐름으로 진행합니다. 공개 설정 전에는 `LICENSE`, `README.md`, `THIRD_PARTY_NOTICES.md`, 빌드 복원 절차와 원치 않는 파일 포함 여부를 확인합니다.

## 4. 깨끗한 소스로 빌드 확인

소스를 새 폴더에 복사한 뒤 README의 미디어 복원과 빌드 절차를 실행합니다. 준비 과정에서는 기존 검증된 도구를 `-SourceDirectory`로 복원하여 소스 구성의 빌드를 확인합니다. 새 PC에서 공식 서버 다운로드 경로도 확인합니다.

현재 빌드는 실행 시 관리자 권한을 요청하고, 새 빌드 ID에서 타이머 설정이 초기화됩니다. 이 동작을 공개 사용 안내에 명시했습니다. 변경하려면 기준 1.2.0 사본을 보존한 채 별도 수정 버전에서 다룹니다.

## 5. 실행 파일 공개 전 남은 항목

기존 제3자 검토는 다음 항목을 미완료로 기록했습니다. 현재 상태는 `docs/third-party-review-status.json`에 보관합니다.

1. FFmpeg/yt-dlp 등에 실제 포함된 구성과 대응 소스·생성 입력·빌드 자료의 연결 확정
2. 일부 후보 구성요소의 원본 고지와 실제 배포 범위 연결 검토
3. 별도 프로세스인 도구를 하나의 EXE에 내장하고 고정 해시로 추출하는 현재 포장 구조의 라이선스 적용 범위 확인

완료 방법은 실제 배포 구성에 맞는 고지와 대응 소스 묶음을 확정하거나, 의존성을 더 쉽게 교체할 수 있는 배포 구조로 정리한 뒤 다시 검토하는 것입니다. 단순히 `review` 값을 완료로 바꾸거나 upstream 링크만 나열하는 것으로 처리하지 않습니다. 외부 링크를 비활성화해도 FFmpeg의 조건은 남습니다.

오픈소스 라이선스 준수와 외부 콘텐츠 다운로드의 권리·사이트 약관 문제는 별도로 확인합니다. GPL 선택이 다운로드 기능의 모든 이용을 허용해 주는 것은 아닙니다.

GitHub 업로드에 코드 서명이 필수는 아닙니다. 다만 현재 EXE는 미서명이며 다른 PC의 Windows 보안 정책에 따라 실행이 차단될 수 있습니다. 코드 서명과 타임스탬프, 다른 PC에서의 실행은 공개 배포 품질 확인 항목입니다.

## 6. v1.2.0 릴리스

1. 소스의 확정 커밋에 `v1.2.0` 태그를 붙입니다.
2. GitHub Releases에서 `Draft a new release`를 열고 태그를 선택합니다.
3. `docs/RELEASE-v1.2.0.md`의 초안을 검토하여 사용합니다.
4. 배포 ZIP을 다시 묶을 때 앱 자체 GPL `LICENSE`, 제3자 고지, 사용 안내와 실제 바이너리에 맞는 소스 취득 안내를 포함합니다.
5. 검토된 완전한 대응 소스와 필요한 빌드 자료를 같은 릴리스에서 쉽게 받을 수 있게 제공합니다. 파일당 2GiB 미만으로 묶고 필요하면 구성요소별 아카이브로 나눕니다.
6. 최종 파일을 기준으로 SHA256을 계산해 첨부합니다. 서명이나 재포장 후에는 기존 해시를 재사용하지 않습니다.
7. 실행 파일·소스·해시·고지 링크와 깨끗한 Windows 환경의 실행 결과를 확인한 뒤 Publish합니다.

현재의 로컬 1.2.0 백업 ZIP은 보존·복원용입니다. 그대로 전체를 Git 저장소에 올리거나 검토 완료된 외부 대응 소스 묶음이라고 안내하지 않습니다.

## 공식 자료

- [저장소 라이선스](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)
- [GitHub 일반 파일 크기 제한](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)
- [GitHub Releases와 첨부 파일](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
- [GNU GPLv3](https://www.gnu.org/licenses/gpl-3.0.html)
- [FFmpeg 법적 정보](https://ffmpeg.org/legal.html)
- [yt-dlp licensing](https://github.com/yt-dlp/yt-dlp#licensing)
