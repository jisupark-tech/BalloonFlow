#if BF_RAIL_HOLDER
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// PROTO_RAIL_HOLDER_20260716 — "홀더가 레일을 탄다" 프로토타입 (Pixel Flow 형).
    ///
    /// ══ 구조 역전 ══
    ///   기존: 홀더(큐 격자)가 다트를 레일에 투입 → 다트가 레일을 돌며 발사(발사=레일에서 제거) → 레일 만석 = 패배.
    ///   프로토: 큐 상자 <b>자체가</b> 레일에 올라타 등간격으로 순회하며 직접 발사 → 다트는 레일에 안 올라감
    ///           → 발사할 때마다 그 상자의 탄창 숫자가 줄고(스케일 펀치+슬롯 연출), 0 이면 상자가 사라짐
    ///           → 총 탄약 소진 = 패배.
    ///
    /// ══ 캐리지 ══
    ///   레일에는 등간격 '캐리지' N개(기본 5, 간격 = 총길이/N)가 <b>쉼 없이</b> 돈다. 캐리지는 위치일 뿐 시각이 없다 —
    ///   상자가 올라타야 보인다. 등간격은 위상(laneOffset)으로 보장 → 벨트 회전 하나로 간격이 영구 보존.
    ///
    /// ══ 탑승 (레일 안 멈춤) ══
    ///   플레이어가 큐 앞줄 상자 탭 → 그 컬럼 '탑승 예약'.
    ///   빈 캐리지가 그 컬럼의 레일 접점(= 기존 배포점)을 지날 때, 큐 상자가 <b>날아가서</b> 그 캐리지에 붙는다.
    ///   비행 중에도 캐리지는 계속 도므로, 상자는 큐 위치 → 캐리지의 '현재' 위치로 매 프레임 보간된다(레일을 세우지 않음).
    ///
    /// ══ 발사 (촘촘히) ══
    ///   캐리지에 상자가 있으면 매 프레임 시도 — 사거리에 매칭 타겟이 있으면 railHolderFireCooldown(기본 0.05s)
    ///   간격으로 즉시 발사. 이전 0.15s 는 벨트가 여러 풍선을 지나치는 동안 스캔을 걸러 "4개 거르고 1발"이 됐다.
    ///
    /// ══ 재사용 ══
    ///   레일 기하/벨트, 카디널 발사방향, 타겟 선정/투사체/명중, 상자 시각/탄창 표시/다트 슬롯 연출, 큐 격자/기믹 — 그대로.
    /// ══ 우회 ══  슬롯 배열, packing physics, 클러스터/head, 배포점 예약, 데드락 모드 — 전부 안 씀.
    ///
    /// ⚠️ 파일 전체가 BF_RAIL_HOLDER define 안에만 존재. 출시 빌드에 define 이 켜져 있으면
    ///    RailHolderBuildGuard 가 빌드를 실패시킴 → 릴리즈 혼입 불가.
    /// </summary>
    public class RailHolderController : SceneSingleton<RailHolderController>
    {
        #region Types

        /// <summary>레일 위 등간격 자리. holderId < 0 = 빈 자리(시각 없음).</summary>
        private class Carriage
        {
            public int   index;
            public float laneOffset;    // 경로상 고정 위상 = index * (총길이 / N)
            public int   holderId = -1;
            public int   color = -1;
            public float nextFireAt;

            // 큐→레일 비행 상태. boarding == true 인 동안엔 발사 안 함(도착 후 시작).
            public bool    boarding;
            public float   boardStartTime;
            public float   boardDuration;
            public Vector3 boardFromPos;   // 큐에서 출발한 고정 좌표

            public bool IsEmpty => holderId < 0;
        }

        #endregion

        #region Fields

        private readonly List<Carriage> _carriages = new List<Carriage>();
        /// <summary>컬럼 → 레일 접점 progress. 레이아웃 확정 후 1회 캐시(고정 위치).</summary>
        private readonly Dictionary<int, float> _boardProgressByColumn = new Dictionary<int, float>();

        private float _travel;      // 벨트 누적 이동거리
        private float _pathLength;
        private bool  _initialized;
        private bool  _boardFinished;

        #endregion

        #region Properties

        /// <summary>레일 위 + 큐 총 탄약. 0 이고 풍선이 남으면 실패(= 기존 RailOverflow 대체).</summary>
        public int TotalRemainingAmmo =>
            HolderManager.HasInstance ? HolderManager.Instance.GetTotalRemainingAmmo() : 0;

        /// <summary>
        /// PROTO_RAIL_HOLDER_20260716: 이 모드가 지금 활성인가 — 마스터 토글 AND 레벨 범위(1~railHolderMaxLevel).
        /// 5곳(DartManager·BoardStateManager·HolderManager·LevelManager·여기)이 전부 이 하나를 참조 →
        /// 게이트가 어긋나지 않는다. 11레벨+ 는 자동으로 기존 다트 배포 메카닉으로 복귀.
        /// </summary>
        public static bool ModeActiveForCurrentLevel
        {
            get
            {
                if (!GameManager.HasInstance || !GameManager.Instance.Board.railHolderMode) return false;
                if (!LevelManager.HasInstance) return false;
                int lv = LevelManager.Instance.CurrentLevelId;
                return lv >= 1 && lv <= GameManager.Instance.Board.railHolderMaxLevel;
            }
        }

        #endregion

        #region Unity

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            base.OnDestroy();
        }

        private void Update()
        {
            if (_boardFinished) return;
            if (!ModeActive) return;
            if (!_initialized && !TryInitialize()) return;
            if (RailManager.Instance.IsPausedByBooster) return;

            AdvanceBelt();
            TickCarriages();
        }

        #endregion

        #region Setup

        private static bool ModeActive => ModeActiveForCurrentLevel;

        private static bool DebugLog =>
            GameManager.HasInstance && GameManager.Instance.Board.railHolderDebugLog;

        /// <summary>레일 레이아웃이 확정된 뒤(레벨 로드 후)에야 경로 길이를 알 수 있으므로 지연 초기화.</summary>
        private bool TryInitialize()
        {
            if (!RailManager.HasInstance || !GameManager.HasInstance) return false;

            _pathLength = RailManager.Instance.TotalPathLength;
            if (_pathLength <= 0.01f) return false;

            int count = Mathf.Clamp(GameManager.Instance.Board.railHolderCount, 1, 8);
            float spacing = _pathLength / count;

            _carriages.Clear();
            for (int i = 0; i < count; i++)
                _carriages.Add(new Carriage { index = i, laneOffset = i * spacing });

            CacheBoardingPoints();
            _initialized = true;

            if (DebugLog)
                Debug.Log($"[RailHolder] init carriages={count} pathLen={_pathLength:F2} spacing={spacing:F2} " +
                          $"boardingPoints={_boardProgressByColumn.Count}");
            return true;
        }

        /// <summary>컬럼별 레일 접점을 progress 로 캐시. 기존 배포점과 동일한 위치.</summary>
        private void CacheBoardingPoints()
        {
            _boardProgressByColumn.Clear();
            if (!HolderVisualManager.HasInstance || !HolderManager.HasInstance) return;

            int cols = HolderManager.Instance.QueueColumns;
            for (int c = 0; c < cols; c++)
            {
                if (!HolderVisualManager.Instance.TryGetColumnRailAttachWorldPos(c, out Vector3 world)) continue;
                _boardProgressByColumn[c] = RailManager.Instance.GetProgressAtWorldPos(world);
            }
        }

        #endregion

        #region Belt

        /// <summary>벨트는 절대 멈추지 않는다 — 탑승은 상자를 날려 붙이는 방식이라 레일 정차가 불필요.</summary>
        private void AdvanceBelt()
        {
            const float MAX_BELT_DELTA_TIME = 1f / 30f;   // RailManager.UpdateInternal 과 동일한 클램프
            float dt = Mathf.Min(Time.deltaTime, MAX_BELT_DELTA_TIME);
            // 홀더 순회 속도 = 초당 레일 바퀴 수(laps/sec) × 경로 총길이. 레일 기하(slotCount/slotSpacing)에서
            //   분리 — 기존엔 GetBeltDistancePerSecond(=rotationSpeed×slotSpacing)를 써서 순회 바퀴/초가
            //   rotationSpeed/slotCount 에 반비례했고, slotCount(용량)가 스테이지마다 커져 '초반 초고속→후반
            //   저속'으로 편차가 났다. laps/sec 는 보드 크기와 무관하게 모든 스테이지가 동일한 한 바퀴 시간을 준다.
            float lapsPerSec = Mathf.Max(0f, GameManager.Instance.Board.railHolderLapsPerSecond);
            _travel += _pathLength * lapsPerSec * dt;
            if (_travel >= _pathLength) _travel -= _pathLength;
        }

        private float ProgressOf(Carriage c)
        {
            float p = _travel + c.laneOffset;
            while (p >= _pathLength) p -= _pathLength;
            return p;
        }

        #endregion

        #region Tick

        private void TickCarriages()
        {
            RailManager rail = RailManager.Instance;

            for (int i = 0; i < _carriages.Count; i++)
            {
                Carriage c = _carriages[i];
                float progress = ProgressOf(c);

                if (c.IsEmpty) continue;   // 빈 캐리지는 탭 시 즉시 배정됨(HandleHolderTapped) — 여기선 대기 안 함

                rail.GetPoseAtDistance(progress, out Vector3 railPos, out _, out _);

                if (c.boarding)
                {
                    // 큐 출발점 → 캐리지의 '현재' 레일 위치로 보간. 레일은 계속 도므로 railPos 가 매 프레임 갱신됨
                    //   → 상자가 움직이는 자리를 쫓아가 붙는 자연스러운 비행.
                    float t = (Time.time - c.boardStartTime) / Mathf.Max(0.01f, c.boardDuration);
                    if (t >= 1f)
                    {
                        c.boarding = false;
                        SetHolderPos(c.holderId, railPos);
                        c.nextFireAt = Time.time;   // 도착 즉시 발사 가능
                    }
                    else
                    {
                        Vector3 flightPos = Vector3.Lerp(c.boardFromPos, railPos, EaseOutCubic(t));
                        SetHolderPos(c.holderId, flightPos);
                    }
                    continue;
                }

                SetHolderPos(c.holderId, railPos);
                TryFire(c, railPos);
            }
        }

        private void SetHolderPos(int holderId, Vector3 pos)
        {
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetRailMountedHolderPosition(holderId, pos);
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private void TryFire(Carriage c, Vector3 pos)
        {
            if (Time.time < c.nextFireAt) return;
            if (!DartManager.HasInstance || !HolderManager.HasInstance) return;

            // 사거리에 매칭 타겟이 있으면 즉시 발사(없으면 탄창 소모 없이 false). 쿨다운만 게이트.
            if (!DartManager.Instance.TryFireFromRailHolder(pos, c.color, c.holderId)) return;

            int remaining = HolderManager.Instance.ConsumeMagazine(c.holderId);
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.PlayRailHolderFireVisual(c.holderId, remaining, pos);

            c.nextFireAt = Time.time + GameManager.Instance.Board.railHolderFireCooldown;

            if (DebugLog)
                Debug.Log($"[RailHolder] fire carriage={c.index} holder={c.holderId} color={c.color} magLeft={remaining}");

            if (remaining <= 0)
                Unboard(c);
        }

        /// <summary>탄창 소진 — 상자를 치우고 캐리지를 비운다.</summary>
        private void Unboard(Carriage c)
        {
            int holderId = c.holderId;

            if (HolderManager.HasInstance) HolderManager.Instance.MarkRailHolderConsumed(holderId);
            if (HolderVisualManager.HasInstance) HolderVisualManager.Instance.DespawnRailMountedHolder(holderId);

            c.holderId = -1;
            c.color = -1;
            c.nextFireAt = 0f;
            c.boarding = false;

            if (DebugLog) Debug.Log($"[RailHolder] carriage={c.index} holder={holderId} 소진 → 하차");
        }

        /// <summary>
        /// PROTO_RAIL_HOLDER_20260716: 즉시 탑승 — 탭한 순간 바로 판정한다(대기 없음).
        ///   ① 빈 캐리지 없음 → false (호출측이 튕김 연출)
        ///   ② 앞줄 상자 태울 수 없음(기믹 등) → false (호출측이 튕김 연출)
        ///   ③ 가능 → 가장 가까운 빈 캐리지에 배정, 상자 즉시 비행 시작 → true
        /// forceHolderId >= 0 이면 그 홀더를 앞줄 대신 강제로 태운다(Hand 부스터용).
        /// </summary>
        private bool TryBoardImmediately(int column, int forceHolderId = -1)
        {
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return false;

            Carriage target = FindNearestEmptyCarriage(column);
            if (target == null) return false;   // 빈 캐리지 없음 → 튕김

            int holderId, color, magazine;
            bool taken;
            if (forceHolderId >= 0)
            {
                holderId = forceHolderId;
                taken = HolderManager.Instance.TryMountHolderOnRailById(forceHolderId, out color, out magazine);
            }
            else
            {
                taken = HolderManager.Instance.TryMountFrontHolderOnRail(column, out holderId, out color, out magazine);
            }
            if (!taken) return false;   // 앞줄이 기믹/빈 상자 등 → 튕김

            if (!HolderVisualManager.Instance.TryMountHolderVisualOnRail(holderId, out Vector3 fromPos))
            {
                // 시각이 없으면(지연 스폰 등) 데이터만 올라가면 유령 상자 → 되돌린다.
                HolderManager.Instance.MarkRailHolderConsumed(holderId);
                return false;
            }

            target.holderId = holderId;
            target.color = color;
            target.boarding = true;
            target.boardStartTime = Time.time;
            target.boardDuration = GameManager.Instance.Board.railHolderBoardFlightTime;
            target.boardFromPos = fromPos;

            if (DebugLog)
                Debug.Log($"[RailHolder] board carriage={target.index} ← col{column} holder={holderId} " +
                          $"color={color} mag={magazine} flightFrom={fromPos}");
            return true;
        }

        /// <summary>
        /// 그 컬럼 접점에서 진행 방향으로 가장 곧 도달할 빈 캐리지 — 상자가 레일을 가로지르지 않고 가깝게 붙는다.
        /// 접점 progress 를 못 구하면(접점 미캐시) 그냥 첫 빈 캐리지.
        /// </summary>
        private Carriage FindNearestEmptyCarriage(int column)
        {
            bool hasStation = _boardProgressByColumn.TryGetValue(column, out float stationProgress);
            Carriage best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _carriages.Count; i++)
            {
                Carriage c = _carriages[i];
                if (!c.IsEmpty) continue;
                if (!hasStation) return c;   // 접점 정보 없으면 첫 빈 캐리지

                // 진행 방향(_travel 증가) 기준 캐리지가 접점까지 가는 forward 거리.
                float fwd = stationProgress - ProgressOf(c);
                while (fwd < 0f) fwd += _pathLength;
                if (fwd < bestDist) { bestDist = fwd; best = c; }
            }
            return best;
        }

        #endregion

        #region Events

        /// <summary>
        /// 큐 앞줄 상자 탭 = 즉시 탑승 시도. 성공하면 상자가 가장 가까운 빈 캐리지로 날아가고,
        /// 못 태우면(빈 캐리지 없음/기믹) 기존 배치실패 연출(OnHolderClickAnim, 제자리 튕김)을 발행한다.
        /// 기존 배포(SelectHolder→DeployCoroutine)는 HolderManager 쪽에서 이 모드일 때 차단된다.
        /// </summary>
        private void HandleHolderTapped(OnHolderTapped evt)
        {
            if (!ModeActive) return;
            if (!HolderManager.HasInstance) return;

            HolderData holder = HolderManager.Instance.FindHolderPublic(evt.holderId);
            if (holder == null || holder.isRailMounted || holder.isConsumed) return;

            if (!TryBoardImmediately(holder.column))
                Bounce(evt.holderId);   // 못 태움 → 즉각 튕김
        }

        /// <summary>
        /// PROTO_RAIL_HOLDER_20260716: Hand/Select Tool 부스터 진입점 — 앞줄 무시, 지정 홀더를 즉시 태운다.
        /// 성공 여부를 그대로 반환(BoosterExecutor 가 성공=효과발행/실패=환불 처리).
        /// </summary>
        public bool TryMountHolderByBoosterSelect(int holderId)
        {
            if (!ModeActive || !HolderManager.HasInstance) return false;

            HolderData holder = HolderManager.Instance.FindHolderPublic(holderId);
            if (holder == null || holder.isRailMounted || holder.isConsumed) return false;

            bool ok = TryBoardImmediately(holder.column, forceHolderId: holderId);
            if (!ok) Bounce(holderId);
            return ok;
        }

        /// <summary>기존 코어의 '배치 못할 때' 연출(제자리 튕김)을 그 홀더에 재생.</summary>
        private void Bounce(int holderId)
        {
            EventBus.Publish(new OnHolderClickAnim { holderId = holderId });
            if (DebugLog) Debug.Log($"[RailHolder] holder={holderId} 탑승 불가 → 튕김");
        }

        private void HandleBoardCleared(OnBoardCleared _) => _boardFinished = true;
        private void HandleBoardFailed(OnBoardFailed _) => _boardFinished = true;

        #endregion

        #region Reset

        public void ResetAll()
        {
            _carriages.Clear();
            _boardProgressByColumn.Clear();
            _travel = 0f;
            _initialized = false;
            _boardFinished = false;
        }

        #endregion
    }
}
#endif
