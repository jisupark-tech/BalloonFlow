# BalloonFlow Episode Uploader (GUI / exe)

`Assets/EditorData/Episodes` 의 `episode_*.json` 을 Firestore `/episodes` 컬렉션에 업로드하는 **독립 실행 GUI 프로그램**.
Unity·Node.js 설치 없이, 배포받은 **exe 하나로 바로 실행**해서 쓸 수 있습니다.

`firebase/seed/upload-episodes.js` (Node Admin SDK) 와 **완전히 동일한 문서 스키마/인코딩**을 재현합니다.

## 팀원 사용법 (배포받은 사람)

1. 배포 폴더를 받습니다 — 안에 다음이 들어 있어야 합니다:
   - `BalloonFlow_EpisodeUploader.exe`
   - `service-account.json`  ← Firebase 관리자 키 (보안 채널로 별도 전달, git 에 없음)
2. `BalloonFlow_EpisodeUploader.exe` 실행.
3. **Episode 폴더** 에 `episode_*.json` 이 있는 폴더를 지정 (예: 저장소의 `Assets/EditorData/Episodes`).
4. **[업로드 시작]** 클릭 → 결과 창에서 성공/실패 확인.
   - **Dry run** 체크 시 업로드 없이 미리보기(파일·압축 크기)만.

> 마지막에 지정한 폴더/키 경로는 자동 저장돼 다음 실행 때 다시 채워집니다.

## service-account.json 받는 법 (관리자)

Firebase 콘솔 → 프로젝트 `balloonloop-d855d` → 프로젝트 설정 → **서비스 계정** →
**새 비공개 키 생성** → 받은 JSON 을 `service-account.json` 으로 저장.
이 파일은 **관리자 권한 키**이므로 git 에 올리지 말고(1Password 등) 보안 채널로만 공유하세요.

## 업로드되는 문서 스키마 (`/episodes/{packageId}`)

| 필드 | 값 |
|---|---|
| `packageId`  | number |
| `levelCount` | `levels.length` |
| `version`    | `episode.version` (없으면 1) |
| `encoding`   | `"gzip+b64"` |
| `levelsJson` | base64( gzip( raw json ) ) — 클라: b64 decode + gunzip + `JsonUtility.FromJson<LevelEpisode>` |
| `rawSize`    | raw json 문자 길이 |
| `updatedAt`  | serverTimestamp |

## 빌드 (개발자 — exe 새로 만들 때)

```
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
→ `bin/Release/net10.0-windows/win-x64/publish/BalloonFlow_EpisodeUploader.exe` (단일 실행 파일).
이 exe + `service-account.json` 을 한 폴더에 담아 배포하면 됩니다. (.NET 설치 불필요 — self-contained)
