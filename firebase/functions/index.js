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
const { initializeApp } = require('firebase-admin/app');
const { getFirestore } = require('firebase-admin/firestore');
const { getMessaging } = require('firebase-admin/messaging');

initializeApp();
const db = getFirestore();
const fcm = getMessaging();

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
