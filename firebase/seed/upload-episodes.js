#!/usr/bin/env node
/**
 * Firestore /episodes 컬렉션 일괄 업로더 (Admin SDK).
 *
 * 사용법:
 *   1) Node.js 18+, firebase/seed/ 안에서 npm install (seed.js 와 동일 deps)
 *   2) service-account.json 준비 (seed.js README 참고)
 *   3) Unity Editor 에서 "BalloonFlow/Level Episodes/Export All Episodes" 실행
 *      → firebase/seed/episodes/episode_XX.json 생성됨
 *   4) node upload-episodes.js
 *
 * 옵션:
 *   --collection <name>    기본 "episodes"
 *   --episodes-dir <path>  기본 ./episodes
 *   --dry-run              업로드 안 하고 출력만
 *   --service-account <p>  키 경로 override (기본 ./service-account.json)
 *
 * Firestore 스키마:
 *   /episodes/{packageId}  doc:
 *     - packageId  : number
 *     - levelCount : number
 *     - version    : number
 *     - levelsJson : string  (JsonUtility-serialized LevelEpisode wrapper)
 *     - updatedAt  : serverTimestamp
 *
 * 멱등: 같은 episode 재업로드는 doc 덮어쓰기.
 */

'use strict';

const fs    = require('fs');
const path  = require('path');
const zlib  = require('zlib');
const admin = require('firebase-admin');

const FIRESTORE_DOC_LIMIT = 1_048_487;  // bytes (1 MiB minus metadata overhead)
const SAFETY_BUDGET       =   950_000;  // 여유 두고 자르기

function parseArgs() {
  const args = {
    collection: 'episodes',
    episodesDir: './episodes',
    dryRun: false,
    serviceAccount: './service-account.json',
  };
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--collection')           args.collection     = argv[++i];
    else if (a === '--episodes-dir')    args.episodesDir    = argv[++i];
    else if (a === '--dry-run')         args.dryRun         = true;
    else if (a === '--service-account') args.serviceAccount = argv[++i];
    else if (a === '-h' || a === '--help') {
      console.log('Usage: node upload-episodes.js [--collection episodes] [--episodes-dir ./episodes] [--dry-run] [--service-account path.json]');
      process.exit(0);
    }
  }
  return args;
}

const args = parseArgs();
const SCRIPT_DIR    = __dirname;
const EP_DIR        = path.isAbsolute(args.episodesDir) ? args.episodesDir : path.join(SCRIPT_DIR, args.episodesDir);
const KEY_PATH      = path.isAbsolute(args.serviceAccount) ? args.serviceAccount : path.join(SCRIPT_DIR, args.serviceAccount);

if (!fs.existsSync(EP_DIR)) {
  console.error(`[upload-episodes] episodes 디렉토리 없음: ${EP_DIR}`);
  console.error('       Unity Editor: BalloonFlow > Level Episodes > Export All Episodes 먼저 실행');
  process.exit(1);
}
if (!fs.existsSync(KEY_PATH)) {
  console.error(`[upload-episodes] service-account.json 없음: ${KEY_PATH}`);
  process.exit(1);
}

const serviceAccount = require(KEY_PATH);
admin.initializeApp({ credential: admin.credential.cert(serviceAccount) });
const db = admin.firestore();

// episode_XX.json 파일 수집
const files = fs.readdirSync(EP_DIR)
  .filter(f => /^episode_\d+\.json$/.test(f))
  .sort();

if (files.length === 0) {
  console.error(`[upload-episodes] ${EP_DIR} 안에 episode_*.json 없음`);
  process.exit(1);
}

async function run() {
  console.log(`[upload-episodes] project=${serviceAccount.project_id} collection=${args.collection} files=${files.length} dryRun=${args.dryRun}`);

  for (const file of files) {
    const fullPath = path.join(EP_DIR, file);
    let raw;
    try {
      raw = fs.readFileSync(fullPath, 'utf8');
    } catch (e) {
      console.error(`  ! ${file} 읽기 실패:`, e.message);
      continue;
    }

    let ep;
    try {
      ep = JSON.parse(raw);
    } catch (e) {
      console.error(`  ! ${file} JSON 파싱 실패:`, e.message);
      continue;
    }

    if (typeof ep.packageId !== 'number' || !Array.isArray(ep.levels)) {
      console.error(`  ! ${file} 스키마 오류 — packageId/levels 없음`);
      continue;
    }

    // Firestore 문서 1 MiB 제한 — 항상 gzip + base64.
    const gz   = zlib.gzipSync(raw, { level: zlib.constants.Z_BEST_COMPRESSION });
    const b64  = gz.toString('base64');
    const docData = {
      packageId  : ep.packageId,
      levelCount : ep.levels.length,
      version    : ep.version || 1,
      encoding   : 'gzip+b64',     // 클라는 b64-decode + gunzip 후 JsonUtility.FromJson<LevelEpisode>
      levelsJson : b64,
      rawSize    : raw.length,
      updatedAt  : admin.firestore.FieldValue.serverTimestamp(),
    };

    const ratio = (b64.length / raw.length * 100).toFixed(1);
    console.log(`  - pkg ${ep.packageId}  levels=${ep.levels.length}  raw=${raw.length}  gz+b64=${b64.length} (${ratio}%)`);

    if (b64.length > SAFETY_BUDGET)
    {
      console.error(`  ! pkg ${ep.packageId} 압축 후에도 ${b64.length} > ${SAFETY_BUDGET} bytes — Firestore 1MiB 제한 위험. 에피소드 분할 필요.`);
      continue;
    }
    if (args.dryRun) continue;

    const ref = db.collection(args.collection).doc(String(ep.packageId));
    await ref.set(docData);
    console.log(`    ✔ /${args.collection}/${ep.packageId} uploaded`);
  }

  console.log('[upload-episodes] done.');
}

run().catch(err => {
  console.error('[upload-episodes] failed:', err);
  process.exit(1);
});
