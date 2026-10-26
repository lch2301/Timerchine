# Android 릴리스와 재배포

Android 1.2.0의 릴리스 태그는 **`android-v1.2.0`**, 제목은 **`타이머신 Android 1.2.0`**입니다. 기존 Windows의 `v.1.2.0` 태그는 그대로 사용합니다. Android 소스는 저장소의 `mobile/android`에 있습니다.

## 제공하는 파일

| 파일 | 내용 |
| --- | --- |
| `Timerchine-1.2.0-android.apk` | 설치용 서명 APK |
| `Timerchine-1.2.0-android-source.zip` | 해당 APK의 앱 소스·글꼴 원본·네이티브 도구 원본·빌드 스크립트·라이선스 |
| `SHA256SUMS.txt` | APK와 소스 ZIP의 SHA-256 검증값 |

APK는 [빌드 안내](BUILD.ko.md)의 서명된 릴리스 빌드로 생성합니다. 디버그 APK와 unsigned APK는 위 설치 파일을 대신하지 않습니다. APK를 다시 빌드해 교체하면 파일 해시도 다시 계산합니다.

소스 ZIP은 저장소의 `mobile/android` 구조와 루트 `LICENSE`, `Assets/Fonts/Source/Nunito-variable.ttf`를 포함합니다. Android 소스의 상대 경로 링크가 이 구조를 기준으로 합니다. `native/ffmpeg`와 `native/deps/sources`의 원본 압축 파일, `native/python/upstream-wrapper`, 네이티브 빌드 스크립트와 고지, `app/src/main/jniLibs`의 실행 파일과 해시 목록도 함께 제공합니다. 빌드 캐시, 기기별 SDK 경로, 서명 키와 비밀번호는 소스 자료에 넣지 않습니다. 수정판은 자체 키로 서명할 수 있습니다.

## GitHub에 새 릴리스 등록

1. 소스 변경 사항을 커밋·푸시한 후 [새 릴리스 작성 화면](https://github.com/lch2301/Timerchine/releases/new)을 엽니다.
2. **Choose a tag**에서 `android-v1.2.0`을 입력하고 새 태그 생성을 선택합니다. **Target**은 해당 소스가 반영된 `main`을 선택합니다.
3. **Release title**에 `타이머신 Android 1.2.0`을 입력합니다.
4. **Write**에는 [수령자용 릴리스 본문](RELEASE-android-v1.2.0.ko.md)의 내용을 붙여 넣습니다.
5. Assets에 위 표의 **파일 세 개**를 첨부하고 각각 업로드가 완료됐는지 확인합니다.
6. Android 릴리스의 **Set as latest release를 해제**합니다. 선택하지 않아도 GitHub가 버전 규칙으로 최신 릴리스를 판단할 수 있으므로, 공개 후 다음 항목도 확인합니다. 정식 버전은 **This is a pre-release**를 선택하지 않습니다.
7. **Publish release**를 누르면 공개됩니다. 작성 내용을 먼저 보관하려면 **Save draft**를 사용할 수 있습니다.

Android 다운로드 주소는 [Android 1.2.0 릴리스](https://github.com/lch2301/Timerchine/releases/tag/android-v1.2.0)입니다. 같은 저장소의 `/releases/latest`는 플랫폼마다 따로 구분되지 않으므로 Android 안내에는 태그가 포함된 주소를 사용합니다. 기존 Windows용 최신 링크를 유지하려면 게시 후 [최신 릴리스 주소](https://github.com/lch2301/Timerchine/releases/latest)가 Windows로 연결되는지 확인하세요. Android로 연결된다면 기존 Windows 릴리스를 편집하여 **Set as latest release**를 선택하고 저장합니다.

소스 또는 APK를 수정해 다시 배포할 때는 적용된 GPL과 글꼴 라이선스를 유지하고, 해당 수정판의 소스·빌드 정보를 수령자가 받을 수 있게 제공하세요. 원 제작자의 서명 키는 필요하지 않습니다.

GitHub의 화면별 기능은 [공식 릴리스 관리 문서](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)에 설명되어 있습니다.
