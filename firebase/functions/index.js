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
const { getFirestore } = require('firebase-admin/firestore');
const { getMessaging } = require('firebase-admin/messaging');
const { getAuth } = require('firebase-admin/auth');
const { BigQuery } = require('@google-cloud/bigquery');

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

/** 클라 'o' 포맷(2026-..Z, 7프랙) → BQ DATETIME/TIMESTAMP 양립 형식(존 제거, ms). null 안전. */
function toBqDatetime(v) {
  if (v == null) return null;
  const d = new Date(v);
  if (isNaN(d.getTime())) return null;
  return d.toISOString().replace('Z', ''); // e.g. 2026-06-16T05:25:40.123 (DATETIME·TIMESTAMP 모두 허용)
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
    let skipped = 0;
    for (const ev of events) {
      const name = ev && ev.name;
      const data = (ev && ev.data) || {};
      const table = EVENT_TABLE[name];
      if (!table) { skipped++; continue; }              // 알 수 없는 이벤트 스킵

      const rename = FIELD_RENAME[table] || {};
      const row = { uid };                              // 토큰 검증 uid (클라 값 무시)
      for (const [k0, val] of Object.entries(data)) {
        if (k0 === 'uid') continue;
        if (k0 === 'event_ts') { row.event_timestamp = toBqDatetime(val); continue; }
        const k = rename[k0] || k0;
        row[k] = val;
      }
      if (!row.event_timestamp) row.event_timestamp = fallbackTs;

      if (!byTable.has(table)) byTable.set(table, []);
      // insertId = event_id → streaming best-effort 중복제거(클라 재시도 중복 흡수)
      byTable.get(table).push({ insertId: row.event_id || undefined, json: row });
    }
    if (byTable.size === 0) { res.status(400).json({ error: 'no valid events' }); return; }

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

    if (failedTables.length > 0) {
      // 일부 테이블 실패 — 클라가 배치 전체 재시도(insertId 로 성공분 중복 흡수). 5xx 로 응답.
      res.status(500).json({ inserted, failedTables, skipped });
      return;
    }
    res.status(200).json({ inserted, skipped });
  }
);
