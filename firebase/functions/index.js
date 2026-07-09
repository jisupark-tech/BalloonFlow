/**
 * BalloonFlow Cloud Functions — FCM cron senders.
 *
 * 정책 (BalloonFlow_아웃게임디렉션.md §9, LiveOps §9):
 *   #2 이탈 복귀 D1~D7 — 매일 09:00 UTC. lastLoginAt 기준 day=1..7 분기.
 *   #3 데일리 보상 미수령 — 매일 20:00 UTC. 당일 수령자 exclude (L668).
 *
 * 모든 cron 은 idempotent — 같은 일자 중복 발송을 lastReturnPushSent /
 * lastDailyPushSent 일자 마킹으로 방지. 토큰 무효(NotRegistered) 시 자동 정리.
 *
 * 텍스트는 Unity 클라(Assets/1.Scripts/Data/PushTexts.cs)와 동기화 필요 —
 * 수정 시 양쪽 같이 갱신.
 */

'use strict';

const { onSchedule } = require('firebase-functions/v2/scheduler');
const { onRequest } = require('firebase-functions/v2/https');
const { initializeApp } = require('firebase-admin/app');
const { getFirestore, FieldValue } = require('firebase-admin/firestore');
const { getMessaging } = require('firebase-admin/messaging');
const { getAuth } = require('firebase-admin/auth');
const { BigQuery } = require('@google-cloud/bigquery');
const { GoogleAuth } = require('google-auth-library');
const crypto = require('crypto');

initializeApp();
const db = getFirestore();
const fcm = getMessaging();
const bq = new BigQuery(); // 런타임 서비스 계정 자격증명 자동 사용 (roles/bigquery.dataEditor 필요)

// ── Push 텍스트 (Unity PushTexts.cs 와 동기 유지) ─────────────────────────
const APP_TITLE = 'BalloonFlow';

const RETURN_PUSH_BODY = {
  1: 'Take a break? Come pop some balloons!',
  2: '🎈 Pop the day off! Hearts are ready, friends await.',
  3: '🎈 Stress? Pop. Pop. Pop. Three taps to your daily smile.',
  4: "🎈 Boredom won't pop itself — your balloons are waiting!",
  5: "🎈 Pop! Pop! Don't you miss that sound? Come back and feel it again.",
  6: "🎈 So many balloons left to pop! They won't pop themselves.",
  7: "🎈 Remember the joy of popping balloons? It's time for one more round!",
};

const DAILY_REWARD_BODY = "⏰ Don't miss today's reward! Tap to collect before it's gone.";

// ── Helpers ────────────────────────────────────────────────────────────────

/** "YYYY-MM-DD" (UTC 기준). 일자 마킹 / exclude 비교용. */
const todayUtc = () => new Date().toISOString().slice(0, 10);

/**
 * 설치 후 첫 24시간 가드 (아웃게임 §9 L675).
 * 신규 유저 첫인상 보호 — 하트 충전 외 푸시 미발송.
 * createdAt 미세팅(legacy 유저) 시 false 반환 — 발송 허용.
 */
function isWithinFirst24Hours(userData, nowMs) {
  const createdAt = userData.createdAt?.toDate?.();
  if (!createdAt) return false;
  return (nowMs - createdAt.getTime()) < 24 * 3600 * 1000;
}

/** Cloud Functions invalidate 시 토큰 비움. 그 외 실패는 단순 카운트. */
async function sendToToken(token, body) {
  try {
    const id = await fcm.send({
      token,
      notification: { title: APP_TITLE, body },
      android: { priority: 'high' },
      apns: { headers: { 'apns-priority': '10' } },
    });
    return { ok: true, id };
  } catch (err) {
    return { ok: false, code: err.code || err.message };
  }
}

/** 토큰 무효 응답 시 fcmToken 필드 비움. (다음 cron 부터 자동 skip) */
function isInvalidTokenError(code) {
  return code === 'messaging/registration-token-not-registered'
      || code === 'messaging/invalid-registration-token'
      || code === 'messaging/invalid-argument';
}

/**
 * 푸시 대상 후보 fetch — fcmToken 보유 + 설정 토글 ON.
 * 1.0 규모(수천~수만 유저) 가정 — 풀스캔. 규모 커지면 페이지네이션 또는
 * Firestore export → BigQuery 쿼리로 이전.
 */
async function fetchPushCandidates() {
  // 두 inequality 를 한 쿼리에 못 묶어서 토글만 서버 필터, fcmToken 은 로컬 체크.
  return db.collection('users')
    .where('settings.notificationOn', '==', true)
    .get();
}

// ── #2 이탈 복귀 D1~D7 (매일 09:00 UTC) ────────────────────────────────────
exports.pushReturnCron = onSchedule(
  {
    schedule: '0 9 * * *',
    timeZone: 'UTC',
    region: 'us-central1',
    memory: '256MiB',
    timeoutSeconds: 540,
  },
  async () => {
    const today = todayUtc();
    const nowMs = Date.now();
    const snapshot = await fetchPushCandidates();

    let sent = 0, skipped = 0, invalidToken = 0;
    const updates = [];

    for (const doc of snapshot.docs) {
      const u = doc.data();

      if (!u.fcmToken) { skipped++; continue; }
      if (u.lastReturnPushSent === today) { skipped++; continue; }
      if (isWithinFirst24Hours(u, nowMs)) { skipped++; continue; }

      const lastLogin = u.lastLoginAt?.toDate?.();
      if (!lastLogin) { skipped++; continue; }

      const elapsedHours = (nowMs - lastLogin.getTime()) / 3600000;
      const day = Math.floor(elapsedHours / 24);
      if (day < 1 || day > 7) { skipped++; continue; }

      const body = RETURN_PUSH_BODY[day];
      if (!body) { skipped++; continue; }

      const result = await sendToToken(u.fcmToken, body);
      if (!result.ok) {
        if (isInvalidTokenError(result.code)) {
          updates.push(doc.ref.update({ fcmToken: '' }));
          invalidToken++;
        }
        skipped++;
        continue;
      }

      updates.push(doc.ref.update({ lastReturnPushSent: today }));
      sent++;
    }

    await Promise.all(updates);
    console.log(`[pushReturnCron] total=${snapshot.size} sent=${sent} skipped=${skipped} invalidToken=${invalidToken}`);
  }
);

// ── #3 데일리 보상 미수령 (매일 20:00 UTC) ──────────────────────────────────
//
// Exclude: dailyReward.lastClaimDate == 오늘. UserData 의 lastClaimDate 는
// 디바이스 local TZ 기반이라 서버 UTC 기준과 약간 어긋날 수 있음 — TZ 보정은
// 1.1+ 에서 서버 timestamp 동시 저장 후 보강. 1.0 은 UTC 단일 기준 send.
// ROLLBACK_DAILY_PUSH_DISABLED_20260618: 1.0 에 데일리 리워드 기능 없음(클라 UI/부트스트랩 비활성, "[1.0 비포함]").
//   보상이 없는데 '오늘의 보상 받으세요' 푸시가 나가면 안 되므로 데일리 cron 발송을 비활성(아래 export 전체 주석).
//   ★ firebase deploy 재배포해야 기존 배포된 pushDailyRewardCron 함수가 제거됨. 도입 시 주석 해제 + 재배포.
/*
exports.pushDailyRewardCron = onSchedule(
  {
    schedule: '0 20 * * *',
    timeZone: 'UTC',
    region: 'us-central1',
    memory: '256MiB',
    timeoutSeconds: 540,
  },
  async () => {
    const today = todayUtc();
    const nowMs = Date.now();
    const snapshot = await fetchPushCandidates();

    let sent = 0, skipped = 0, invalidToken = 0;
    const updates = [];

    for (const doc of snapshot.docs) {
      const u = doc.data();

      if (!u.fcmToken) { skipped++; continue; }
      if (u.lastDailyPushSent === today) { skipped++; continue; }
      if (isWithinFirst24Hours(u, nowMs)) { skipped++; continue; }
      if (u.dailyReward?.lastClaimDate === today) { skipped++; continue; }

      const result = await sendToToken(u.fcmToken, DAILY_REWARD_BODY);
      if (!result.ok) {
        if (isInvalidTokenError(result.code)) {
          updates.push(doc.ref.update({ fcmToken: '' }));
          invalidToken++;
        }
        skipped++;
        continue;
      }

      updates.push(doc.ref.update({ lastDailyPushSent: today }));
      sent++;
    }

    await Promise.all(updates);
    console.log(`[pushDailyRewardCron] total=${snapshot.size} sent=${sent} skipped=${skipped} invalidToken=${invalidToken}`);
  }
);
*/
// ROLLBACK_DAILY_PUSH_DISABLED_20260618: end — pushDailyRewardCron 주석 끝.

// ── Analytics 직접 적재 엔드포인트 (Firebase Analytics→BQ export 대체) ────────
//
// Unity AnalyticsManager 가 이벤트를 배치로 모아 이 엔드포인트에 POST → BigQuery streaming insert.
// 인증: Firebase Auth ID 토큰(Bearer). 모바일 클라가 BQ 자격증명을 직접 들면 키 추출 위험이 있어
//   반드시 서버 경유. 토큰 검증 uid 를 권위값으로 사용(클라가 보낸 uid 는 무시).
// 라우팅/매핑/정렬 상태: firebase/bigquery/README.md. 대상 데이터셋 balloonloop_db 의 이벤트별 타입드 테이블.
//
// 요청 본문: { "events": [ { "name": "<event_name>", "data": { ...flat snake_case params... } }, ... ] }
// 응답: 200 { inserted: N } | 4xx(클라 오류, 재시도 무의미) | 5xx(일시 오류, 클라 재시도 권장)
const BQ_DATASET = 'balloonloop_db';
const MAX_EVENTS_PER_REQUEST = 500;

// 클라 이벤트명(AnalyticsConsts EVT_*) → balloonloop_db 타입드 테이블.
const EVENT_TABLE = {
  session_start_event:    'session_start',
  session_end_event:      'session_end',
  level_play_start_event: 'play_start',
  level_play_event:       'play_event',
  item_use_event:         'item_use',
  purchase_event:         'purchase',
  economy_event:          'economy',
  ad_event:               'ad_event',
};

// 테이블별 컬럼명이 공통과 다른 경우의 rename. (session_start 만 version/country 컬럼 사용)
const FIELD_RENAME = {
  session_start: { app_version: 'version', geo_country: 'country' },
};

// ── user_property UPSERT — ROLLBACK_USER_PROPERTY_PIPELINE_20260708 ─────────────
// [2026-07-09] 시각 컬럼은 TIMESTAMP — 실존 테이블(6/23 셋업)이 xlsx(DATETIME)와 달리 TIMESTAMP 로
//   생성돼 있음(bq show 실측). CAST 를 테이블에 맞춤. toBqDatetime 의 존-제거 문자열은 UTC 로 해석됨(정합).
// R_user_property(v3.2)는 uid 당 1행 UPSERT 테이블 — 스트리밍 append 불가 → DML MERGE.
// 클라 user_property_event(세션 시작/종료 시 발사)를 여기로 라우팅. 특성:
//   · uid 는 토큰 검증값 사용(클라 값 무시 — 이벤트 테이블과 동일 원칙)
//   · last_updated_at 게이트: 구본(재시도/역순 배치)이 신본을 덮지 않음 → 멱등, 클라 재전송 무해
//   · install_* 는 불변(INSERT 시에만), 누적치(total_*)는 GREATEST 로 역행 방지
//   · MMP 계열(install_media_source/campaign/adgroup/creative)·idfa/aid 는 클라 미전송 → NULL 유지
//   · user_property 테이블은 DML 전용(스트리밍 버퍼 없음)이라 MERGE 충돌 없음
// 비용/쿼터: 세션당 1~2회 소형 MERGE — 1.0 규모 무해. 스케일 시 스테이징 append + 주기 MERGE 로 전환.
const USER_PROPERTY_EVENT = 'user_property_event';

const USER_PROP_MERGE_SQL = `
MERGE \`${BQ_DATASET}.user_property\` T
USING (SELECT
  @uid AS uid,
  @game_id AS game_id,
  CAST(@install_at AS TIMESTAMP) AS install_at,
  @install_version AS install_version,
  @install_country AS install_country,
  @install_platform AS install_platform,
  @install_device AS install_device,
  CAST(@last_active_at AS TIMESTAMP) AS last_active_at,
  @last_active_version AS last_active_version,
  @last_active_country AS last_active_country,
  CAST(@last_played_at AS TIMESTAMP) AS last_played_at,
  @max_reached_level AS max_reached_level,
  @total_play_count AS total_play_count,
  @total_clear_count AS total_clear_count,
  @total_coin_balance AS total_coin_balance,
  CAST(@total_spend_usd AS NUMERIC) AS total_spend_usd,
  CAST(@total_ad_revenue_usd AS NUMERIC) AS total_ad_revenue_usd,
  CAST(@infinite_lives_expiry AS TIMESTAMP) AS infinite_lives_expiry,
  @is_payer AS is_payer,
  CAST(@last_updated_at AS TIMESTAMP) AS last_updated_at,
  @appsflyer_id AS appsflyer_id
) S
ON T.uid = S.uid
WHEN MATCHED AND S.last_updated_at >= IFNULL(T.last_updated_at, TIMESTAMP '1970-01-01 00:00:00+00') THEN UPDATE SET
  last_active_at        = S.last_active_at,
  last_active_version   = S.last_active_version,
  last_active_country   = S.last_active_country,
  last_played_at        = COALESCE(S.last_played_at, T.last_played_at),
  max_reached_level     = GREATEST(IFNULL(T.max_reached_level, 0), IFNULL(S.max_reached_level, 0)),
  total_play_count      = GREATEST(IFNULL(T.total_play_count, 0), IFNULL(S.total_play_count, 0)),
  total_clear_count     = GREATEST(IFNULL(T.total_clear_count, 0), IFNULL(S.total_clear_count, 0)),
  total_coin_balance    = S.total_coin_balance,
  total_spend_usd       = GREATEST(IFNULL(T.total_spend_usd, CAST(0 AS NUMERIC)), IFNULL(S.total_spend_usd, CAST(0 AS NUMERIC))),
  total_ad_revenue_usd  = GREATEST(IFNULL(T.total_ad_revenue_usd, CAST(0 AS NUMERIC)), IFNULL(S.total_ad_revenue_usd, CAST(0 AS NUMERIC))),
  infinite_lives_expiry = S.infinite_lives_expiry,
  is_payer              = IFNULL(T.is_payer, FALSE) OR IFNULL(S.is_payer, FALSE),
  appsflyer_id          = COALESCE(S.appsflyer_id, T.appsflyer_id),
  last_updated_at       = S.last_updated_at
WHEN NOT MATCHED THEN INSERT (
  game_id, uid, install_at, install_version, install_country, install_platform, install_device,
  last_active_at, last_active_version, last_active_country, last_played_at,
  max_reached_level, total_play_count, total_clear_count, total_coin_balance,
  total_spend_usd, total_ad_revenue_usd, infinite_lives_expiry, is_payer, last_updated_at, appsflyer_id
) VALUES (
  S.game_id, S.uid, S.install_at, S.install_version, S.install_country, S.install_platform, S.install_device,
  S.last_active_at, S.last_active_version, S.last_active_country, S.last_played_at,
  S.max_reached_level, S.total_play_count, S.total_clear_count, S.total_coin_balance,
  S.total_spend_usd, S.total_ad_revenue_usd, S.infinite_lives_expiry, S.is_payer, S.last_updated_at, S.appsflyer_id
)`;

function toIntOrNull(v) {
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? Math.trunc(n) : null;
}

function toStrOrNull(v) {
  if (v == null) return null;
  const s = String(v);
  return s.length > 0 ? s : null;
}

async function mergeUserProperty(uid, d) {
  const params = {
    uid,
    game_id:               toStrOrNull(d.game_id) || 'balloonloop',
    install_at:            toBqDatetime(d.install_at),
    install_version:       toStrOrNull(d.install_version),
    install_country:       toStrOrNull(d.install_country),
    install_platform:      toStrOrNull(d.install_platform),
    install_device:        toStrOrNull(d.install_device),
    last_active_at:        toBqDatetime(d.last_active_at),
    last_active_version:   toStrOrNull(d.last_active_version),
    last_active_country:   toStrOrNull(d.last_active_country),
    last_played_at:        toBqDatetime(d.last_played_at),
    max_reached_level:     toIntOrNull(d.max_reached_level),
    total_play_count:      toIntOrNull(d.total_play_count),
    total_clear_count:     toIntOrNull(d.total_clear_count),
    total_coin_balance:    toIntOrNull(d.total_coin_balance),
    total_spend_usd:       sanitizeNumeric(d.total_spend_usd),
    total_ad_revenue_usd:  sanitizeNumeric(d.total_ad_revenue_usd),
    infinite_lives_expiry: toBqDatetime(d.infinite_lives_expiry),
    is_payer:              d.is_payer === true,
    last_updated_at:       toBqDatetime(d.last_updated_at) || toBqDatetime(new Date().toISOString()),
    appsflyer_id:          toStrOrNull(d.appsflyer_id),
  };
  // null 파라미터는 타입 추론 불가 → 전 파라미터 명시 타입 (DATETIME 은 SQL 측 CAST, 여기선 STRING).
  const types = {
    uid: 'STRING', game_id: 'STRING',
    install_at: 'STRING', install_version: 'STRING', install_country: 'STRING',
    install_platform: 'STRING', install_device: 'STRING',
    last_active_at: 'STRING', last_active_version: 'STRING', last_active_country: 'STRING',
    last_played_at: 'STRING',
    max_reached_level: 'INT64', total_play_count: 'INT64', total_clear_count: 'INT64',
    total_coin_balance: 'INT64',
    total_spend_usd: 'FLOAT64', total_ad_revenue_usd: 'FLOAT64',
    infinite_lives_expiry: 'STRING', is_payer: 'BOOL', last_updated_at: 'STRING',
    appsflyer_id: 'STRING',
  };
  // location 미지정 — 데이터셋 리전은 라이브러리가 잡 생성 시 해석 (US 외 리전이면 배포 후 1회 확인).
  await bq.query({ query: USER_PROP_MERGE_SQL, params, types });
}

/** 클라 'o' 포맷(2026-..Z, 7프랙) → BQ DATETIME/TIMESTAMP 양립 형식(존 제거, ms). null 안전. */
function toBqDatetime(v) {
  if (v == null) return null;
  const d = new Date(v);
  if (isNaN(d.getTime())) return null;
  return d.toISOString().replace('Z', ''); // e.g. 2026-06-16T05:25:40.123 (DATETIME·TIMESTAMP 모두 허용)
}

// NUMERIC 컬럼(소수 9자리 상한)에 들어가는 실수 필드. 클라 double 누적 노이즈
// (예: total_spend_usd=115.94999999999999)가 오면 행 전체가 거절되고, 부분실패 500 →
// 클라가 배치를 15초마다 무한 재전송하는 poison 루프가 된다 → insert 전 라운딩으로 차단.
const NUMERIC_FIELDS = new Set([
  'total_spend_usd', 'total_ad_revenue_usd',
  'price_usd', 'price_local', 'revenue_usd',
  'peak_resource_usage_ratio', 'avg_resource_usage_ratio',
]);

/** NUMERIC 필드 값 위생 처리 — 유한 실수는 소수 6자리 라운딩, 그 외(NaN/문자열 등)는 null. */
function sanitizeNumeric(v) {
  const n = typeof v === 'number' ? v : Number(v);
  if (!Number.isFinite(n)) return null;
  return Math.round(n * 1e6) / 1e6;
}

exports.ingestAnalyticsEvents = onRequest(
  {
    region: 'us-central1',
    memory: '256MiB',
    timeoutSeconds: 60,
    maxInstances: 20,   // 폭주/비용 상한
    cors: false,        // 모바일 네이티브 클라(UnityWebRequest) — 브라우저 CORS 불필요
  },
  async (req, res) => {
    if (req.method !== 'POST') { res.status(405).json({ error: 'POST only' }); return; }

    // 1) 인증 — Firebase Auth ID 토큰(Bearer)
    const authz = req.get('Authorization') || '';
    const m = authz.match(/^Bearer (.+)$/);
    if (!m) { res.status(401).json({ error: 'missing bearer token' }); return; }
    let decoded;
    try {
      decoded = await getAuth().verifyIdToken(m[1]);
    } catch (e) {
      res.status(401).json({ error: 'invalid token' });
      return;
    }
    const uid = decoded.uid;

    // 2) 본문 검증
    const events = Array.isArray(req.body && req.body.events) ? req.body.events : null;
    if (!events || events.length === 0) { res.status(400).json({ error: 'no events' }); return; }
    if (events.length > MAX_EVENTS_PER_REQUEST) { res.status(413).json({ error: 'too many events' }); return; }

    // 3) 이벤트명 → 테이블별로 행 그룹화. event_ts→event_timestamp 매핑, 컬럼명 rename, uid 권위값.
    const fallbackTs = toBqDatetime(new Date().toISOString());
    const byTable = new Map();
    const userPropEvents = []; // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: MERGE 경로 (스트리밍 아님)
    let skipped = 0;
    for (const ev of events) {
      const name = ev && ev.name;
      const data = (ev && ev.data) || {};
      if (name === USER_PROPERTY_EVENT) { userPropEvents.push(data); continue; }
      const table = EVENT_TABLE[name];
      if (!table) { skipped++; continue; }              // 알 수 없는 이벤트 스킵

      const rename = FIELD_RENAME[table] || {};
      const row = { uid };                              // 토큰 검증 uid (클라 값 무시)
      for (const [k0, val] of Object.entries(data)) {
        if (k0 === 'uid') continue;
        if (k0 === 'event_ts') { row.event_timestamp = toBqDatetime(val); continue; }
        const k = rename[k0] || k0;
        row[k] = NUMERIC_FIELDS.has(k) ? sanitizeNumeric(val) : val;
      }
      if (!row.event_timestamp) row.event_timestamp = fallbackTs;

      if (!byTable.has(table)) byTable.set(table, []);
      // insertId = event_id → streaming best-effort 중복제거(클라 재시도 중복 흡수)
      byTable.get(table).push({ insertId: row.event_id || undefined, json: row });
    }
    if (byTable.size === 0 && userPropEvents.length === 0) { res.status(400).json({ error: 'no valid events' }); return; }

    // 4) 테이블별 BigQuery streaming insert. ignoreUnknownValues — 타겟에 없는 컬럼은 무시(클라/스키마 드리프트 내성).
    const tables = [...byTable.keys()];
    const settled = await Promise.allSettled(tables.map((t) =>
      bq.dataset(BQ_DATASET).table(t).insert(byTable.get(t), {
        raw: true,
        skipInvalidRows: false,
        ignoreUnknownValues: true,
      })
    ));

    let inserted = 0;
    const failedTables = [];
    settled.forEach((r, i) => {
      const t = tables[i];
      const n = byTable.get(t).length;
      if (r.status === 'fulfilled') { inserted += n; return; }
      failedTables.push(t);
      const err = r.reason;
      const detail = (err && err.errors) ? JSON.stringify(err.errors.slice(0, 3)) : (err && err.message);
      console.error(`[ingestAnalyticsEvents] insert failed table=${t} rows=${n} detail=${detail}`);
    });

    // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: user_property MERGE UPSERT.
    //   요청 내 여러 건이면 last_updated_at 최신 1건만 반영(uid 는 토큰상 동일).
    //   실패 시 failedTables 에 포함 → 5xx → 클라 배치 재시도(MERGE 는 last_updated_at 게이트로 멱등).
    if (userPropEvents.length > 0) {
      try {
        const latest = userPropEvents.reduce((a, b) =>
          String(b.last_updated_at || '') > String(a.last_updated_at || '') ? b : a);
        await mergeUserProperty(uid, latest);
        inserted += userPropEvents.length;
      } catch (e) {
        failedTables.push('user_property');
        console.error(`[ingestAnalyticsEvents] user_property MERGE failed detail=${e && e.message}`);
      }
    }

    if (failedTables.length > 0) {
      // 일부 테이블 실패 — 클라가 배치 전체 재시도(insertId 로 성공분 중복 흡수). 5xx 로 응답.
      res.status(500).json({ inserted, failedTables, skipped });
      return;
    }
    res.status(200).json({ inserted, skipped });
  }
);

// ── IAP 영수증 검증 — 방식 B (아웃게임디렉션 §7, QA 블로커 #2 해소) ─────────────
//
// Unity IAPManager 가 ProcessPurchase 에서 Pending 을 반환한 뒤 이 엔드포인트로 영수증을 보낸다.
// Google Play Developer API 로 구매 상태를 검증하고 Firestore(iap_receipts)에 멱등 기록 후
// 지급 허가를 반환. 클라는 valid=true && !alreadyProcessed 일 때만 보상 지급 후
// ConfirmPendingPurchase 로 트랜잭션을 확정한다.
//
// 응답 규약:
//   200 { valid:true,  alreadyProcessed }        → 지급(최초) 또는 confirm only(재전달)
//   200 { valid:false, definitive:true }         → 위조/취소 — 지급 없이 confirm (재시도 무의미)
//   200 { valid:false, definitive:false }        → 결제 보류(pending) — confirm 보류, 클라 재시도
//   401 / 5xx                                    → 일시 오류 — confirm 보류, 클라 재시도
//
// 1회 셋업 (배포 전):
//   1) GCP 콘솔 → Google Play Android Developer API 활성화 (프로젝트 balloonloop-d855d).
//   2) Play Console → 설정 → API 액세스 → 함수 런타임 SA
//      (<project-number>-compute@developer.gserviceaccount.com) 초대,
//      권한: '앱 정보 보기(읽기 전용)' + '주문 및 정기 결제 관리'.
//   3) cd firebase/functions && npm install (google-auth-library 추가됨)
//   4) firebase deploy --only functions:validatePurchase
const ANDROID_PACKAGE_NAME = 'xyz.aimed.balloonloop';

let _playAuthClientPromise = null;
function getPlayAuthClient() {
  if (!_playAuthClientPromise) {
    _playAuthClientPromise = new GoogleAuth({
      scopes: ['https://www.googleapis.com/auth/androidpublisher'],
    }).getClient();
  }
  return _playAuthClientPromise;
}

exports.validatePurchase = onRequest(
  {
    region: 'us-central1',
    memory: '256MiB',
    timeoutSeconds: 30,
    maxInstances: 10,
    cors: false,
  },
  async (req, res) => {
    if (req.method !== 'POST') { res.status(405).json({ error: 'POST only' }); return; }

    // 1) 인증 — ingestAnalyticsEvents 와 동일하게 Firebase Auth ID 토큰(Bearer)
    const authz = req.get('Authorization') || '';
    const m = authz.match(/^Bearer (.+)$/);
    if (!m) { res.status(401).json({ error: 'missing bearer token' }); return; }
    let decoded;
    try {
      decoded = await getAuth().verifyIdToken(m[1]);
    } catch (e) {
      res.status(401).json({ error: 'invalid token' });
      return;
    }
    const uid = decoded.uid;

    const { productId, purchaseToken, orderId } = req.body || {};
    if (!productId || !purchaseToken) {
      res.status(400).json({ error: 'productId/purchaseToken required' });
      return;
    }

    // 2) Google Play Developer API — purchases.products.get
    let purchase;
    try {
      const client = await getPlayAuthClient();
      const { token } = await client.getAccessToken();
      const url = `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${ANDROID_PACKAGE_NAME}`
        + `/purchases/products/${encodeURIComponent(productId)}/tokens/${encodeURIComponent(purchaseToken)}`;
      const r = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });

      if (r.status === 400 || r.status === 404) {
        // 토큰/상품 불일치 — 위조 또는 무효 영수증. 정정 불가(definitive).
        console.warn(`[validatePurchase] Play API ${r.status} — invalid receipt uid=${uid} product=${productId}`);
        res.status(200).json({ valid: false, definitive: true, reason: `play_${r.status}` });
        return;
      }
      if (!r.ok) {
        // 401/403 = SA 미초대/권한 부족(셋업 문제), 5xx = Play 측 장애 — 클라 재시도 대상.
        console.error(`[validatePurchase] Play API ${r.status} — SA 초대/권한 또는 일시 장애 확인 필요.`);
        res.status(502).json({ error: `play_api_${r.status}` });
        return;
      }
      purchase = await r.json();
    } catch (e) {
      console.error('[validatePurchase] Play API 호출 실패:', e);
      res.status(502).json({ error: 'play_api_unreachable' });
      return;
    }

    // 3) 구매 상태 — 0=구매완료 / 1=취소 / 2=결제보류(pending, 편의점 결제 등)
    if (purchase.purchaseState === 2) {
      res.status(200).json({ valid: false, definitive: false, reason: 'payment_pending' });
      return;
    }
    if (purchase.purchaseState !== 0) {
      console.warn(`[validatePurchase] purchaseState=${purchase.purchaseState} — 거부. uid=${uid} product=${productId}`);
      res.status(200).json({ valid: false, definitive: true, reason: `state_${purchase.purchaseState}` });
      return;
    }

    // 4) Firestore 멱등 기록 — purchaseToken 해시가 문서 키. 이미 지급 허가된 토큰이면 alreadyProcessed
    //    (클라가 지급 후 confirm 전에 종료돼 스토어가 재전달한 케이스의 중복 지급 방지).
    const docId = crypto.createHash('sha256').update(purchaseToken).digest('hex');
    const ref = db.collection('iap_receipts').doc(docId);
    let alreadyProcessed = false;
    try {
      await db.runTransaction(async (tx) => {
        const snap = await tx.get(ref);
        if (snap.exists) { alreadyProcessed = true; return; }
        tx.set(ref, {
          uid,
          productId,
          orderId: purchase.orderId || orderId || '',
          purchaseTimeMillis: purchase.purchaseTimeMillis || null,
          grantedAt: FieldValue.serverTimestamp(),
        });
      });
    } catch (e) {
      // 지급 허가 기록 전 실패 — 허가 자체를 내리지 않음(클라 재시도, Pending 유지).
      console.error('[validatePurchase] Firestore 기록 실패:', e);
      res.status(500).json({ error: 'receipt_store_failed' });
      return;
    }

    res.status(200).json({
      valid: true,
      definitive: true,
      alreadyProcessed,
      orderId: purchase.orderId || orderId || '',
    });
  }
);
