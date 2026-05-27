#!/usr/bin/env node
/**
 * Firestore /config/winningStreak 단일 doc 업로더 (Admin SDK).
 *
 * 사용법:
 *   1) Node.js 18+, firebase/seed/ 안에서 npm install (이미 upload-episodes.js 에서 설치돼 있으면 skip)
 *   2) service-account.json 준비
 *   3) node upload-winning-streak.js
 *
 * 옵션:
 *   --collection <name>     기본 "config"
 *   --doc-id <id>           기본 "winningStreak"
 *   --source <path>         기본 ./winningStreak/config.json
 *   --service-account <p>   키 경로 (기본 ./service-account.json)
 *   --dry-run               업로드 안 하고 출력만
 *
 * Firestore 경로: /config/winningStreak (단일 doc)
 *
 * 멱등: 재업로드는 doc 덮어쓰기 — updatedAt 만 새로 갱신.
 */

'use strict';

const fs    = require('fs');
const path  = require('path');
const admin = require('firebase-admin');

function parseArgs() {
  const args = {
    collection: 'config',
    docId: 'winningStreak',
    source: './winningStreak/config.json',
    serviceAccount: './service-account.json',
    dryRun: false,
  };
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if      (a === '--collection')      args.collection     = argv[++i];
    else if (a === '--doc-id')          args.docId          = argv[++i];
    else if (a === '--source')          args.source         = argv[++i];
    else if (a === '--service-account') args.serviceAccount = argv[++i];
    else if (a === '--dry-run')         args.dryRun         = true;
    else if (a === '-h' || a === '--help') {
      console.log('Usage: node upload-winning-streak.js [--collection config] [--doc-id winningStreak] [--source ./winningStreak/config.json] [--dry-run] [--service-account path.json]');
      process.exit(0);
    }
  }
  return args;
}

const args        = parseArgs();
const SCRIPT_DIR  = __dirname;
const SRC_PATH    = path.isAbsolute(args.source)         ? args.source         : path.join(SCRIPT_DIR, args.source);
const KEY_PATH    = path.isAbsolute(args.serviceAccount) ? args.serviceAccount : path.join(SCRIPT_DIR, args.serviceAccount);

if (!fs.existsSync(SRC_PATH)) {
  console.error(`[upload-winning-streak] source 파일 없음: ${SRC_PATH}`);
  console.error('       Unity Editor: BalloonFlow > Winning Streak > Export Config 먼저 실행하거나,');
  console.error('       firebase/seed/winningStreak/config.json 가 존재하는지 확인.');
  process.exit(1);
}
if (!fs.existsSync(KEY_PATH)) {
  console.error(`[upload-winning-streak] service-account.json 없음: ${KEY_PATH}`);
  process.exit(1);
}

const serviceAccount = require(KEY_PATH);
admin.initializeApp({ credential: admin.credential.cert(serviceAccount) });
const db = admin.firestore();

async function run() {
  let raw;
  try { raw = fs.readFileSync(SRC_PATH, 'utf8'); }
  catch (e) {
    console.error(`[upload-winning-streak] ${SRC_PATH} 읽기 실패:`, e.message);
    process.exit(1);
  }

  let doc;
  try { doc = JSON.parse(raw); }
  catch (e) {
    console.error(`[upload-winning-streak] JSON 파싱 실패:`, e.message);
    process.exit(1);
  }

  // 최소 검증 — 필수 필드 누락 시 abort.
  const requiredKeys = ['unlockLevel', 'streakMultipliers', 'difficultyMultipliers', 'boosterCosts', 'stages'];
  for (const k of requiredKeys) {
    if (!(k in doc)) {
      console.error(`[upload-winning-streak] 필수 필드 누락: ${k}`);
      process.exit(1);
    }
  }
  if (!Array.isArray(doc.stages) || doc.stages.length === 0) {
    console.error('[upload-winning-streak] stages 가 비어있음');
    process.exit(1);
  }

  // updatedAt 만 추가. 나머지는 JSON 그대로 SET (덮어쓰기).
  const docData = {
    ...doc,
    updatedAt: admin.firestore.FieldValue.serverTimestamp(),
  };

  console.log(`[upload-winning-streak] project=${serviceAccount.project_id} /${args.collection}/${args.docId}`);
  console.log(`  unlockLevel=${doc.unlockLevel}  stages=${doc.stages.length}  dryRun=${args.dryRun}`);

  if (args.dryRun) {
    console.log('  (dry-run — 업로드 생략)');
    return;
  }

  const ref = db.collection(args.collection).doc(args.docId);
  await ref.set(docData);
  console.log(`  ✔ /${args.collection}/${args.docId} uploaded`);
}

run().catch(err => {
  console.error('[upload-winning-streak] failed:', err);
  process.exit(1);
});
