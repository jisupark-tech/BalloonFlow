-- ROLLBACK_ANALYTICS_EDITOR_INGEST_BLOCK_20260709 세트 — 세션 start/end 이격 보정 지표 쿼리.
-- 배경 (2026-07-09 실측): raw start 523 vs end 305 이격의 분해 결과
--   · 77% = 유저별 '마지막 세션' (클라 소급 구조상 다음 접속 전까지 end 없음 — 오류 아님, 시간차)
--   · 20% = editor 세션 (pause 콜백 부재 — 개발 노이즈. 클라에서 적재 차단됨, 과거 데이터만 잔존)
--   · android 진짜 누수 1.96% (재설치/데이터클리어 추정)
-- 사용법: 세션 지표는 반드시 아래 보정 기준으로 조회할 것.

-- 1) 플랫폼별 실질 누수율 (editor 제외 + 유저별 마지막 세션 허용)
WITH s AS (
  SELECT session_id, uid, platform, event_timestamp,
         ROW_NUMBER() OVER (PARTITION BY uid ORDER BY event_timestamp DESC) rn
  FROM `balloonloop_db.session_start`
  WHERE platform != 'editor'
), j AS (
  SELECT s.*, e.session_id IS NOT NULL AS has_end
  FROM s LEFT JOIN `balloonloop_db.session_end` e USING (session_id)
)
SELECT platform,
       COUNT(*)                                        AS starts,
       COUNTIF(NOT has_end AND rn = 1)                 AS open_last_sessions,  -- 정상 미종결(접속중/이탈 마지막)
       COUNTIF(NOT has_end AND rn > 1)                 AS leaked_real,         -- 진짜 누수
       ROUND(COUNTIF(NOT has_end AND rn > 1) / COUNT(*) * 100, 2) AS leaked_pct
FROM j
GROUP BY platform;

-- 2) 역방향 점검 (end 만 존재 = 포그라운드 크래시 start 유실 — 클라 START_PERSIST 보정 후 감소해야 정상)
SELECT COUNT(*) AS end_without_start
FROM `balloonloop_db.session_end` e
LEFT JOIN `balloonloop_db.session_start` s USING (session_id)
WHERE s.session_id IS NULL;

-- 3) 세션 수 집계 표준식 (지표용): editor 제외 + session_id 기준 distinct
SELECT DATE(event_timestamp) AS d,
       COUNT(DISTINCT session_id) AS sessions
FROM `balloonloop_db.session_start`
WHERE platform != 'editor'
GROUP BY d ORDER BY d DESC;
