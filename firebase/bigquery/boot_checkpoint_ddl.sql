-- ROLLBACK_BOOT_CHECKPOINTS_20260713
-- boot_checkpoint — 부팅→첫플레이 퍼널 계측 테이블 (append 스트리밍, play_event 등과 동일 방식).
--
-- 목적: 설치 후 "첫 레벨 미도달" 유저(BQ 실측 설치의 ~23%, 그 중 절반이 play_start 조차 없음)가
--   부팅 파이프라인의 어느 단계에서 이탈하는지 규명 + P0(타이틀 네트워크 게이트 수정) 실효 측정.
--
-- 퍼널: session_start(앱 열기) → boot_checkpoint(stage_index 0~5 로딩step / 6 loading_complete /
--        7 enter / -1 net_gate_offline) → level_play_start(레벨 로드).
--   · session_start 있고 stage_index MAX < 7  → 로딩 중 이탈 (어느 step 인지 = MAX+1)
--   · net_gate_offline(-1) 존재            → 오프라인 게이트 진입(P0 타깃 코호트)
--   · stage_index 7(enter) 있고 play_start 없음 → enter~레벨로드 사이 이탈(크래시 의심)
--
-- ⚠️ 활성화 순서 엄수 (poison 방지):
--   (1) 이 DDL 로 테이블 생성  →  (2) functions/index.js EVENT_TABLE 의 boot_checkpoint_event 주석 해제
--   →  (3) firebase deploy --only functions:ingestAnalyticsEvents
--   테이블 부재 상태로 서버 라우팅을 켜면 insert 실패→500→클라 배치 무한 재시도(poison). 그 전까지
--   클라가 이벤트를 보내도 서버가 unknown 으로 안전 스킵되므로 무해.
--
-- 1회 실행:
--   bq query --use_legacy_sql=false < firebase/bigquery/boot_checkpoint_ddl.sql

CREATE TABLE IF NOT EXISTS `balloonloop_db.boot_checkpoint` (
  event_id        STRING    OPTIONS(description='이벤트 고유 ID (streaming insertId 중복제거 키)'),
  session_id      STRING    OPTIONS(description='세션 ID (session_start 와 조인)'),
  game_id         STRING    OPTIONS(description='게임 식별자'),
  uid             STRING    OPTIONS(description='유저 ID (서버 토큰 검증값으로 덮어씀)'),
  event_timestamp TIMESTAMP OPTIONS(description='발화 시각 (UTC)'),
  app_version     STRING    OPTIONS(description='앱 버전'),
  geo_country     STRING    OPTIONS(description='발화 시점 국가'),
  platform        STRING    OPTIONS(description='android / ios / editor'),
  device_model    STRING    OPTIONS(description='디바이스 모델 (저사양 상관분석용)'),
  stage           STRING    OPTIONS(description='단계 라벨(예: "Connecting server...", "enter_firstlevel", "net_gate_offline")'),
  stage_index     INT64     OPTIONS(description='0~5 로딩step / 6 loading_complete / 7 enter / -1 net_gate_offline'),
  elapsed_ms      INT64     OPTIONS(description='로딩 시작 후 경과 (ms)'),
  net_reachable   BOOL      OPTIONS(description='발화 시점 인터넷 도달 가능 여부')
)
PARTITION BY DATE(event_timestamp)
CLUSTER BY uid
OPTIONS(description='부팅→첫플레이 퍼널 체크포인트 (진단용). ROLLBACK_BOOT_CHECKPOINTS_20260713');
