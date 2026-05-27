# BalloonFlow Cloud Functions

FCM cron senders — 이탈 복귀(D1~D7) + 데일리 보상 미수령 푸시.

## 구조

| Function | Schedule (UTC) | 트리거 | 가드 | 명세 |
|---|---|---|---|---|
| `pushReturnCron` | 매일 09:00 | `lastLoginAt`이 1~7일 전 | `settings.notificationOn=true` + `lastReturnPushSent≠오늘` + 설치 후 24h 경과 + 유효 `fcmToken` | 아웃게임 §9 #2-1~#2-7 / L676 |
| `pushDailyRewardCron` | 매일 20:00 | `dailyReward.lastClaimDate ≠ 오늘` | + 당일 수령자 exclude (L668) + 설치 후 24h 경과 (L675) | 아웃게임 §9 #3 |

## 1회 셋업

```bash
# 1. Firebase CLI 설치 (글로벌, Node 20+)
npm install -g firebase-tools

# 2. firebase/ 에서 프로젝트 alias 등록
cd firebase
firebase login
firebase use --add        # 프로젝트 선택 → .firebaserc 생성

# 3. functions 의존성 설치
cd functions
npm install
```

## 로컬 테스트

```bash
# Functions emulator + Firestore emulator
cd firebase
firebase emulators:start --only functions,firestore

# 또는 functions shell 에서 직접 호출
cd firebase/functions
npm run shell
> pushReturnCron()
```

## 배포

```bash
cd firebase/functions
npm run deploy        # = firebase deploy --only functions
```

배포 후 GCP Console > Cloud Scheduler 에서 `pushReturnCron` / `pushDailyRewardCron` job 두 개 확인. 수동 실행도 가능 ("RUN NOW").

## 로그 확인

```bash
cd firebase/functions
npm run logs
# 또는
firebase functions:log --only pushReturnCron --lines 50
```

## 텍스트 변경

`index.js` 의 `RETURN_PUSH_BODY` / `DAILY_REWARD_BODY` 는 Unity 클라(`Assets/1.Scripts/Data/PushTexts.cs`)와 동기 유지. **양쪽 같이 갱신**.

## 알려진 한계 / TODO

- **TZ 보정 미적용** — `dailyReward.lastClaimDate`는 디바이스 local TZ 기반. 서버 UTC 기준 비교라 시차 8h 한국 유저는 약간 어긋날 수 있음. 1.1+ 에서 서버 timestamp 동시 저장 후 보강 예정.
- **풀스캔 쿼리** — `users` 컬렉션 전체 fetch. 수천~수만 규모까지 OK. 그 이상은 Firestore export → BigQuery 또는 페이지네이션 도입.
- **포어그라운드 수신 표시 안 함** — 게임 중 산만함 방지. 백그라운드 알림은 OS 자동 표시.
- **D1~D7 한 번씩만** — D7 이후 미접속 유저는 더 이상 발송 안 함 (스팸 방지, 아웃게임 §9 L676).
- **글로벌 단일 시각** — 사용자별 local time 발송은 1.1+ (user TZ 캐싱 필요).

## 보안

- `service-account.json` 은 절대 커밋 금지 — Cloud Functions 런타임은 Application Default Credentials 사용 (배포 시 자동 권한)
- emulator 로컬 테스트만 service account 필요 — `.gitignore` 등록됨

## 비용 (참고)

- onSchedule 함수 호출: 일 2회 × 30일 = 60 invocations / 월. Free tier 충분.
- FCM send: 무료 (FCM 자체 무료 서비스).
- Firestore read: users collection 전체 풀스캔 × 2회/일. 5000 유저 기준 ~300k reads/월. Free tier 50k/일 = ~1.5M/월 무료. OK.
