using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// [WS 0단계 2026-06-12] 승리 복귀 시 로비 WS 연출(flame 비행→게이지)보다 '앞'에 재생되는 Dim 배경 연출 팝업.
    /// 아트 제작 PopupWinningStreakReward.prefab(스크립트 미부착)을 Instantiate 후 이름 기반으로 자식을 와이어링 —
    /// UIManager.OpenUI&lt;T&gt; 는 prefab 에 T 컴포넌트가 부착돼 있어야 해서 사용하지 않음.
    ///
    /// prefab 구조(아트):
    ///   PopupWinningStreakReward
    ///   ├ Overlay                                  — Dim (터치 차단)
    ///   ├ FxWinningStreakReward
    ///   │ ├ FX (RotateLight / FX_Glow×3 / ParticleLight[off] / FX_BackLightR)
    ///   │ ├ Icon
    ///   │ ├ TxtAmountOutline > TxtAmount           — 획득 포인트 카운터 (수 연산 표시)
    ///   │ └ FXReward[off]
    ///   └ FXItem (그룹)
    ///     ├ FXItem                                 — 기본 포인트 비행체 (파티클 트레일)
    ///     ├ FXBadge[off]                           — 난이도 배수 비행체 (Hard/SuperHard 전용)
    ///     └ FXMultiple > TextGaugeOutline > TextGauge — 연승 배수 비행체 (x5/10/25/100)
    ///
    /// 시퀀스 (디자이너 사양 2026-06-12, 수 연산 모델 confirmed):
    ///   획득 포인트 = 1(기본) × 난이도배수 × 연승배수.
    ///   1. 등장 — Dim + ParticleLight 1회. 카운터는 빈 값.
    ///   2. FXItem 포물선 비행 → TxtAmountOutline 도착 → "1" 표시.
    ///   3. Hard/SuperHard 클리어면 FXBadge 활성화 → 포물선 비행 → 도착 순간 ×난이도배수.
    ///   4. 연승 배수 > 1 이면 FXMultiple(x{n}) → 포물선 비행 → 도착 순간 ×연승배수.
    ///   매 도착마다: 수 갱신 + TxtAmount 컬러 #6BFF8F + TxtAmountOutline 머티리얼
    ///   Poppins-Bold-GreenOutline + FXReward 파티클 재생 (confirmed: 매 도착마다).
    ///   5. 유지 후 자동 닫힘(파괴). 호출측(UILobby)은 IsFinished 폴링으로 대기.
    /// </summary>
    public class PopupWinningStreakReward : MonoBehaviour
    {
        // deprecated — Overlay 페이드(2026-06-19 추가 지시)를 분리해 직렬화한 이후 미사용.
        // 외부 참조 없음(grep 확인 — 본 파일 내 단일 사용처도 IconScaleT* 합으로 교체). 안전을 위해 상수만 남김.
        private const float IntroHoldSeconds = 0.6f;   // 등장 → 첫 비행까지
        private const float StepHoldSeconds  = 0.45f;  // 도착(수 연산) → 다음 비행까지 (카운트업 완료 대기 포함)
        // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: 연출 단축 — 1.1f → 0.6f (마지막 카운트업 0.35s 커버 + 짧은 hold).
        private const float OutroHoldSeconds = 0.6f;    // 마지막 연산 → 자동 닫힘까지
        private const float FlyDurationSeconds = 0.5f; // 포물선 비행 시간
        private const float FlyJumpPower = 150f;       // 포물선 정점 높이 (로컬/px)
        private const float CountUpSeconds = 0.35f;    // 수 연산 카운트업(이전 값→새 값 순차 증가) 시간
        // 디자이너 등장 사양(2026-06-19): IntroHoldSeconds(0.6s) 내부에 자연스럽게 들어가도록 분할 — t1+t2+t3≈0.6s.
        private const float IntroFadeDur = 0.25f;      // Overlay alpha 0→220/255 페이드 (Icon 1.3 도달과 정렬).
        private const float IconScaleT1 = 0.25f;       // 0 → 1.3 (OutBack)
        private const float IconScaleT2 = 0.18f;       // 1.3 → 0.9 (InOutSine). ParticleLight 1회는 이 구간 '시작'(=1.3 peak) 에 발화 — 2026-06-19 추가 지시.
        private const float IconScaleT3 = 0.17f;       // 0.9 → 1.0 (OutSine)
        private const float OverlayTargetAlpha = 220f / 255f;
        private static readonly Color32 GainColor = new Color32(0x6B, 0xFF, 0x8F, 0xFF);
        // TextMesh Pro/Resources 하위라 Resources.Load 가능.
        private const string GreenOutlineMaterialPath = "Fonts & Materials/Poppins-Bold-GreenOutline";

        /// <summary>표시 중 여부 — UILobby 스와이프 게이트용 (이 팝업은 UIBase 가 아니라 GetTopmostBackConsumingUI 에 안 잡힘).</summary>
        public static bool IsShowing { get; private set; }

        /// <summary>시퀀스 종료(또는 파괴) 여부 — 호출측 대기 플래그.</summary>
        public bool IsFinished { get; private set; }

        private TextMeshProUGUI _txtAmount;
        private TextMeshProUGUI _txtAmountOutline;
        private GameObject _particleLight;
        private GameObject _fxReward;
        private GameObject _fxItemFly;
        private GameObject _fxBadge;
        private GameObject _fxMultiple;
        private Image _fxBadgeImage;
        private Transform _fxWinningStreakReward;
        private Vector3 _fxWinningStreakRewardBaseScale = Vector3.one;
        private Image _overlay;
        private Transform _icon;
        private List<Transform> _fxGlows;
        private TextMeshProUGUI _txtGauge;
        private TextMeshProUGUI _txtGaugeOutline;
        private Material _greenOutlineMat;
        private Tween _punchTween;
        private Tween _countTween;
        private Tween _rewardRootPulseTween;
        private int _displayedAmount;   // 현재 카운터 표시 값 — 카운트업 시작점

        /// <summary>프리팹 스폰 + 시퀀스 시작. 프리팹/부모 미존재 시 null (호출측은 null 이면 대기 없이 진행).
        /// diffMult/streakMult 는 1 이상으로 정규화. gainedPoints 는 최종 보정값(0 이면 보정 생략).</summary>
        public static PopupWinningStreakReward Play(int diffMult, int streakMult, int gainedPoints, bool showBadge,
            DifficultyPurpose clearedDifficulty = DifficultyPurpose.Normal)
        {
            if (!UIManager.HasInstance) return null;
            var prefab = Resources.Load<GameObject>(Const.POPUP_WINNING_STREAK_REWARD);
            if (prefab == null)
            {
                Debug.LogWarning($"[PopupWinningStreakReward] prefab not found: {Const.POPUP_WINNING_STREAK_REWARD}");
                return null;
            }
            Transform parent = UIManager.Instance.PopupTr != null ? UIManager.Instance.PopupTr : UIManager.Instance.UiTr;
            if (parent == null) return null;

            var go = Instantiate(prefab, parent);
            // ROLLBACK_WS_REWARD_POPUP_HANG_FIX_20260618: 프리팹이 비활성 상태로 저장돼 있으면 StartCoroutine 이
            //   조용히 무시되어 RunSequence 가 영영 실행 안 됨 → IsFinished 가 false 로 고정 → 호출측(UILobby:679)
            //   while(!IsFinished) 무한 대기 → 로비 FX 코루틴이 finally 도달 못 함 → PlayButton 영구 차단(Bug1/2).
            //   인스턴스를 무조건 활성화해 코루틴이 실제로 돌도록 보장한다. 롤백: 이 줄 제거.
            go.SetActive(true);
            var ctrl = go.AddComponent<PopupWinningStreakReward>();
            ctrl.WireRefs();
            ctrl.StartCoroutine(ctrl.RunSequence(
                Mathf.Max(1, diffMult), Mathf.Max(1, streakMult), gainedPoints, showBadge, clearedDifficulty));
            return ctrl;
        }

        private void WireRefs()
        {
            _txtAmountOutline = FindDeepComponent<TextMeshProUGUI>(transform, "TxtAmountOutline");
            _txtAmount        = FindDeepComponent<TextMeshProUGUI>(transform, "TxtAmount");
            _particleLight    = FindDeepGameObject(transform, "ParticleLight");
            _fxReward         = FindDeepGameObject(transform, "FXReward");
            _fxWinningStreakReward = FindDeep(transform, "FxWinningStreakReward")
                                   ?? FindDeep(transform, "FXWinningStreakReward");
            if (_fxWinningStreakReward != null)
            {
                _fxWinningStreakRewardBaseScale = _fxWinningStreakReward.localScale;
                if (_fxWinningStreakRewardBaseScale == Vector3.zero)
                    _fxWinningStreakRewardBaseScale = Vector3.one;
            }

            // 디자이너 등장 사양(2026-06-19) — Overlay 페이드인 + Icon 3단 scale + FX_Glow scale.
            // 와이어링 실패는 graceful 무시(로그 경고만): 기존 PopupWinningStreakReward 패턴.
            _overlay = FindDeepComponent<Image>(transform, "Overlay");
            Transform iconRoot = _fxWinningStreakReward != null ? _fxWinningStreakReward : transform;
            _icon = FindDeep(iconRoot, "Icon");
            // FX_Glow 노드는 3개로 동일 이름 — 단일 FindDeep 사용 금지(첫 번째만 잡힘). 트리 전체 순회로 모두 수집.
            _fxGlows = new List<Transform>();
            Transform[] allTrs = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTrs.Length; i++)
            {
                if (allTrs[i] != null && allTrs[i].name == "FX_Glow")
                    _fxGlows.Add(allTrs[i]);
            }
            if (_overlay == null) Debug.LogWarning("[PopupWinningStreakReward] Overlay Image not found — fade skipped.");
            if (_icon == null) Debug.LogWarning("[PopupWinningStreakReward] Icon not found — scale intro skipped.");

            // FXItem 은 '그룹 노드'와 그 안의 '비행체' 이름이 동일 — 그룹(루트 직계)을 먼저 찾고 직계 자식으로 구분.
            Transform fxGroup = FindDirectChild(transform, "FXItem");
            if (fxGroup != null)
            {
                _fxItemFly  = FindDirectChildGameObject(fxGroup, "FXItem");
                _fxBadge    = FindDirectChildGameObject(fxGroup, "FXBadge");
                _fxMultiple = FindDirectChildGameObject(fxGroup, "FXMultiple");
            }
            else
            {
                _fxBadge    = FindDeepGameObject(transform, "FXBadge");
                _fxMultiple = FindDeepGameObject(transform, "FXMultiple");
            }
            if (_fxMultiple != null)
            {
                _txtGaugeOutline = FindDeepComponent<TextMeshProUGUI>(_fxMultiple.transform, "TextGaugeOutline");
                _txtGauge        = FindDeepComponent<TextMeshProUGUI>(_fxMultiple.transform, "TextGauge");
            }
            if (_fxBadge != null)
                _fxBadgeImage = _fxBadge.GetComponentInChildren<Image>(true);
        }

        private IEnumerator RunSequence(int diffMult, int streakMult, int gainedPoints, bool showBadge, DifficultyPurpose clearedDifficulty)
        {
            IsShowing = true;
            // 2026-06-19 추가 지시: ParticleLight 는 명시적으로 비활성화 상태에서 시작 — prefab 기본값이
            //   바뀌더라도 회귀 방지. PlayParticleLightAndAutoDisable 가 활성화·재생·자연종료감지·비활성화
            //   전 생애주기를 소유하므로 외부(이 함수 포함) 어디서도 별도 SetActive 호출 금지.
            if (_particleLight != null) _particleLight.SetActive(false);
            _greenOutlineMat = Resources.Load<Material>(GreenOutlineMaterialPath);

            // 등장 — 카운터 빈 값, 비행체들은 출발 전 숨김 (FXBadge/FXMultiple 은 prefab 기본도 off).
            // ROLLBACK_WS_SKIP_BASE_PLUS_ONE_FLY_20260618:
            // Lobby Winning Streak reward popup now starts at +1. The base FXItem flight is
            // skipped so only difficulty/streak multiplier flyers animate into the counter.
            int amount = 1;
            SetBaseAmountText(amount);
            // ROLLBACK_WS_REWARD_INTRO_DESIGN_20260619: 디자이너 등장 사양 — Overlay 페이드 + Icon 3단 scale +
            //   FX_Glow scale + Icon 1.3 peak 도달 시 ParticleLight 활성화·1회 재생. 루트 pulse(StartRewardRootPulse)는
            //   디자이너 사양(2026-06-19) + 추가 지시에 미포함 — 의도된 제거(태스크 owner 추가 지시에서도 언급 없음).
            //   OnDestroy 의 StopRewardRootPulse 는 안전 유지.
            //   ParticleLight lifecycle(2026-06-19 추가 지시): RunSequence 진입 직후 명시적 SetActive(false) →
            //   Icon scale 0→1.3 peak 도달 시 PlayParticleLightAndAutoDisable 가 활성화 후 모든 자식
            //   ParticleSystem 1회 Play → 모든 ParticleSystem.IsAlive(true)=false 가 될 때까지 매 프레임 폴링
            //   (활성 유지) → 자연 종료 후 SetActive(false). 재생 중 강제 비활성화/Stop 호출 금지(회귀 방지).
            //   트리거 시점이 0.9 도달 후(이전 차수)였을 때 사용자 reject — 반드시 '1.3 peak' 정확한 시점 유지.
            //   롤백: StartRewardRootPulse() 호출 복원 + 아래 Overlay/Icon/FX_Glow 시퀀스 제거.
            bool flyMultiple = streakMult > 1;
            if (_fxItemFly != null) _fxItemFly.SetActive(false);
            if (_fxBadge != null)
            {
                ApplyFxBadgeSprite(clearedDifficulty);
                _fxBadge.SetActive(false);
            }
            if (_fxMultiple != null)
            {
                _fxMultiple.SetActive(false);
                string multText = $"x{streakMult}";
                if (_txtGaugeOutline != null) _txtGaugeOutline.text = multText;
                if (_txtGauge != null) _txtGauge.text = multText;
            }

            // Icon/FX_Glow 초기 스케일 사전 세팅 — Overlay 페이드가 진행되는 동안 Icon 이 prefab 초기 크기로
            // 한 프레임도 노출되지 않도록 미리 0/1 로 박제(2026-06-19 추가 지시).
            if (_icon != null) _icon.localScale = Vector3.zero;
            if (_fxGlows != null)
            {
                for (int i = 0; i < _fxGlows.Count; i++)
                {
                    if (_fxGlows[i] != null) _fxGlows[i].localScale = Vector3.one;
                }
            }

            // Overlay: 기존 RGB 보존, alpha 만 0→220/255 페이드인. (Color 통째 덮어쓰기 금지 — prefab 색감 유지.)
            // SetUpdate(true): 게임 일시정지(Time.timeScale=0) 중에도 재생 보장.
            if (_overlay != null)
            {
                Color overlayColor = _overlay.color;
                overlayColor.a = 0f;
                _overlay.color = overlayColor;
                _overlay.DOFade(OverlayTargetAlpha, IntroFadeDur).SetUpdate(true);
            }

            // 2026-06-19 추가 지시: Overlay alpha 가 220/255 에 도달한 '뒤'에야 Icon/FX_Glow/ParticleLight 시작.
            // Overlay→Icon 직렬화의 핵심 — 이 wait 제거 시 다시 같은 프레임에 병렬 시작됨.
            yield return new WaitForSecondsRealtime(IntroFadeDur);

            // Icon: 0 → 1.3(OutBack, 빠른 확대) → 0.9(InOutSine, 반동) → 1.0(OutSine, 안착) — 탄성 시퀀스.
            // ParticleLight lifecycle: Icon 이 1.3 peak 에 도달하는 순간 PlayParticleLightAndAutoDisable 가
            //   활성화·1회 재생을 시작 → IsAlive(true)=false 까지 활성 유지 → 자연 종료 후 SetActive(false).
            //   '빠르게 확대된 peak' 와 라이트 플래시를 합치시키려는 디자이너 의도(2026-06-19 추가 지시:
            //   '1.3에 도달하는 시점에 재생을 시작한다'). 재생 중 강제 비활성화/Stop 호출 금지.
            if (_icon != null)
            {
                _icon.localScale = Vector3.zero;
                Sequence iconSeq = DOTween.Sequence().SetUpdate(true);
                iconSeq.Append(_icon.DOScale(1.3f, IconScaleT1).SetEase(Ease.OutBack));
                iconSeq.AppendCallback(PlayParticleLightAndAutoDisable);
                iconSeq.Append(_icon.DOScale(0.9f, IconScaleT2).SetEase(Ease.InOutSine));
                iconSeq.Append(_icon.DOScale(1.0f, IconScaleT3).SetEase(Ease.OutSine));
            }
            else
            {
                // Icon 와이어링 실패 폴백: ParticleLight 즉시 재생해 기존 등장 임팩트는 유지(자연 종료까지 활성 유지).
                PlayParticleLightAndAutoDisable();
            }

            // FX_Glow 3개: Icon 1.3 peak 와 동시에 1.3 도달, Icon 1.0 복귀 와 동시에 1.0 복귀.
            if (_fxGlows != null)
            {
                for (int i = 0; i < _fxGlows.Count; i++)
                {
                    Transform glow = _fxGlows[i];
                    if (glow == null) continue;
                    glow.localScale = Vector3.one;
                    Sequence glowSeq = DOTween.Sequence().SetUpdate(true);
                    glowSeq.Append(glow.DOScale(1.3f, IconScaleT1).SetEase(Ease.OutSine));
                    glowSeq.Append(glow.DOScale(1.0f, IconScaleT2 + IconScaleT3).SetEase(Ease.InOutSine));
                }
            }

            // Icon 시퀀스 총합(IconScaleT1+T2+T3 = 0.60s) 완료 시점까지 다음 FXItem 비행 시작을 보류.
            // 기존 IntroHoldSeconds(0.6s) 는 '등장→첫 비행' 단일 버퍼였으나 이제 Overlay 페이드(0.25s)가 분리됐으므로
            // Icon 시퀀스 길이로 직접 대기(2026-06-19 추가 지시 — Icon 마무리 전 비행 시작 금지).
            yield return new WaitForSecondsRealtime(IconScaleT1 + IconScaleT2 + IconScaleT3);

            // 1) 기본 포인트 — FXItem 비행 → "1".
            // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: START
            // 2~3) 난이도배수(FXBadge) + 연승배수(FXMultiple) — 디자이너 키프레임(2026-06-15) 오버랩 타임라인.
            //   기존 순차(FlyAndApply×2, ~1.9s)를 scale-up 등장 + 오버랩(~1.1s)으로 단축:
            //   t0.0 뱃지 scale-up 등장 → t0.3 scale-down+비행 → t0.5 계수 등장(뱃지 비행 중) →
            //   t0.6 뱃지 도착·×난이도·소멸 → t0.8 계수 scale-down+비행 → t1.1 계수 도착·×연승·소멸.
            //   (뱃지 없이 계수만이면 계수가 t0.0 부터 단독 진행.)
            //   롤백: 아래 블록을 종전 FlyAndApply 2블록(showBadge/flyMultiple)으로 복원.
            bool flyBadge = showBadge && _fxBadge != null;
            int afterBadge = flyBadge ? amount * diffMult : amount;
            bool doMultiple = flyMultiple && _fxMultiple != null;
            int afterMultiple = doMultiple ? afterBadge * streakMult : afterBadge;
            if (flyBadge || doMultiple)
                yield return PlayCoefficientOverlapFx(flyBadge, afterBadge, doMultiple, afterMultiple);
            amount = afterMultiple;
            // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: END

            // 최종 보정 — config 변경/미캡처 등으로 단계 곱과 실제 적립값이 어긋나면 적립값을 신뢰.
            if (gainedPoints > 0 && amount != gainedPoints)
            {
                _countTween?.Kill();
                _displayedAmount = gainedPoints;
                SetAmountText($"+{gainedPoints}");
            }

            yield return new WaitForSecondsRealtime(OutroHoldSeconds);

            IsFinished = true;
            Destroy(gameObject);
        }

        // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: START — 뱃지/계수 오버랩 타임라인(디자이너 키프레임 2026-06-15).
        /// <summary>난이도배수(FXBadge)+연승배수(FXMultiple) 오버랩 연출. 각 비행체: scale-up 등장 → scale-down 하며
        /// 카운터로 포물선 비행 → 도착 순간 수 연산·소멸. 뱃지 t0.0, 계수 t0.5(뱃지 비행 중 오버랩) — 단독이면 t0.0.</summary>
        private IEnumerator PlayCoefficientOverlapFx(bool badge, int badgeAmount, bool multiple, int multAmount)
        {
            const float APPEAR = 0.3f;      // scale-up 등장
            const float MOVE   = 0.3f;      // 카운터로 비행
            const float MULT_APPEAR_T = 0.5f; // 뱃지와 오버랩 등장 시각
            const float MOVE_SCALE = 0.7f;  // 비행 중 축소 배율

            Sequence seq = DOTween.Sequence().SetUpdate(true);
            if (badge)
                AddCoefficientFlyer(seq, _fxBadge, 0f, APPEAR, MOVE, MOVE_SCALE, badgeAmount);
            if (multiple)
                AddCoefficientFlyer(seq, _fxMultiple, badge ? MULT_APPEAR_T : 0f, APPEAR, MOVE, MOVE_SCALE, multAmount);
            yield return seq.WaitForCompletion();
        }

        /// <summary>비행체 1개를 시퀀스에 키프레임 삽입: appearT 에 scale 0→full 등장 → (appearT+appear) 비행 시작
        /// → (appearT+appear+move) 도착 시 ApplyAmount + 소멸. 스케일/위치는 도착 콜백에서 원복(풀/재사용 안전).</summary>
        private void AddCoefficientFlyer(Sequence seq, GameObject flyer, float appearT, float appear, float move, float moveScale, int amount)
        {
            if (flyer == null || _txtAmountOutline == null) return;
            Transform tr = flyer.transform;
            Vector3 fullScale = tr.localScale;
            if (fullScale == Vector3.zero) fullScale = Vector3.one;
            tr.localScale = Vector3.zero;   // 등장 전까지 0 — DOScale 시작값을 콜백 순서와 무관하게 보장(비활성이라 비가시).
            Vector3 startLocal = tr.localPosition;
            Vector3 end = ResolveLocalPoint(tr.parent as RectTransform, (RectTransform)_txtAmountOutline.transform);
            end.z = startLocal.z;
            float moveStart = appearT + appear;

            seq.InsertCallback(appearT, () =>
            {
                tr.localScale = Vector3.zero;
                tr.localPosition = startLocal;
                if (!flyer.activeSelf) flyer.SetActive(true);
                PlayFxOnce(flyer);
            });
            seq.Insert(appearT, tr.DOScale(fullScale, appear).SetEase(Ease.OutBack).SetUpdate(true));
            seq.Insert(moveStart, tr.DOScale(fullScale * moveScale, move).SetUpdate(true));
            seq.Insert(moveStart, tr.DOLocalJump(end, FlyJumpPower, 1, move).SetEase(Ease.InOutSine).SetUpdate(true));
            seq.InsertCallback(moveStart + move, () =>
            {
                ApplyAmount(amount);
                flyer.SetActive(false);
                tr.localScale = fullScale;
                tr.localPosition = startLocal;
            });
        }
        // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: END

        /// <summary>비행체를 아트 배치 위치에서 TxtAmountOutline 까지 포물선(DOLocalJump)으로 날린 뒤,
        /// 도착 순간 비행체 숨김 + 수 갱신 + 그린 강조 + 펀치 + FXReward 재생 (매 도착마다 — confirmed).</summary>
        private IEnumerator FlyAndApply(GameObject flyer, int newAmount)
        {
            if (flyer != null && _txtAmountOutline != null)
            {
                if (!flyer.activeSelf) flyer.SetActive(true);
                PlayFxOnce(flyer); // 비행 중 파티클 트레일

                Transform flyTr = flyer.transform;
                Vector3 end = ResolveLocalPoint(flyTr.parent as RectTransform, (RectTransform)_txtAmountOutline.transform);
                end.z = flyTr.localPosition.z;

                Tween jump = flyTr.DOLocalJump(end, FlyJumpPower, 1, FlyDurationSeconds)
                    .SetEase(Ease.InOutSine)
                    .SetUpdate(true);
                yield return jump.WaitForCompletion();
                flyer.SetActive(false);
            }

            ApplyAmount(newAmount);
            yield return new WaitForSecondsRealtime(StepHoldSeconds);
        }

        /// <summary>도착 순간의 수 연산 반영 — 카운트업 + #6BFF8F + GreenOutline 머티리얼 + 펀치 + FXReward.
        /// [2026-06-12] 한 번에 교체가 아니라 이전 값→새 값으로 순차 증가(롤링 카운터) — 사용자 지시.</summary>
        private void ApplyAmount(int amount)
        {
            _countTween?.Kill();
            int from = _displayedAmount;
            _displayedAmount = amount;
            if (from <= 0 || from >= amount)
            {
                // 첫 표시(빈 값→1) 또는 비증가 보정 — 즉시 세팅.
                SetAmountText($"+{amount}");
            }
            else
            {
                int rolling = from;
                _countTween = DOTween.To(() => rolling, v => { rolling = v; SetAmountText($"+{v}"); },
                        amount, CountUpSeconds)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true);
            }
            if (_txtAmount != null) _txtAmount.color = GainColor;
            if (_txtAmountOutline != null) _txtAmountOutline.color = GainColor;
            if (_greenOutlineMat != null && _txtAmountOutline != null)
                _txtAmountOutline.fontSharedMaterial = _greenOutlineMat;

            // 도착 임팩트 — 카운터 가벼운 펀치 스케일. (과하면 아래 2줄 주석)
            if (_txtAmountOutline != null)
            {
                _punchTween?.Kill(true);
                _punchTween = _txtAmountOutline.transform.DOPunchScale(new Vector3(0.2f, 0.2f, 0f), 0.25f, 7, 0.7f).SetUpdate(true);
            }

            PlayFxOnce(_fxReward);
        }

        private void SetBaseAmountText(int amount)
        {
            // ROLLBACK_WS_BASE_AMOUNT_PREFAB_STYLE_20260618:
            // Initial +1 must keep the prefab-authored TxtAmount/TxtAmountOutline style.
            // The green gain color/material is applied only when a multiplier actually lands.
            _displayedAmount = amount;
            SetAmountText($"+{amount}");
        }

        private void StartRewardRootPulse()
        {
            if (_fxWinningStreakReward == null) return;

            // ROLLBACK_WS_REWARD_ROOT_PULSE_20260618:
            // While the coefficient popup is active, pulse the visual root between 1.0 and
            // 1.1 scale. This is presentation-only and does not affect point calculation.
            _rewardRootPulseTween?.Kill(false);
            _fxWinningStreakReward.localScale = _fxWinningStreakRewardBaseScale;
            _rewardRootPulseTween = _fxWinningStreakReward
                .DOScale(_fxWinningStreakRewardBaseScale * 1.1f, 0.35f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        private void StopRewardRootPulse()
        {
            _rewardRootPulseTween?.Kill(false);
            _rewardRootPulseTween = null;
            if (_fxWinningStreakReward != null)
                _fxWinningStreakReward.localScale = _fxWinningStreakRewardBaseScale;
        }

        private void ApplyFxBadgeSprite(DifficultyPurpose difficulty)
        {
            if (_fxBadgeImage == null) return;

            string key = difficulty switch
            {
                DifficultyPurpose.SuperHard => Const.SPR_BADGEX5,
                DifficultyPurpose.Hard      => Const.SPR_BADGEX3,
                _                           => null
            };
            if (string.IsNullOrEmpty(key)) return;

            Sprite sprite = ResourceManager.HasInstance
                ? ResourceManager.Instance.UISpriteOr(key, null)
                : null;

            if (sprite == null)
            {
                Debug.LogWarning($"[PopupWinningStreakReward] badge sprite not found in atlas_ui: {key}");
                return;
            }

            _fxBadgeImage.sprite = sprite;
            _fxBadgeImage.enabled = true;
            _fxBadgeImage.color = Color.white;
        }

        private void SetAmountText(string text)
        {
            if (_txtAmountOutline != null) _txtAmountOutline.text = text;
            if (_txtAmount != null) _txtAmount.text = text;
        }

        /// <summary>비활성 FX 오브젝트 활성화 + 하위 ParticleSystem 전부 재시작 Play.</summary>
        private static void PlayFxOnce(GameObject fx)
        {
            if (fx == null) return;
            if (!fx.activeSelf) fx.SetActive(true);
            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        /// <summary>ParticleLight 전용 재생 — 활성화 후 모든 자식 ParticleSystem 1회 Play, 자연 종료(IsAlive(true)=false)
        /// 시점까지 활성 유지, 종료 후 SetActive(false). 2026-06-19 추가 지시: 재생 도중 강제 비활성화·중단 금지.</summary>
        private void PlayParticleLightAndAutoDisable()
        {
            if (_particleLight == null) return;
            if (!_particleLight.activeSelf) _particleLight.SetActive(true);
            var systems = _particleLight.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                systems[i].Clear(true);
                systems[i].Play(true);
            }
            StartCoroutine(WaitAndDisableParticleLight(systems));
        }

        /// <summary>모든 ParticleSystem 의 IsAlive(true) 가 false 가 될 때까지 매 프레임 폴링한 뒤 SetActive(false).
        /// Stop/Clear 호출하지 않음 — 사용자 지시('재생 도중 강제 비활성화·중단 금지').</summary>
        private IEnumerator WaitAndDisableParticleLight(ParticleSystem[] systems)
        {
            while (true)
            {
                bool anyAlive = false;
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] != null && systems[i].IsAlive(true))
                    {
                        anyAlive = true;
                        break;
                    }
                }
                if (!anyAlive) break;
                yield return null;
            }
            if (_particleLight != null) _particleLight.SetActive(false);
        }

        /// <summary>target 의 화면 위치를 parentRt 로컬 좌표로 변환 (포물선 도착점).</summary>
        private static Vector3 ResolveLocalPoint(RectTransform parentRt, RectTransform target)
        {
            if (parentRt == null || target == null) return Vector3.zero;
            Canvas canvas = parentRt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, target.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screen, cam, out Vector2 local))
                return new Vector3(local.x, local.y, 0f);
            return Vector3.zero;
        }

        private void OnDestroy()
        {
            // 씬 전환 등 외부 파괴 시에도 대기측이 영원히 기다리지 않게.
            StopRewardRootPulse();
            _punchTween?.Kill();
            _countTween?.Kill();
            IsShowing = false;
            IsFinished = true;
        }

        // ── 이름 기반 자식 탐색 — 아트 prefab 바이너리라 SerializeField 와이어링 불가 ──

        private static Transform FindDirectChild(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name == name) return root.GetChild(i);
            return null;
        }

        private static GameObject FindDirectChildGameObject(Transform root, string name)
        {
            Transform t = FindDirectChild(root, name);
            return t != null ? t.gameObject : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindDeepGameObject(Transform root, string name)
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.gameObject : null;
        }

        private static T FindDeepComponent<T>(Transform root, string name) where T : Component
        {
            Transform t = FindDeep(root, name);
            return t != null ? t.GetComponent<T>() : null;
        }
    }
}
