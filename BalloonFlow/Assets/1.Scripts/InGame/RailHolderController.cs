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

            // Step1 착지열: 탑승(비행 완료) 이후 누적 순회 거리. >= _pathLength 면 한 바퀴 완주 → 착지 판정.
            //   boarding 완료 시 0 으로 리셋, 매 프레임 벨트 이동분 누적.
            public float distanceSinceBoard;

            // 큐→레일 비행 상태. boarding == true 인 동안엔 발사 안 함(도착 후 시작).
            public bool    boarding;
            public float   boardStartTime;
            public float   boardDuration;
            public Vector3 boardFromPos;   // 큐에서 출발한 고정 좌표

            public bool IsEmpty => holderId < 0;
        }

        /// <summary>[정거장] 레일 왼쪽아래 모서리 정거장에서 탑승을 기다리는 상자.
        /// 탭 → 정거장으로 비행(fromPos→대기슬롯, 아치) → 도착 후 줄 서서 대기 → 빈 캐리지(규칙 간격의
        /// 빈 자리)가 정거장을 지날 때 FIFO 로 올라탄다(짧은 hop 은 기존 boarding 추적비행 재사용).</summary>
        private class StationWaiter
        {
            public int     holderId;
            public int     color;
            public bool    arrived;      // 정거장 대기 슬롯 도착 완료
            public float   flightT0;
            public float   flightDur;
            public Vector3 fromPos;      // 출발점(큐/착지열)
            public Vector3 pos;          // 현재 위치(대기열 재정렬 MoveTowards 용)
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

        // Step1 착지열: 큐 앞 가로 착지열. 값 = 그 슬롯에 안착한 holderId(-1 = 빈 칸). 크기 = railHolderLandingSlots.
        private int[] _landingSlots;
        // Step1 착지열: 레일 홀더가 한 바퀴 완주했는데 빈 착지 슬롯이 없어 복귀 불가 → 실패 대기(BoardStateManager 가 소비).
        private bool  _landingOverflowFail;
        // Step1 착지열: 직전 프레임 벨트 이동분(캐리지별 순회 거리 누적용). AdvanceBelt 에서 갱신.
        private float _lastBeltDelta;

        // [정거장] 탑승 대기열(FIFO). 탭/재탭한 상자는 캐리지 직행이 아니라 여기로 날아와 줄 선다.
        private readonly List<StationWaiter> _stationWaiters = new List<StationWaiter>();
        private const float STATION_WAIT_OUT = 0.9f;       // 정거장 대기 위치 — 레일 바깥(아래 -Z)으로 물러난 거리
        private const float STATION_STACK_HEIGHT = 0.85f;  // 대기 탑 층 높이(월드 Y) — 상자 높이에 맞춰 튜닝
        private const float STATION_SHIFT_SPEED = 8f;      // 앞(맨 아래)이 빠졌을 때 탑이 내려앉는 속도(월드/초)
        // [최적화] ValidateLandingSlots 스로틀 — 다음 실행 허용 시각(unscaled).
        private float _nextLandingValidateAt;

        // [Pixel Flow 정합] 레일 동시 탑승 상한(railHolderCount) + 라이더 일련번호(로그용).
        private int _maxRiders = 5;
        private int _riderSerial;

        #endregion

        #region Properties

        /// <summary>레일 위 + 큐 총 탄약. 0 이고 풍선이 남으면 실패(= 기존 RailOverflow 대체).</summary>
        public int TotalRemainingAmmo =>
            HolderManager.HasInstance ? HolderManager.Instance.GetTotalRemainingAmmo() : 0;

        /// <summary>Step1 착지열: 레일 홀더가 한 바퀴 완주했는데 착지열이 만석이라 복귀 불가 → 실패 대기 플래그.
        /// BoardStateManager.EvaluateRailHolderAmmoFail 이 읽어 grace 후 TriggerFail. ResetAll 에서 해제.</summary>
        public bool LandingOverflowFailPending => _landingOverflowFail;

        /// <summary>[최적화/커버리지] 직전 프레임 벨트 이동거리(월드) — DartManager 가 FPS 무관 동적 탐색 밴드
        /// 산정에 사용(저FPS·가속 시 프레임당 여러 라인을 지나쳐도 밴드가 따라 넓어져 놓침 방지).</summary>
        public float LastBeltDelta => _lastBeltDelta;

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
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);   // Step5: 이어하기 릴리프
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
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

            // [Pixel Flow 정합 2026-07-21] 고정 등간격 캐리지 사전 배치 폐기 — 원작은 '최소 간격만 유지한 채
            //   최대 N개까지' 정거장에서 즉시 탑승하는 모델(등간격 빈 자리를 기다리는 딜레이가 없음).
            //   라이더는 탑승 시점에 동적 생성(AcquireRiderAtStation), 전원 같은 벨트 속도라 간격은 자동 보존.
            _maxRiders = Mathf.Clamp(GameManager.Instance.Board.railHolderCount, 1, 8);
            _carriages.Clear();

            // Step1 착지열: 큐 앞 가로 착지열 슬롯 초기화(전부 빈 칸).
            int landing = Mathf.Clamp(GameManager.Instance.Board.railHolderLandingSlots, 1, 8);
            _landingSlots = new int[landing];
            for (int i = 0; i < landing; i++) _landingSlots[i] = -1;
            _landingOverflowFail = false;

            // [착지열 마커] 생성은 HVM.SpawnWaitingHolders(레이아웃 확정 직후)가 단일 소스 —
            //   여기(첫 Update)서 만들면 로드 코루틴의 ClearAllVisuals 가 직후에 지워버리는 레이스가 있었다.

            CacheBoardingPoints();

            // [최적화] 연속 사격의 팝 폭주가 '팝 1개=컨투어 전체 재빌드'를 매 프레임 돌리는 것 방지 —
            //   홀더 모드 동안 재빌드 최소 간격 0.1s(사이 stale 은 죽은 풍선 O(1) 스킵으로 안전).
            DirectionalTargeting.MinRebuildInterval = 0.1f;

            _initialized = true;

            if (DebugLog)
                Debug.Log($"[RailHolder] init maxRiders={_maxRiders} pathLen={_pathLength:F2} " +
                          $"minGap={GameManager.Instance.Board.railHolderMinGap:F2} boardingPoints={_boardProgressByColumn.Count}");
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
            // [Almost There] 클리어 확정 국면 가속 — 다트 모드의 레일 ×1.8(UI 표기 2배속)과 동일 배율.
            //   RailManager 의 _almostThere 는 레일 다트 기준이라 홀더 모드(다트 0)에선 안 서므로 자체 판정 사용.
            if (IsAlmostThereFreeRide()) lapsPerSec *= ALMOST_THERE_SPEED_MULT;
            float delta = _pathLength * lapsPerSec * dt;
            _lastBeltDelta = delta;                 // Step1 착지열: 캐리지별 순회 거리 누적에 사용
            _travel += delta;
            if (_travel >= _pathLength) _travel -= _pathLength;
        }

        private float ProgressOf(Carriage c)
        {
            float p = _travel + c.laneOffset;
            while (p >= _pathLength) p -= _pathLength;
            return p;
        }

        // [Almost There 프리라이드 2026-07-21] "조금만 더" 국면(게임이 어떻게 해도 클리어되는 상황) 판정 —
        //   다트 모드 트리거(총 잔여 다트 < 레일 용량, RailManager.IsAlmostThereImminent)의 홀더 모드 미러:
        //   총 잔여 탄약 < 레일 물리 용량. 이 국면엔 ① 순회 ×1.8 가속(AdvanceBelt) ② 랩 완주해도 착지하지
        //   않고 계속 돌며 소진까지 발사(TickCarriages) — 착지→재탭 사이클이 확정 클리어를 지연시키지 않게.
        private const float ALMOST_THERE_SPEED_MULT = 1.8f;
        // [최적화] TotalRemainingAmmo 가 홀더 리스트 선형 합산이라 프레임당 다중 호출(AdvanceBelt+캐리지별)을
        //   프레임 캐시로 1회 계산으로 축소.
        private int _freeRideCacheFrame = -1;
        private bool _freeRideCached;
        private bool IsAlmostThereFreeRide()
        {
            if (Time.frameCount == _freeRideCacheFrame) return _freeRideCached;
            _freeRideCacheFrame = Time.frameCount;

            bool result = false;
            if (RailManager.HasInstance)
            {
                int cap = RailManager.Instance.PhysicalCapacity;
                int ammo = TotalRemainingAmmo;
                result = cap > 0 && ammo > 0 && ammo < cap;
            }
            _freeRideCached = result;
            return result;
        }

        #endregion

        #region Tick

        private void TickCarriages()
        {
            RailManager rail = RailManager.Instance;

            // [QA] 만석 실패 플래그는 매 틱 재평가(자가치유) — 막힌 캐리지가 아래 HandleLapComplete 에서
            //   매 프레임 다시 세운다. 유저가 착지 홀더를 재탭해 슬롯을 비우면 다음 틱에 착지 성공 →
            //   플래그가 저절로 꺼져 실패 카운트다운이 해제된다(기존: 한 번 서면 영구 잔존 → 회피 불가 버그).
            _landingOverflowFail = false;
            // [QA] Zap 등 부스터가 착지 홀더를 소진시키면 슬롯이 유령 점유로 남는다 — 스테일 정리.
            // [최적화] 슬롯×FindHolder(선형)라 매 프레임은 낭비 — 0.25s 스로틀(부스터 사건 대비 충분히 촘촘).
            if (Time.unscaledTime >= _nextLandingValidateAt)
            {
                _nextLandingValidateAt = Time.unscaledTime + 0.25f;
                ValidateLandingSlots();
            }

            float turnSpeed = GameManager.Instance.Board.railHolderTurnSpeedDeg;
            float arcHeight = GameManager.Instance.Board.railHolderBoardArcHeight;

            // [정거장] 대기열 처리 — 정거장 비행/줄서기 + 빈 캐리지 도착 시 FIFO 탑승.
            TickStationWaiters(arcHeight);

            for (int i = 0; i < _carriages.Count; i++)
            {
                Carriage c = _carriages[i];
                float progress = ProgressOf(c);

                if (c.IsEmpty) continue;   // 빈 캐리지는 탭 시 즉시 배정됨(HandleHolderTapped) — 여기선 대기 안 함

                rail.GetPoseAtDistance(progress, out Vector3 railPos, out _, out Vector3 fireDir);
                // [3D 레일 2026-07-22] 레일 모델 두께만큼 탑승 높이 가산 — 홀더가 레일 윗면에 얹혀 보이게.
                //   (0이면 경로 y 그대로 = 모델과 겹침. 탑승 비행 목표·포즈·랩 처리 전부 이 값 기준.)
                railPos.y += GameManager.Instance.Board.railHolderRideHeight;

                if (c.boarding)
                {
                    // 큐 출발점 → 캐리지의 '현재' 레일 위치로 보간. 레일은 계속 도므로 railPos 가 매 프레임 갱신됨
                    //   → 상자가 움직이는 자리를 쫓아가 붙는 자연스러운 비행.
                    float t = (Time.time - c.boardStartTime) / Mathf.Max(0.01f, c.boardDuration);
                    if (t >= 1f)
                    {
                        c.boarding = false;
                        c.distanceSinceBoard = 0f;  // Step1 착지열: 도착 시점부터 한 바퀴 카운트 시작
                        SetHolderPose(c.holderId, railPos, fireDir, turnSpeed);
                        c.nextFireAt = Time.time;   // 도착 즉시 발사 가능
                    }
                    else
                    {
                        // [연출] 직선 추적 비행이 밋밋/어색 → 포물선 아치(4t(1-t))를 얹어 '떠서 날아가 앉는' 궤적.
                        //   경로 보간은 기존 EaseOutCubic 유지(움직이는 자리를 쫓는 성질 보존), 아치는 순수 Y 오프셋.
                        float eased = EaseOutCubic(t);
                        Vector3 flightPos = Vector3.Lerp(c.boardFromPos, railPos, eased);
                        flightPos.y += arcHeight * 4f * t * (1f - t);
                        SetHolderPos(c.holderId, flightPos);
                    }
                    continue;
                }

                // Step1 착지열: 한 바퀴 완주 감지 → 착지(탄창 남음) 또는 만석 실패. 완주 처리되면 이 프레임 발사 스킵.
                c.distanceSinceBoard += _lastBeltDelta;
                if (c.distanceSinceBoard >= _pathLength)
                {
                    // [Almost There 프리라이드] 클리어 확정 국면 — 착지 생략, 랩 카운터만 리셋하고 계속 순회·발사.
                    if (IsAlmostThereFreeRide())
                    {
                        c.distanceSinceBoard -= _pathLength;
                    }
                    else
                    {
                        HandleLapComplete(c, railPos, fireDir, turnSpeed);
                        continue;
                    }
                }

                SetHolderPose(c.holderId, railPos, fireDir, turnSpeed);
                TryFire(c, railPos, progress);
            }

            // Step4: 이 프레임의 착지/재탑승/스테일 정리 결과를 게이지 파이프라인에 반영(변화 시에만 발행).
            PublishLandingOccupancy();
        }

        /// <summary>[QA] 착지 슬롯 스테일 정리 — 슬롯이 가리키는 홀더가 소진/제거됐거나(부스터 Zap 등)
        /// 더 이상 착지 상태가 아니면 빈 칸으로 되돌린다. 유령 점유로 인한 영구 만석(오실패) 방지.</summary>
        private void ValidateLandingSlots()
        {
            if (_landingSlots == null || !HolderManager.HasInstance) return;
            for (int i = 0; i < _landingSlots.Length; i++)
            {
                int id = _landingSlots[i];
                if (id < 0) continue;
                HolderData h = HolderManager.Instance.FindHolderPublic(id);
                if (h == null || h.isConsumed || !h.isOnLandingRow)
                    _landingSlots[i] = -1;
            }
        }

        private void SetHolderPos(int holderId, Vector3 pos)
        {
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetRailMountedHolderPosition(holderId, pos);
        }

        /// <summary>[연출] 위치 + 모델 회전(발사 방향으로 부드럽게, 코너 스냅 없음) + 텍스트 월드 고정.</summary>
        private void SetHolderPose(int holderId, Vector3 pos, Vector3 fireDir, float turnSpeedDeg)
        {
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetRailMountedHolderPose(holderId, pos, fireDir, turnSpeedDeg);
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private void TryFire(Carriage c, Vector3 pos, float progress)
        {
            if (Time.time < c.nextFireAt) return;
            if (!DartManager.HasInstance || !HolderManager.HasInstance) return;

            // [QA/체인 재결속 2026-07-21] 라이더 상태 가드:
            //   ① 외부 제거(Zap: isConsumed) → 즉시 하차(해동 크레딧 미발행 — 부스터가 이미 줌).
            //   ② 탄창 0 → HandleEmptyRider: 체인 파트너가 살아 있으면 '소진 대기'로 레일에 남아 체인 유지
            //      (발사·착지 없음), 그룹 전원 소진 시 전원 함께 하차.
            HolderData riderData = HolderManager.Instance.FindHolderPublic(c.holderId);
            if (riderData == null || riderData.isConsumed)
            {
                Unboard(c, publishDeployDone: false);
                return;
            }
            if (riderData.magazineCount <= 0)
            {
                HandleEmptyRider(c, riderData);
                return;
            }

            // [놓침 fix 2026-07-21] 프레임당 다발 사격 — 기존 1발/프레임 제한은 홀더가 빠를 때(가속·저FPS)
            //   한 프레임에 라인 여러 개를 지나쳐 그 라인들의 풍선을 영영 안 쏘는 놓침을 만들었다
            //   (60fps 기준도 프레임당 0.245 이동 ≈ 라인 간격 0.275 — 항상 경계). 매 호출 타겟팅을
            //   다시 돌려 탐색 밴드(±2라인) 안의 다른 타겟에 연속 발사 — 지나친 라인을 같은 프레임에 커버.
            //   예약(RegisterInflightDart)이 중복 타겟을 막으므로 과잉 사격은 없다.
            // [놓침 fix/FPS 무관] 프레임당 발사 상한을 이동량에 비례 확장 — 저FPS(프로파일러 Deep Profile 등)나
            //   가속으로 한 프레임에 라인 3개+를 지나쳐도 그만큼 더 쏴서 커버(밴드도 DartManager 가 동적 확장).
            float cellSp = Mathf.Max(0.01f, GameManager.Instance.Board.cellSpacing);
            int maxShots = Mathf.Clamp(2 + Mathf.CeilToInt(_lastBeltDelta / cellSp), MAX_SHOTS_PER_FRAME, 8);

            int shots = 0;
            while (shots < maxShots)
            {
                // [최적화] progress 직접 전달 — DartManager 내부의 월드→progress 역변환(240샘플 브루트포스) 제거.
                if (!DartManager.Instance.TryFireFromRailHolder(pos, c.color, c.holderId, progress)) break;

                int remaining = HolderManager.Instance.ConsumeMagazine(c.holderId);
                if (HolderVisualManager.HasInstance)
                    HolderVisualManager.Instance.PlayRailHolderFireVisual(c.holderId, remaining, pos);
                shots++;

                if (DebugLog)
                    Debug.Log($"[RailHolder] fire carriage={c.index} holder={c.holderId} color={c.color} magLeft={remaining} (shot {shots}/{maxShots})");

                if (remaining <= 0)
                {
                    // [체인 재결속] 정상 소진 — 비체인은 즉시 하차(deploydone), 체인은 파트너 생존 시 소진 대기.
                    HandleEmptyRider(c, riderData);
                    return;
                }
            }

            if (shots > 0)
                c.nextFireAt = Time.time + GameManager.Instance.Board.railHolderFireCooldown;
        }

        /// <summary>
        /// [체인 재결속 2026-07-21] 탄창 0 라이더 처리(사용자 사양: 체인은 둘 다 소진해야 사라짐) —
        ///   비체인: 즉시 하차(deploydone 발행 → Frozen 해동/Pipe 방출).
        ///   체인 + 파트너 잔탄 있음: '소진 대기' — 레일에 남아 계속 순회(발사·착지 없음), 체인 시각 유지.
        ///   체인 + 그룹 전원 소진: 레일 위 그룹 멤버 전원 함께 하차(멤버당 deploydone 1회 = 다트 모드 크레딧 페이스).
        /// </summary>
        private void HandleEmptyRider(Carriage c, HolderData rider)
        {
            if (rider.chainGroupId >= 0 && HolderManager.HasInstance
                && HolderManager.Instance.ChainGroupHasRemainingAmmo(rider.chainGroupId))
                return;   // 소진 대기 — 파트너가 다 쓸 때까지 체인을 유지한 채 순회

            if (rider.chainGroupId >= 0) UnboardChainGroup(rider.chainGroupId);
            else Unboard(c, publishDeployDone: true);
        }

        /// <summary>[체인 재결속] 그룹 전원 소진 → 레일 위 그룹 라이더 전원 동시 하차.</summary>
        private void UnboardChainGroup(int chainGroupId)
        {
            for (int i = 0; i < _carriages.Count; i++)
            {
                Carriage rc = _carriages[i];
                if (rc.IsEmpty) continue;
                HolderData h = HolderManager.HasInstance
                    ? HolderManager.Instance.FindHolderPublic(rc.holderId) : null;
                if (h == null || h.chainGroupId != chainGroupId) continue;
                Unboard(rc, publishDeployDone: true);
            }
            if (DebugLog) Debug.Log($"[RailHolder] chain group {chainGroupId} 전원 소진 → 동시 하차");
        }

        /// <summary>[놓침 fix] 프레임당 최대 발사 수 — 한 프레임에 지나치는 라인 수(가속 시 2~3)를 커버.</summary>
        private const int MAX_SHOTS_PER_FRAME = 3;

        /// <summary>탄창 소진 — 상자를 치우고 캐리지를 비운다.
        /// publishDeployDone: 정상 소진(발사로 탄창 0)일 때만 true — Frozen 해동/Pipe 방출/분석 무브를 구동.
        ///   Zap(색 제거) 등 외부 소진은 false: 부스터가 자체적으로 해동 크레딧(DecrementFrozenHoldersHP)을
        ///   주므로 여기서 또 발행하면 Frozen 이 이중 감산으로 너무 빨리 녹는다(기믹 감사 2026-07-21).</summary>
        private void Unboard(Carriage c, bool publishDeployDone = true)
        {
            int holderId = c.holderId;

            // [기믹 감사 2026-07-21] 소진 = 다트 모드의 '배포 완료'와 동등한 사건 — 컬럼을 먼저 확보하고
            //   소진 처리 후 OnHolderDeploymentDone 을 발행한다. 이 이벤트가 Frozen 해동(frozenHP--)·
            //   Pipe/Spawner 다음 방출(ProcessSpawners)·분석 moves_used 를 구동하는데, 기존엔 미발행이라
            //   Frozen 이 영영 안 녹고 파이프가 다음 payload 를 방출하지 않는 소프트락이 있었다.
            int column = 0;
            if (HolderManager.HasInstance)
            {
                HolderData hd = HolderManager.Instance.FindHolderPublic(holderId);
                if (hd != null) column = hd.column;
            }

            if (HolderManager.HasInstance) HolderManager.Instance.MarkRailHolderConsumed(holderId);
            if (HolderVisualManager.HasInstance) HolderVisualManager.Instance.DespawnRailMountedHolder(holderId);

            c.holderId = -1;
            c.color = -1;
            c.nextFireAt = 0f;
            c.boarding = false;

            if (publishDeployDone)
                EventBus.Publish(new OnHolderDeploymentDone { holderId = holderId, column = column });

            if (DebugLog) Debug.Log($"[RailHolder] carriage={c.index} holder={holderId} 소진 → 하차 (deploydone={publishDeployDone})");
        }

        // ── Step1 착지열 사이클 ───────────────────────────────────────────────────────────────
        /// <summary>한 바퀴 완주한 캐리지 처리 — 탄창 남으면 빈 착지 슬롯에 안착, 없으면 만석 실패 대기.
        /// (탄창 0 은 발사 시점에 Unboard(즉시 소멸)로 처리되므로 여기 도달 시엔 정상적으로 탄창>0.)</summary>
        private void HandleLapComplete(Carriage c, Vector3 railPos, Vector3 fireDir, float turnSpeedDeg)
        {
            int holderId = c.holderId;
            if (holderId < 0) { c.distanceSinceBoard = 0f; return; }

            // 안전망 + [체인 재결속]: 외부 제거(isConsumed)는 착지 없이 소멸(크레딧 미발행).
            //   탄창 0 체인 멤버는 파트너 잔탄이 남았으면 '소진 대기' — 착지하지 않고 계속 순회(체인 유지).
            HolderData lapHolder = HolderManager.HasInstance
                ? HolderManager.Instance.FindHolderPublic(holderId) : null;
            if (lapHolder == null || lapHolder.isConsumed)
            {
                Unboard(c, publishDeployDone: false);
                return;
            }
            if (lapHolder.magazineCount <= 0)
            {
                if (lapHolder.chainGroupId >= 0
                    && HolderManager.Instance.ChainGroupHasRemainingAmmo(lapHolder.chainGroupId))
                {
                    c.distanceSinceBoard -= _pathLength;   // 랩 카운터만 리셋, 계속 순회
                    SetHolderPose(holderId, railPos, fireDir, turnSpeedDeg);
                    return;
                }
                HandleEmptyRider(c, lapHolder);
                return;
            }

            int slot = FindFreeLandingSlot();
            if (slot < 0)
            {
                // 착지열 만석 + 복귀 불가 → 실패 대기. 플래그는 TickCarriages 가 매 틱 지우고 여기서 다시 세우는
                //   자가치유 구조 — 유저가 착지 홀더를 재탭해 슬롯을 비우면 다음 틱에 착지가 성공하며 저절로 해제.
                // [QA] 막힌 캐리지도 레일 위에서 계속 순회(포즈 갱신) — 기존엔 위치 갱신이 끊겨 화면에 얼어붙었다.
                _landingOverflowFail = true;
                SetHolderPose(holderId, railPos, fireDir, turnSpeedDeg);
                if (DebugLog) Debug.Log($"[RailHolder] 착지열 만석 — holder={holderId} 완주 복귀 불가 → 실패 대기");
                return;
            }

            // 빈 슬롯 안착: 데이터 상태 전환 + 시각 파킹(회전 원복 + DOJump 안착 연출) + 캐리지 비움.
            _landingSlots[slot] = holderId;
            if (HolderManager.HasInstance) HolderManager.Instance.MoveRailHolderToLanding(holderId, slot);

            if (HolderVisualManager.HasInstance
                && HolderVisualManager.Instance.TryGetLandingSlotWorldPos(slot, _landingSlots.Length, out Vector3 landPos))
                HolderVisualManager.Instance.ParkRailHolderAtLanding(
                    holderId, landPos, GameManager.Instance.Board.railHolderBoardFlightTime);

            c.holderId = -1;
            c.color = -1;
            c.nextFireAt = 0f;
            c.boarding = false;
            c.distanceSinceBoard = 0f;

            if (DebugLog) Debug.Log($"[RailHolder] carriage={c.index} holder={holderId} 완주 → 착지 슬롯 {slot}");
        }

        private int FindFreeLandingSlot()
        {
            if (_landingSlots == null) return -1;
            for (int i = 0; i < _landingSlots.Length; i++)
                if (_landingSlots[i] < 0) return i;
            return -1;
        }

        private bool IsLandingRowFull()
        {
            if (_landingSlots == null) return false;
            for (int i = 0; i < _landingSlots.Length; i++)
                if (_landingSlots[i] < 0) return false;
            return true;
        }

        /// <summary>
        /// Step4: 착지열 점유를 OnRailOccupancyChanged 로 발행 — 기존 경고 게이지/위험 연출 파이프라인
        /// (BoardStateManager GaugeStage, UI 게이지)을 그대로 재사용한다. 홀더 모드의 압력계는 레일 점유
        /// (항상 0, 다트가 안 올라감)가 아니라 착지열 채움율이기 때문. 점유 변화 시에만 발행(스팸 방지).
        /// </summary>
        private int _lastPublishedLandingOccupied = -1;

        // Step5 Analytics: 홀더 모드의 압력 지표 = 착지열 채움율. 레일 점유(항상 0) 대신
        //   play_event 의 peak/avg_resource_usage_ratio 로 적재된다(AnalyticsLevelTracker 가드 분기).
        private float _peakLandingFill;
        private double _landingFillSum;
        private int _landingFillSamples;
        /// <summary>이번 레벨 동안의 착지열 최대 채움율(0~1).</summary>
        public float PeakLandingFillRatio => _peakLandingFill;
        /// <summary>이번 레벨 동안의 착지열 평균 채움율(0~1). 틱마다 샘플.</summary>
        public float AverageLandingFillRatio =>
            _landingFillSamples > 0 ? (float)(_landingFillSum / _landingFillSamples) : 0f;

        private void PublishLandingOccupancy()
        {
            if (_landingSlots == null) return;
            int occupied = 0;
            for (int i = 0; i < _landingSlots.Length; i++)
                if (_landingSlots[i] >= 0) occupied++;

            float fill = (float)occupied / _landingSlots.Length;
            if (fill > _peakLandingFill) _peakLandingFill = fill;
            _landingFillSum += fill;
            _landingFillSamples++;

            if (occupied == _lastPublishedLandingOccupied) return;
            _lastPublishedLandingOccupied = occupied;

            EventBus.Publish(new OnRailOccupancyChanged
            {
                activeDarts = occupied,
                totalSlots = _landingSlots.Length,
                occupancy = fill
            });
        }

        /// <summary>착지열 홀더를 다시 레일에 태운다 — 슬롯을 비우고 정거장 대기열에 합류.
        /// [체인 재결속 2026-07-21] 체인 멤버 재탭 = 착지 중인 그룹 멤버 전원 함께 재탑승(단독 재탑승 금지 —
        /// 사용자 사양: 그룹 재결속 강제). 전원분 탑승 여유가 없으면 전체 튕김.</summary>
        private bool TryReboardFromLanding(HolderData holder)
        {
            if (holder == null || !holder.isOnLandingRow) return false;
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return false;

            if (holder.chainGroupId >= 0)
            {
                // 착지 중인 그룹 멤버 수집(레일에 남아 있는 멤버는 이미 탑승 중이므로 대상 아님).
                _chainReboardBuffer.Clear();
                if (_landingSlots != null)
                {
                    for (int i = 0; i < _landingSlots.Length; i++)
                    {
                        if (_landingSlots[i] < 0) continue;
                        HolderData h = HolderManager.Instance.FindHolderPublic(_landingSlots[i]);
                        if (h != null && h.isOnLandingRow && h.chainGroupId == holder.chainGroupId)
                            _chainReboardBuffer.Add(h);
                    }
                }
                if (_chainReboardBuffer.Count == 0) return false;
                if (RemainingBoardCapacity() < _chainReboardBuffer.Count) return false;   // 전원 보장 실패 → 튕김

                bool any = false;
                for (int i = 0; i < _chainReboardBuffer.Count; i++)
                    any |= ReboardOneFromLanding(_chainReboardBuffer[i]);
                if (DebugLog && any)
                    Debug.Log($"[RailHolder] chain group {holder.chainGroupId} 착지 멤버 {_chainReboardBuffer.Count}개 그룹 재탑승");
                return any;
            }

            // [Pixel Flow 정합] 탑승 상한(라이더+대기자) 이내로만 예약 — 대기자 전원의 탑승이 항상 보장된다.
            if (RemainingBoardCapacity() <= 0) return false;   // → 튕김
            return ReboardOneFromLanding(holder);
        }

        private readonly List<HolderData> _chainReboardBuffer = new List<HolderData>(8);

        /// <summary>착지 홀더 1개 재탑승 코어 — 데이터 마운트 + 시각 마운트 + 슬롯 비움 + 정거장 합류.</summary>
        private bool ReboardOneFromLanding(HolderData holder)
        {
            int slotIndex = holder.landingSlotIndex;

            if (!HolderManager.Instance.TryMountLandingHolderOnRail(holder.holderId, out int color, out int magazine))
                return false;

            if (!HolderVisualManager.Instance.TryMountHolderVisualOnRail(holder.holderId, out Vector3 fromPos))
            {
                // 시각 실패 → 데이터 롤백(다시 착지 상태로) 방지: 안전하게 소진 처리.
                HolderManager.Instance.MarkRailHolderConsumed(holder.holderId);
                if (slotIndex >= 0 && slotIndex < _landingSlots.Length) _landingSlots[slotIndex] = -1;
                return false;
            }

            if (slotIndex >= 0 && slotIndex < _landingSlots.Length) _landingSlots[slotIndex] = -1;

            AddStationWaiter(holder.holderId, color, fromPos);

            if (DebugLog)
                Debug.Log($"[RailHolder] reboard → 정거장 대기열 ← 착지슬롯 {slotIndex} holder={holder.holderId} mag={magazine}");
            return true;
        }

        /// <summary>
        /// PROTO_RAIL_HOLDER_20260716 [정거장]: 탭 = 정거장 예약 — 상자가 레일 왼쪽아래 모서리 정거장으로
        /// 날아가 줄 서고, 빈 캐리지(규칙 간격의 빈 자리)가 정거장을 지날 때 순서대로 올라탄다.
        ///   ① 대기자 수 ≥ 빈 캐리지 수 → false (호출측이 튕김 연출 — 전원 탑승 보장 초과 예약 금지)
        ///   ② 앞줄 상자 태울 수 없음(기믹 등) → false (호출측이 튕김 연출)
        ///   ③ 가능 → 큐에서 빼고 정거장 대기열 합류 → true
        /// forceHolderId >= 0 이면 그 홀더를 앞줄 대신 강제로 태운다(Hand 부스터용).
        /// </summary>
        private bool TryBoardImmediately(int column, int forceHolderId = -1)
        {
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return false;

            // [Pixel Flow 정합] 탑승 상한(라이더+대기자) 이내로만 예약(초과 예약 시 영영 못 타는 대기자 발생).
            if (RemainingBoardCapacity() <= 0) return false;   // → 튕김

            int holderId, color, magazine;
            bool taken;
            if (forceHolderId >= 0)
            {
                holderId = forceHolderId;
                taken = HolderManager.Instance.TryMountHolderOnRailById(forceHolderId, out color, out magazine);
            }
            else
            {
                taken = HolderManager.Instance.TryMountFrontHolderOnRail(column, out holderId, out color, out magazine, out int chainGroup);
                // [기믹 감사] 앞줄이 체인 멤버(그룹 2+) → 단독 탑승 금지, 그룹 일괄 탑승 경로로 라우팅.
                if (!taken && chainGroup >= 0)
                    return TryBoardChainGroup(chainGroup);
            }
            if (!taken) return false;   // 앞줄이 기믹/빈 상자 등 → 튕김

            if (!HolderVisualManager.Instance.TryMountHolderVisualOnRail(holderId, out Vector3 fromPos))
            {
                // 시각이 없으면(지연 스폰 등) 데이터만 올라가면 유령 상자 → 되돌린다.
                HolderManager.Instance.MarkRailHolderConsumed(holderId);
                return false;
            }

            AddStationWaiter(holderId, color, fromPos);

            if (DebugLog)
                Debug.Log($"[RailHolder] board → 정거장 대기열 ← col{column} holder={holderId} " +
                          $"color={color} mag={magazine} flightFrom={fromPos}");
            return true;
        }

        /// <summary>
        /// [기믹 감사 2026-07-21] Chain 그룹 일괄 탑승 — 전 멤버가 함께 정거장 대기열에 합류(FIFO 연속 탑승).
        /// 게이트: 빈 캐리지 여유가 그룹 전원 수용 가능할 때만(분할 탑승 금지 — 다트 모드 관용).
        /// </summary>
        private readonly List<HolderData> _chainMountBuffer = new List<HolderData>(8);
        private bool TryBoardChainGroup(int chainGroupId)
        {
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return false;

            int size = HolderManager.Instance.GetChainGroup(chainGroupId).Count;
            if (size == 0) return false;
            if (RemainingBoardCapacity() < size) return false;   // 전원 탑승 보장 실패 → 튕김

            if (!HolderManager.Instance.TryMountChainGroupOnRail(chainGroupId, _chainMountBuffer)) return false;

            for (int i = 0; i < _chainMountBuffer.Count; i++)
            {
                HolderData h = _chainMountBuffer[i];
                if (HolderVisualManager.Instance.TryMountHolderVisualOnRail(h.holderId, out Vector3 fromPos))
                    AddStationWaiter(h.holderId, h.color, fromPos);
                else
                    HolderManager.Instance.MarkRailHolderConsumed(h.holderId);   // 시각 실패 폴백(단일 경로와 동일)
            }
            if (DebugLog) Debug.Log($"[RailHolder] chain group {chainGroupId} 일괄 탑승 — {_chainMountBuffer.Count}개 정거장 합류");
            return true;
        }

        /// <summary>[Pixel Flow 정합] 현재 레일 위 라이더 수(탑승 hop 중 포함).</summary>
        private int CountMountedRiders()
        {
            int n = 0;
            for (int i = 0; i < _carriages.Count; i++)
                if (!_carriages[i].IsEmpty) n++;
            return n;
        }

        /// <summary>[Pixel Flow 정합] 남은 탑승 여유 = 상한 − 라이더 − 정거장 대기자(예약분).</summary>
        private int RemainingBoardCapacity()
            => _maxRiders - CountMountedRiders() - _stationWaiters.Count;

        /// <summary>
        /// [Pixel Flow 정합] 지금 정거장에서 즉시 탑승 가능한가 — ① 라이더 수 상한 미만
        /// ② 정거장 지점 기준 모든 라이더와 최소 간격(railHolderMinGap) 확보(양방향 최단거리).
        /// 전원 같은 벨트 속도라 탑승 후 간격은 그대로 보존된다 — 등간격 빈 자리를 기다릴 필요가 없다.
        /// </summary>
        private bool CanBoardAtStation()
        {
            if (CountMountedRiders() >= _maxRiders) return false;

            float minGap = Mathf.Max(0.1f, GameManager.Instance.Board.railHolderMinGap);
            float station = StationProgress;
            for (int i = 0; i < _carriages.Count; i++)
            {
                Carriage c = _carriages[i];
                if (c.IsEmpty) continue;
                float fwd = station - ProgressOf(c);
                while (fwd < 0f) fwd += _pathLength;
                float dist = Mathf.Min(fwd, _pathLength - fwd);   // 랩 최단거리
                if (dist < minGap) return false;
            }
            return true;
        }

        /// <summary>[Pixel Flow 정합] 정거장 지점에 라이더 슬롯 확보 — 빈 슬롯 재사용, 없으면 생성.
        /// laneOffset 을 '지금의 정거장 progress'로 잡아 새 라이더가 정확히 정거장에서 출발한다.</summary>
        private Carriage AcquireRiderAtStation()
        {
            Carriage rider = null;
            for (int i = 0; i < _carriages.Count; i++)
                if (_carriages[i].IsEmpty) { rider = _carriages[i]; break; }
            if (rider == null)
            {
                rider = new Carriage();
                _carriages.Add(rider);
            }

            rider.index = _riderSerial++;
            float offset = StationProgress - _travel;
            while (offset < 0f) offset += _pathLength;
            rider.laneOffset = offset;
            return rider;
        }

        /// <summary>[정거장] 대기열 합류 — 출발점에서 정거장 대기 슬롯으로 아치 비행 시작.</summary>
        private void AddStationWaiter(int holderId, int color, Vector3 fromPos)
        {
            _stationWaiters.Add(new StationWaiter
            {
                holderId = holderId,
                color = color,
                fromPos = fromPos,
                pos = fromPos,
                flightT0 = Time.time,
                flightDur = GameManager.Instance.Board.railHolderBoardFlightTime
            });
        }

        /// <summary>[정거장] 정거장의 경로 거리 — railHolderBoardStation01(0=왼쪽아래 모서리) × 경로 총길이.</summary>
        private float StationProgress =>
            Mathf.Clamp01(GameManager.Instance.Board.railHolderBoardStation01) * _pathLength;

        /// <summary>[정거장] queueIndex 번째 대기 슬롯 월드 좌표 — 레일 바깥(아래 -Z)으로 물러난 한 지점에서
        /// 같은 X/Z 로 Y축 탑 쌓기(사용자 요청 2026-07-21: -Z 줄서기는 카메라 각도상 대각선으로 보임 → 일자 탑).
        /// index 0 = 맨 아래(먼저 탑승), 위 상자가 아래 상자를 가려 텍스트가 안 보이는 건 의도된 허용.
        /// 앞이 빠지면 MoveTowards 로 탑이 한 층씩 내려앉는다.</summary>
        private Vector3 GetStationWaitPos(int queueIndex)
        {
            Vector3 basePos = RailManager.Instance.GetPositionAtDistance(StationProgress);
            basePos.z -= STATION_WAIT_OUT;
            basePos.y += STATION_STACK_HEIGHT * queueIndex;
            return basePos;
        }

        /// <summary>
        /// [정거장] 대기열 틱 — ① 외부 소진(Zap 등) 정리 ② 비행/대기 위치 갱신(앞이 빠지면 당겨짐)
        /// ③ 앞 대기자(도착 완료)에게, 정거장 앞 트리거 반경에 들어온 빈 캐리지를 FIFO 배정(짧은 hop 탑승).
        /// </summary>
        private void TickStationWaiters(float arcHeight)
        {
            if (_stationWaiters.Count == 0) return;

            // ① 외부 소진 정리 — 대기 중 Zap 등으로 탄창이 사라진 상자는 하차 처리.
            for (int i = _stationWaiters.Count - 1; i >= 0; i--)
            {
                int id = _stationWaiters[i].holderId;
                if (!HolderManager.HasInstance || HolderManager.Instance.GetMagazineCount(id) <= 0)
                {
                    if (HolderManager.HasInstance) HolderManager.Instance.MarkRailHolderConsumed(id);
                    if (HolderVisualManager.HasInstance) HolderVisualManager.Instance.DespawnRailMountedHolder(id);
                    _stationWaiters.RemoveAt(i);
                }
            }

            // ② 비행(아치) / 대기 위치 갱신 — 대기자는 앞이 빠지면 MoveTowards 로 새 슬롯에 당겨진다.
            for (int i = 0; i < _stationWaiters.Count; i++)
            {
                StationWaiter w = _stationWaiters[i];
                Vector3 target = GetStationWaitPos(i);
                if (!w.arrived)
                {
                    float t = (Time.time - w.flightT0) / Mathf.Max(0.01f, w.flightDur);
                    if (t >= 1f)
                    {
                        w.arrived = true;
                        w.pos = target;
                    }
                    else
                    {
                        Vector3 pos = Vector3.Lerp(w.fromPos, target, EaseOutCubic(t));
                        pos.y += arcHeight * 4f * t * (1f - t);
                        w.pos = pos;
                    }
                }
                else
                {
                    w.pos = Vector3.MoveTowards(w.pos, target, STATION_SHIFT_SPEED * Time.deltaTime);
                }
                SetHolderPos(w.holderId, w.pos);
            }

            // ③ [Pixel Flow 정합 2026-07-21] 즉시 탑승 — 등간격 빈 자리를 기다리지 않는다. 라이더 수 상한 미만이고
            //   정거장 지점 기준 최소 간격(railHolderMinGap)만 확보되면 앞 대기자가 바로 정거장 progress 에 탑승.
            //   전원 같은 벨트 속도라 탑승 후 간격은 자동 보존. 연속 탭 시 앞 라이더가 minGap 만큼 이동하는
            //   시간(≈0.1s대) 간격으로 줄줄이 올라타는 원작 리듬이 나온다. (비행 중 대기자도 즉시 매칭 — 기존 유지.)
            //   프레임당 1명씩 — 다음 대기자는 앞 라이더가 minGap 을 벗어나는 다음 기회에.
            if (_stationWaiters.Count > 0 && CanBoardAtStation())
            {
                StationWaiter w = _stationWaiters[0];
                _stationWaiters.RemoveAt(0);

                Carriage rider = AcquireRiderAtStation();
                rider.holderId = w.holderId;
                rider.color = w.color;
                rider.boarding = true;
                rider.boardStartTime = Time.time;
                rider.boardDuration = Mathf.Max(0.08f, GameManager.Instance.Board.railHolderBoardFlightTime * 0.6f);
                rider.boardFromPos = w.pos;
                rider.distanceSinceBoard = 0f;
                rider.nextFireAt = 0f;
                if (DebugLog)
                    Debug.Log($"[RailHolder] 정거장 즉시 탑승 — holder={w.holderId} → rider#{rider.index} " +
                              $"(riders={CountMountedRiders()}/{_maxRiders})");
            }
        }

        /// <summary>
        /// 그 컬럼 접점에서 진행 방향으로 가장 곧 도달할 빈 캐리지 — 상자가 레일을 가로지르지 않고 가깝게 붙는다.
        /// 접점 progress 를 못 구하면(접점 미캐시) 그냥 첫 빈 캐리지.
        /// [정거장 모델 도입(2026-07-21)으로 미사용 — 직행 탑승 롤백용 보존.]
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
            if (holder == null || holder.isConsumed) return;

            // Step1 착지열: 착지열 홀더 탭 → 레일 재탑승(슬롯을 비우는 행위라 만석이어도 허용).
            if (holder.isOnLandingRow)
            {
                if (!TryReboardFromLanding(holder)) Bounce(evt.holderId);
                return;
            }

            if (holder.isRailMounted) return;   // 이미 레일 위

            // Step1 착지열: 착지열 만석이면 '새로' 태우는 건 차단(튕김) — 완주 시 복귀 불가라 실패만 앞당김.
            //   (재탑승은 위에서 슬롯을 비우므로 허용, 신규 탑승만 차단.)
            if (IsLandingRowFull())
            {
                Bounce(evt.holderId);
                return;
            }

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

            // [QA] 부스터 경로도 탭 경로와 동일 규칙 — ① 착지 홀더 선택 = 재탑승(슬롯 비움이라 만석에도 허용),
            //   ② 신규 탑승은 착지열 만석이면 차단. 기존엔 이 경로가 둘 다 우회해 착지 슬롯이 유령 점유로 새고
            //   만석 차단도 뚫렸다.
            if (holder.isOnLandingRow)
            {
                bool reOk = TryReboardFromLanding(holder);
                if (!reOk) Bounce(holderId);
                return reOk;
            }
            if (IsLandingRowFull())
            {
                Bounce(holderId);
                return false;
            }

            // [기믹 감사 2026-07-21] 체인 멤버 부스터 선택 = 그룹 일괄 탑승(다트 모드 ForceSelect 의 체인 관용).
            //   단독 강제 탑승을 허용하면 체인이 분할된다.
            if (HolderManager.HasInstance && holder.chainGroupId >= 0
                && HolderManager.Instance.GetChainGroup(holder.chainGroupId).Count > 1)
            {
                bool chainOk = TryBoardChainGroup(holder.chainGroupId);
                if (!chainOk) Bounce(holderId);
                return chainOk;
            }

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

        /// <summary>
        /// Step5: 이어하기 릴리프 — 착지열 전원을 큐 맨뒤로 복귀시켜 판을 재개한다.
        /// 착지열이 비면 만석으로 복귀 못 하던 레일 홀더가 다음 틱에 착지(자가치유로 실패 대기도 해제).
        /// 탄약은 그대로라 '공간'만 사는 이어하기 — 기존 다트 모드의 '레일 비우기'와 동일한 의미.
        /// </summary>
        private void HandleContinueApplied(OnContinueApplied _)
        {
            if (!ModeActive) return;

            if (_landingSlots != null && HolderManager.HasInstance)
            {
                for (int i = 0; i < _landingSlots.Length; i++)
                {
                    int id = _landingSlots[i];
                    if (id < 0) continue;
                    int col = HolderManager.Instance.ReturnLandingHolderToQueue(id);
                    if (col >= 0 && HolderVisualManager.HasInstance)
                        HolderVisualManager.Instance.ReturnLandingHolderVisualToQueue(id, col);
                    _landingSlots[i] = -1;
                }
            }

            _boardFinished = false;   // OnBoardFailed 로 멈춘 틱 재개
            if (DebugLog) Debug.Log("[RailHolder] Continue relief — 착지열 전원 큐 복귀, 판 재개");
        }

        #endregion

        #region Reset

        public void ResetAll()
        {
            _carriages.Clear();
            _boardProgressByColumn.Clear();
            _travel = 0f;
            _initialized = false;
            _boardFinished = false;
            // Step1 착지열: 다음 레벨/재시도로 넘어갈 때 착지 상태·실패 대기 초기화.
            _landingSlots = null;
            _landingOverflowFail = false;
            _lastBeltDelta = 0f;
            _lastPublishedLandingOccupied = -1;   // Step4: 새 레벨 첫 틱에 0/N 게이지 발행 보장
            _peakLandingFill = 0f;                // Step5: Analytics 지표 리셋
            _landingFillSum = 0; _landingFillSamples = 0;
            _stationWaiters.Clear();              // [정거장] 대기열 초기화
            if (HolderVisualManager.HasInstance)  // [착지열 마커] 다음 레벨에서 새 좌표로 재생성
                HolderVisualManager.Instance.ClearLandingSlotMarkers();
            // [최적화] 재빌드 스로틀 해제 — 다트 모드 레벨로 넘어가면 기존 즉시-재빌드 동작 복원.
            DirectionalTargeting.MinRebuildInterval = 0f;
            _riderSerial = 0;   // [Pixel Flow 정합] 라이더 일련번호 리셋
        }

        #endregion
    }
}
#endif
