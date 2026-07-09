-- ROLLBACK_USER_PROPERTY_PIPELINE_20260708
-- R_user_property (puzzle_game_data_schema_v3_2) — uid 당 1행, UPSERT 전용 테이블.
-- [2026-07-09 정정] 테이블은 6/23 초기 셋업 때 이미 생성돼 있음(0행) — 시각 컬럼이 xlsx(DATETIME)와 달리
-- TIMESTAMP. 이 DDL 은 문서/재생성용이며 실테이블 기준(TIMESTAMP)으로 정정됨. CREATE IF NOT EXISTS 라 재실행 무해.
-- 적재 경로: 클라 user_property_event(세션 시작/종료) → ingestAnalyticsEvents → DML MERGE.
--   · 스트리밍 insert 를 절대 쓰지 않는다 (스트리밍 버퍼가 생기면 DML MERGE 가 충돌함).
--   · uid 는 BQ 파티션 키로 쓸 수 없어(문자열) DATE(install_at) 파티션 + uid 클러스터로 대체.
--   · MMP 계열(install_media_source/campaign/adgroup/creative)·idfa/aid 는 클라 미전송 → NULL
--     (AppsFlyer S2S/수동 연동 시 별도 백필).
-- 1회 실행:
--   bq query --use_legacy_sql=false < firebase/bigquery/user_property_ddl.sql
CREATE TABLE IF NOT EXISTS `balloonloop_db.user_property` (
  game_id               STRING NOT NULL OPTIONS(description='게임 식별자'),
  uid                   STRING NOT NULL OPTIONS(description='유저 ID (소문자). 90일 re-attribution window'),
  install_at            TIMESTAMP OPTIONS(description='설치 시각 (UTC). 90일 정책 기준값'),
  install_version       STRING   OPTIONS(description='설치 버전 (불변)'),
  install_country       STRING   OPTIONS(description='설치 국가 (불변)'),
  install_platform      STRING   OPTIONS(description='설치 플랫폼 (불변)'),
  install_device        STRING   OPTIONS(description='설치 디바이스 (불변)'),
  install_media_source  STRING   OPTIONS(description='설치 유입 채널 (UA). MMP 연동 자동, 미연동 NULL'),
  last_active_at        TIMESTAMP OPTIONS(description='마지막 앱 실행 시각 (UTC)'),
  last_active_version   STRING   OPTIONS(description='마지막 활성 앱 버전'),
  last_active_country   STRING   OPTIONS(description='마지막 활성 국가'),
  last_played_at        TIMESTAMP OPTIONS(description='마지막 레벨 플레이 시각 (UTC). churn 판정 기준'),
  max_reached_level     INT64    OPTIONS(description='현재 도전 가능 최고 레벨'),
  total_play_count      INT64    OPTIONS(description='누적 판 수 (is_replay_after_clear=FALSE)'),
  total_clear_count     INT64    OPTIONS(description='누적 첫클리어 건수 (= max_reached_level - 1)'),
  total_coin_balance    INT64    OPTIONS(description='현재 코인 잔량 스냅샷'),
  total_spend_usd       NUMERIC  OPTIONS(description='누적 IAP 매출 (verified only)'),
  total_ad_revenue_usd  NUMERIC  OPTIONS(description='누적 광고 매출. LTV = spend + ad_revenue'),
  infinite_lives_expiry TIMESTAMP OPTIONS(description='무한 하트 만료 시각 (UTC). NULL=비활성'),
  is_payer              BOOL     OPTIONS(description='결제자 라벨 — verified 1건 이상, 영구 TRUE'),
  last_updated_at       TIMESTAMP OPTIONS(description='마지막 갱신 (UTC). 세션 시작/종료 UPSERT'),
  install_campaign      STRING   OPTIONS(description='설치 유입 캠페인 (불변). MMP 자동'),
  install_adgroup       STRING   OPTIONS(description='설치 유입 광고그룹 (불변). MMP 자동'),
  install_creative      STRING   OPTIONS(description='설치 유입 크리에이티브 (불변). MMP 자동'),
  idfa                  STRING   OPTIONS(description='iOS IDFA. ATT 동의 시. Android NULL'),
  aid                   STRING   OPTIONS(description='Android GAID. iOS NULL'),
  appsflyer_id          STRING   OPTIONS(description='AppsFlyer raw data 조인 키')
)
PARTITION BY DATE(install_at)
CLUSTER BY uid
OPTIONS(description='유저 프로필 (uid 당 1행, MERGE UPSERT — 스트리밍 금지). v3.2 R_user_property');
