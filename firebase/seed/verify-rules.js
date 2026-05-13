#!/usr/bin/env node
/**
 * Firestore Rules 라이브 검증.
 *
 * Admin SDK 가 Rules 를 우회하는 것과 달리, 이 스크립트는 실제 Anonymous Auth 토큰으로
 * 공개 REST 엔드포인트를 호출 → 클라가 보는 권한과 동일한 결과 산출.
 *
 * 검증 매트릭스:
 *   1) /products/{pid}              → allow read (public)
 *   2) /episodes/{1,2,3}            → allow read (public)
 *   3) /dailyRewards/{doc}          → allow read (public)
 *   4) /users/{own_uid}             → allow read (auth.uid 매치)
 *   5) /users/{other_uid}           → DENY (다른 uid)
 *   6) /users/{own_uid} write       → allow
 *   7) /users/{other_uid} write     → DENY
 *
 * 사용:
 *   node verify-rules.js
 *
 * 결과: 각 케이스의 HTTP status + 예상 일치 여부 표시.
 */

'use strict';

const PROJECT_ID = 'balloonloop-d855d';
const API_KEY    = 'AIzaSyCpBSxrrG-0LC2D1Xz9HExeeaaG0WPckhY';  // google-services.json 의 current_key

const SIGNUP_URL    = `https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${API_KEY}`;
const FIRESTORE_BASE = `https://firestore.googleapis.com/v1/projects/${PROJECT_ID}/databases/(default)/documents`;

async function signInAnonymously() {
  const res = await fetch(SIGNUP_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ returnSecureToken: true }),
  });
  if (!res.ok) {
    const txt = await res.text();
    throw new Error(`Anonymous sign-in 실패 ${res.status}: ${txt}`);
  }
  const data = await res.json();
  return { idToken: data.idToken, uid: data.localId };
}

async function getDoc(path, idToken) {
  const url = `${FIRESTORE_BASE}/${path}`;
  const res = await fetch(url, {
    method: 'GET',
    headers: { 'Authorization': `Bearer ${idToken}` },
  });
  return { status: res.status, ok: res.ok };
}

async function patchDoc(path, idToken) {
  const url = `${FIRESTORE_BASE}/${path}?updateMask.fieldPaths=lastLoginAt`;
  const res = await fetch(url, {
    method: 'PATCH',
    headers: { 'Authorization': `Bearer ${idToken}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ fields: { lastLoginAt: { timestampValue: new Date().toISOString() } } }),
  });
  return { status: res.status, ok: res.ok };
}

function fmt(actual, expected) {
  const verdict = (actual === expected) ? '✅' : '❌';
  return `${verdict} actual=${actual} expected=${expected}`;
}

async function run() {
  console.log(`[verify-rules] project=${PROJECT_ID}`);
  console.log('[verify-rules] Anonymous sign-in...');
  const { idToken, uid } = await signInAnonymously();
  console.log(`  ✔ uid=${uid}\n`);

  const otherUid = 'fake_other_uid_for_test_xyzzy';
  const cases = [
    // public reads
    { label: 'GET  /products/xyz.aimed.balloonloop.noads',  fn: () => getDoc('products/xyz.aimed.balloonloop.noads', idToken),  expect: 'allow' },
    { label: 'GET  /episodes/1',                            fn: () => getDoc('episodes/1', idToken),                              expect: 'allow' },
    { label: 'GET  /episodes/2',                            fn: () => getDoc('episodes/2', idToken),                              expect: 'allow' },
    { label: 'GET  /episodes/3',                            fn: () => getDoc('episodes/3', idToken),                              expect: 'allow' },
    { label: 'GET  /dailyRewards/default',                  fn: () => getDoc('dailyRewards/default', idToken),                    expect: 'allow' },

    // /users own
    { label: `GET  /users/${uid} (own)`,                    fn: () => getDoc(`users/${uid}`, idToken),                            expect: 'allow' },
    { label: `PATCH /users/${uid} (own)`,                   fn: () => patchDoc(`users/${uid}`, idToken),                          expect: 'allow' },

    // /users other (must deny)
    { label: `GET  /users/${otherUid} (other)`,             fn: () => getDoc(`users/${otherUid}`, idToken),                       expect: 'deny' },
    { label: `PATCH /users/${otherUid} (other)`,            fn: () => patchDoc(`users/${otherUid}`, idToken),                     expect: 'deny' },
  ];

  let pass = 0, fail = 0;
  for (const c of cases) {
    let actual = 'error';
    let status = 0;
    try {
      const r = await c.fn();
      status = r.status;
      // 200 OK 또는 404 (doc 없음, 하지만 read 권한은 있음) = allow
      // 403/permission_denied = deny
      // 401 = auth invalid
      if (status === 200) actual = 'allow';
      else if (status === 404) actual = 'allow (doc missing)';
      else if (status === 403) actual = 'deny';
      else if (status === 401) actual = 'unauthenticated';
      else actual = `http${status}`;
    } catch (e) {
      actual = `error: ${e.message}`;
    }

    const ok = (
      (c.expect === 'allow' && (actual === 'allow' || actual === 'allow (doc missing)')) ||
      (c.expect === 'deny'  && actual === 'deny')
    );
    if (ok) pass++; else fail++;
    const mark = ok ? '✅' : '❌';
    console.log(`  ${mark} ${c.label}`);
    console.log(`     → ${actual}  (expected=${c.expect}, http=${status})`);
  }

  console.log(`\n[verify-rules] ${pass} passed / ${fail} failed`);
  process.exit(fail > 0 ? 1 : 0);
}

run().catch(err => {
  console.error('[verify-rules] failed:', err.message);
  process.exit(1);
});
