#!/usr/bin/env node
/**
 * Firestore /products 컬렉션 일괄 업로더 (Admin SDK).
 *
 * 사용법:
 *   1) Node.js 18+ 설치
 *   2) firebase/seed/ 안에서  npm install
 *   3) Firebase Console > 프로젝트 설정 > 서비스 계정 > "새 비공개 키 생성"
 *      → 다운로드된 JSON 을 firebase/seed/service-account.json 로 저장 (.gitignore 됨)
 *   4) node seed.js
 *
 * 옵션:
 *   --collection <name>   기본 "products"
 *   --dry-run             업로드 안 하고 출력만
 *   --service-account <path>  키 경로 override (기본 ./service-account.json)
 *
 * 멱등: 같은 파일 여러 번 실행해도 동일 doc 으로 set (merge:false 덮어쓰기).
 */

'use strict';

const fs   = require('fs');
const path = require('path');
const admin = require('firebase-admin');

// ─── args ───────────────────────────────────────────────────────────────
function parseArgs() {
  const args = { collection: 'products', dryRun: false, serviceAccount: './service-account.json' };
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--collection')     args.collection     = argv[++i];
    else if (a === '--dry-run')   args.dryRun         = true;
    else if (a === '--service-account') args.serviceAccount = argv[++i];
    else if (a === '-h' || a === '--help') {
      console.log('Usage: node seed.js [--collection products] [--dry-run] [--service-account path.json]');
      process.exit(0);
    }
  }
  return args;
}

const args = parseArgs();
const SCRIPT_DIR    = __dirname;
const SEED_FILE     = path.join(SCRIPT_DIR, 'products.json');
const KEY_PATH_RAW  = args.serviceAccount;
const KEY_PATH      = path.isAbsolute(KEY_PATH_RAW) ? KEY_PATH_RAW : path.join(SCRIPT_DIR, KEY_PATH_RAW);

if (!fs.existsSync(SEED_FILE)) {
  console.error(`[seed] products.json 없음: ${SEED_FILE}`);
  process.exit(1);
}
if (!fs.existsSync(KEY_PATH)) {
  console.error(`[seed] service-account.json 없음: ${KEY_PATH}`);
  console.error('       Firebase Console > 프로젝트 설정 > 서비스 계정 > 새 비공개 키 생성 후 저장');
  process.exit(1);
}

// ─── load ───────────────────────────────────────────────────────────────
const products = JSON.parse(fs.readFileSync(SEED_FILE, 'utf8'));
if (!Array.isArray(products)) {
  console.error('[seed] products.json 의 최상위는 배열이어야 함');
  process.exit(1);
}

const serviceAccount = require(KEY_PATH);
admin.initializeApp({ credential: admin.credential.cert(serviceAccount) });
const db = admin.firestore();

// ─── upload ─────────────────────────────────────────────────────────────
async function run() {
  console.log(`[seed] project=${serviceAccount.project_id} collection=${args.collection} count=${products.length} dryRun=${args.dryRun}`);
  if (args.dryRun) {
    products.forEach(p => console.log(`  - ${p.productId}  ($${p.priceUsd})  ${p.category}`));
    return;
  }

  const BATCH_SIZE = 400; // Firestore batch limit = 500
  for (let start = 0; start < products.length; start += BATCH_SIZE) {
    const batch = db.batch();
    const slice = products.slice(start, start + BATCH_SIZE);
    for (const p of slice) {
      if (!p.productId) {
        console.warn('  ! productId 누락 — skip:', p);
        continue;
      }
      const ref = db.collection(args.collection).doc(p.productId);
      batch.set(ref, p);
    }
    await batch.commit();
    console.log(`  ✔ batch ${start + 1}..${start + slice.length} committed`);
  }
  console.log('[seed] done.');
}

run().catch(err => {
  console.error('[seed] failed:', err);
  process.exit(1);
});
