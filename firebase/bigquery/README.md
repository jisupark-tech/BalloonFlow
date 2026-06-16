# BalloonFlow — BigQuery 직접 적재 (Direct Streaming Ingest)

기존 **Firebase Analytics → BigQuery 자동 export** 를 폐기하고, 커스텀 이벤트를 Cloud Function 경유로
이미 생성해 둔 `balloonloop_db` 의 **타입드 이벤트 테이블**에 직접 streaming insert 한다.

```
Unity AnalyticsManager (배치 버퍼)
   └─ POST /ingestAnalyticsEvents  (Authorization: Bearer <Firebase ID 토큰>)
        └─ Cloud Function: 토큰 검증 → 이벤트명별 테이블 라우팅 → BigQuery streaming insert
             └─ balloonloop_db.<table>
```

## 왜 클라에서 BQ 직접 호출이 아닌가
모바일 클라가 BQ API 를 직접 부르려면 서비스 계정 키를 앱에 심어야 하고, 디컴파일 추출 시 데이터
read/write/delete + 비용 폭탄에 노출. 반드시 서버(함수) 경유 — 함수가 Firebase Auth ID 토큰으로
호출자를 검증하고 토큰 uid 를 권위값으로 사용(클라가 보낸 uid 무시).

## 이벤트 → 테이블 라우팅 (`functions/index.js` EVENT_TABLE)
| 클라 이벤트명 | balloonloop_db 테이블 |
|---|---|
| session_start_event    | session_start |
| session_end_event      | session_end |
| level_play_start_event | play_start |
| level_play_event       | play_event |
| item_use_event         | item_use |
| purchase_event         | purchase |
| economy_event          | economy |
| ad_event               | ad_event |

`item_master`(정적 카탈로그), `user_property`(유저 상태)는 이벤트 스트림이 아님 — 별도 경로로 적재(이 함수 범위 밖).

## 공통 필드 매핑 (함수에서 처리)
- `event_ts` → **`event_timestamp`**. 클라 ISO('o', Z·7프랙) → `toBqDatetime`(존 제거·ms)로 DATETIME/TIMESTAMP 양립.
- **session_start 테이블만** 컬럼명이 다름 → `app_version→version`, `geo_country→country` (FIELD_RENAME).
- `uid` = 토큰 검증값으로 덮어씀.
- `ignoreUnknownValues: true` — 타겟 테이블에 없는 키는 무시(클라/스키마 드리프트 내성).

## 클라↔BQ 컬럼 정렬 상태 (2026-06-16 "클라를 BQ 에 맞춤")
- **session_start / session_end** ✅ 완전 적재.
- **play_start** 🟡 적재 OK. `install_version`/`pre_play_item_ids`/`pre_play_item_count` 는 클라 미emit → NULL(추후 보강).
- **play_event** ✅ `extra_json` 통합 폐기 → `play_time_sec`/`background_time_sec`/`score`/`star_count` 개별 emit.
  나머지 상세 컬럼(moves_*, undo_count, deadlock_count, objective_*, coin_*, continue_*, shuffle/hint, fail_* …)은
  클라가 아직 미계측 → NULL(게임 로직 계측 후 emit 추가).
- **economy** ✅ `flow_type+amount` → **`change_amount`(earn=+, spend=-)**. `source` 컬럼에 earn 출처/spend 대상 통합.
- **item_use** 🟡 `item_id`/`item_category(=booster)` 적재. `acquisition_type`/`cost_amount`/`cost_currency_id` 는
  부스터 인벤토리 사용이라 사용시점 직접 비용 없음 → NULL(제품 정의 시 보강).
- **purchase** 🟡 정렬: `currency→currency_code`, `transaction_id→receipt_id`. `store`/`product_category`/`device_model` 은
  purchase 테이블에 컬럼 없음 → 미emit. `product_name`/`product_type`/`price_local`/`iap_placement`/`coin_granted`/
  `items_granted`/`lives_granted`/`is_verified` 는 클라 미계측 → NULL(추후 보강).
- **ad_event** ⏸ emitter 미구현(테이블만 준비됨).

## 1회 셋업 (배포 전)
1. **함수 런타임 SA 에 BigQuery 쓰기 권한** — gen2 기본 SA(`<project-number>-compute@developer.gserviceaccount.com`)에
   `balloonloop_db` 데이터셋 `roles/bigquery.dataEditor` 부여(BQ 콘솔 → 데이터셋 공유).
2. **Anonymous 인증 provider 활성화** — Firebase 콘솔 → Authentication → Sign-in method → Anonymous → Enable.
   (외부 인증 도입 후엔 CurrentUser 가 실제 유저로 채워져 동일 경로 동작.)
3. **함수 의존성 설치 & 배포**
   ```bash
   cd firebase/functions && npm install        # @google-cloud/bigquery 포함
   firebase deploy --only functions:ingestAnalyticsEvents
   ```
   - 배포 URL 확인 후 다르면 Unity `AnalyticsManager.INGEST_URL` 교체.
   - 기본 별칭: `https://us-central1-balloonloop-d855d.cloudfunctions.net/ingestAnalyticsEvents`
4. **기존 export 정리(선택)** — Firebase 콘솔 → 프로젝트 설정 → 통합 → BigQuery 링크 해제(중복 적재 방지).

## 클라 동작 요약 (`AnalyticsManager`)
- `LogEvent` → `_bqBatch` 버퍼링 + Facebook/AppsFlyer 즉시 전송.
- flush: 20건 누적 / 15초 주기 / 백그라운드 / 종료. 토큰 확보(익명 lazy 폴백) → 최대 100건씩 POST.
- 실패: 5xx·네트워크 = 재시도, 4xx = 폐기, 버퍼 512 초과 = oldest drop.
- 한계(1.0): 백그라운드 직전 flush 는 best-effort — 무손실은 디스크 영속화 필요(1.1+).
