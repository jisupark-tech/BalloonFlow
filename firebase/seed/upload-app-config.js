#!/usr/bin/env node
/**
 * Firestore /config/app 단일 doc 업로더 (Admin SDK).
 *   강제 업데이트용 minSupportedVersion 등 앱 전역 config.
 *
 * 사용법:
 *   1) Node.js 18+, firebase/seed/ 안에서 npm install (이미 다른 uploader 로 설치돼 있으면 skip)
 *   2) service-account.json 준비
 *   3) node upload-app-config.js
 *
 * 옵션:
 *   --collection <name>     기본 "config"
 *   --doc-id <id>           기본 "app"
 *   --source <path>         기본 ./app/config.json
 *   --service-account <p>   키 경로 (기본 ./service-account.json)
 *   --dry-run               업로드 안 하고 출력만
 *
 * Firestore 경로: /config/app (단일 doc)
 * 멱등: 재업로드는 doc 덮어쓰기 — updatedAt 만 새로 갱신.
 *
 * ※ 값만 바꿔 재업로드하면 앱 재배포 없이 강제 업데이트 기준을 바꿀 수 있음.
 *   예) minSupportedVersion "1.0.2" 로 올리면 1.0.1 이하 클라이언트가 강제 업데이트.
 */

'use strict';

const fs    = require('fs');
const path  = require('path');
const admin = require('firebase-admin');

function parseArgs() {
  const args = {
    collection: 'config',
    docId: 'app',
    source: './app/config.json',
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
      console.log('Usage: node upload-app-config.js [--collection config] [--doc-id app] [--source ./app/config.json] [--dry-run] [--service-account path.json]');
      process.exit(0);
    }
  }
  return args;
}

const args       = parseArgs();
const SCRIPT_DIR = __dirname;
const SRC_PATH   = path.isAbsolute(args.source)         ? args.source         : path.join(SCRIPT_DIR, args.source);
const KEY_PATH   = path.isAbsolute(args.serviceAccount) ? args.serviceAccount : path.join(SCRIPT_DIR, args.serviceAccount);

if (!fs.existsSync(SRC_PATH)) {
  console.error(`[upload-app-config] source 파일 없음: ${SRC_PATH}`);
  process.exit(1);
}
if (!fs.existsSync(KEY_PATH)) {
  console.error(`[upload-app-config] service-account.json 없음: ${KEY_PATH}`);
  process.exit(1);
}

const serviceAccount = require(KEY_PATH);
admin.initializeApp({ credential: admin.credential.cert(serviceAccount) });
const db = admin.firestore();

async function run() {
  let raw;
  try { raw = fs.readFileSync(SRC_PATH, 'utf8'); }
  catch (e) { console.error(`[upload-app-config] ${SRC_PATH} 읽기 실패:`, e.message); process.exit(1); }

  let doc;
  try { doc = JSON.parse(raw); }
  catch (e) { console.error(`[upload-app-config] JSON 파싱 실패:`, e.message); process.exit(1); }

  if (!doc.minSupportedVersion || typeof doc.minSupportedVersion !== 'string') {
    console.error('[upload-app-config] 필수 필드 누락/형식오류: minSupportedVersion (string)');
    process.exit(1);
  }

  const docData = {
    ...doc,
    updatedAt: admin.firestore.FieldValue.serverTimestamp(),
  };

  console.log(`[upload-app-config] project=${serviceAccount.project_id} /${args.collection}/${args.docId}`);
  console.log(`  minSupportedVersion=${doc.minSupportedVersion}  dryRun=${args.dryRun}`);

  if (args.dryRun) { console.log('  (dry-run — 업로드 생략)'); return; }

  const ref = db.collection(args.collection).doc(args.docId);
  await ref.set(docData);
  console.log(`  ✔ /${args.collection}/${args.docId} uploaded`);
}

run().catch(err => {
  console.error('[upload-app-config] failed:', err);
  process.exit(1);
});
