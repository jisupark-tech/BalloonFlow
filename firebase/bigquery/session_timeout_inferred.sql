-- ROLLBACK_SESSION_TIMEOUT_INFERRED_20260713
-- 목적: session_start 는 있으나 session_end 가 없는 '미마감 세션'(주로 never-return 유저의 마지막 세션,
--   프로세스 킬로 quit 미발생 + 다음 부팅이 없어 orphan 소급도 불가)을 일정 시간(GRACE) 경과 후 서버가
--   timeout_inferred 로 마감 → start/end 카운트 이격 해소. 클라 END_TIMEOUT_INFERRED 상수의 서버측 구현.
--
-- 실행: BigQuery 예약 쿼리(Scheduled Query)로 1시간마다 실행 권장.
--   콘솔: BigQuery > 쿼리 편집기 > 이 SQL 붙여넣기 > '예약' > 반복 1시간 > 대상 없음(INSERT 문 자체 기록).
--   또는 CLI: bq query --use_legacy_sql=false --schedule='every 1 hours' \
--             --display_name='session_timeout_inferred' < firebase/bigquery/session_timeout_inferred.sql
--   수동 1회: bq query --use_legacy_sql=false < firebase/bigquery/session_timeout_inferred.sql
--
-- 안전성:
--   · 멱등 — NOT EXISTS(session_end) 가드로 이미 마감된 세션은 재삽입 안 함. event_id 도 결정적(inferred_<sid>).
--   · INSERT(append) 전용 — session_end 스트리밍 버퍼와 병행 안전(DELETE/UPDATE 아님).
--   · GRACE_HOURS 만큼 지난 세션만 대상 → 정상 지연(orphan 소급/늦은 flush) 도착분을 timeout 으로 오분류하지 않음.
--   · end_reason='timeout_inferred' 로 태깅 → 지표에서 실제 quit 과 구분 가능(세션 길이는 추정치임을 명시).

DECLARE grace_hours   INT64 DEFAULT 6;    -- 세션 시작 후 이 시간 지나도 end 없으면 timeout 처리
DECLARE lookback_days INT64 DEFAULT 30;   -- 과거 이 기간까지만 스캔(비용/스캔량 상한)

INSERT INTO `balloonloop_db.session_end`
  (event_id, session_id, game_id, uid, event_timestamp, end_reason, duration_sec)
SELECT
  CONCAT('inferred_', s.session_id)                                AS event_id,
  s.session_id,
  s.game_id,
  s.uid,
  s.last_activity                                                  AS event_timestamp,
  'timeout_inferred'                                               AS end_reason,
  GREATEST(0, TIMESTAMP_DIFF(s.last_activity, s.start_ts, SECOND)) AS duration_sec
FROM (
  SELECT
    ss.session_id,
    ANY_VALUE(ss.game_id)   AS game_id,
    ANY_VALUE(ss.uid)       AS uid,
    MIN(ss.event_timestamp) AS start_ts,
    -- 마지막 활동 시각 = 세션 시작과 그 세션의 play/ad 이벤트 최대 시각 중 큰 값(세션 길이 추정 근거)
    GREATEST(MIN(ss.event_timestamp), IFNULL(MAX(act.act_ts), MIN(ss.event_timestamp))) AS last_activity
  FROM `balloonloop_db.session_start` ss
  LEFT JOIN (
    -- 각 UNION 브랜치에 파티션 날짜필터 — 없으면 매 실행마다 대용량 테이블 풀스캔(비용 폭주).
    SELECT session_id, event_timestamp AS act_ts FROM `balloonloop_db.play_event`  WHERE event_timestamp >= TIMESTAMP_SUB(CURRENT_TIMESTAMP(), INTERVAL lookback_days DAY)
    UNION ALL
    SELECT session_id, event_timestamp AS act_ts FROM `balloonloop_db.play_start` WHERE event_timestamp >= TIMESTAMP_SUB(CURRENT_TIMESTAMP(), INTERVAL lookback_days DAY)
    UNION ALL
    SELECT session_id, event_timestamp AS act_ts FROM `balloonloop_db.ad_event`   WHERE event_timestamp >= TIMESTAMP_SUB(CURRENT_TIMESTAMP(), INTERVAL lookback_days DAY)
  ) act USING (session_id)
  WHERE ss.event_timestamp >= TIMESTAMP_SUB(CURRENT_TIMESTAMP(), INTERVAL lookback_days DAY)
    AND ss.session_id IS NOT NULL AND ss.session_id != ''
  GROUP BY ss.session_id
) s
-- start_ts 대신 last_activity 기준 — 오래 전 시작됐어도 최근 활동 있으면(살아있는 세션) 조기 마감 안 함
--   → 뒤늦은 진짜 session_end 와의 이중 마감 방지. 무활동 GRACE 지난 세션만 마감.
WHERE s.last_activity < TIMESTAMP_SUB(CURRENT_TIMESTAMP(), INTERVAL grace_hours HOUR)
  AND NOT EXISTS (
    SELECT 1 FROM `balloonloop_db.session_end` e WHERE e.session_id = s.session_id
  );
