using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

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
        private const float IntroHoldSeconds = 0.6f;   // 등장 → 첫 비행까지
        private const float StepHoldSeconds  = 0.45f;  // 도착(수 연산) → 다음 비행까지 (카운트업 완료 대기 포함)
        // ROLLBACK_WS_COEFF_OVERLAP_TIMELINE_20260615: 연출 단축 — 1.1f → 0.6f (마지막 카운트업 0.35s 커버 + 짧은 hold).
        private const float OutroHoldSeconds = 0.6f;    // 마지막 연산 → 자동 닫힘까지
        private const float FlyDurationSeconds = 0.5f; // 포물선 비행 시간
        private const float FlyJumpPower = 150f;       // 포물선 정점 높이 (로컬/px)
        private const float CountUpSeconds = 0.35f;    // 수 연산 카운트업(이전 값→새 값 순차 증가) 시간
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
        private TextMeshProUGUI _txtGauge;
        private TextMeshProUGUI _txtGaugeOutline;
        private Material _greenOutlineMat;
        private Tween _punchTween;
        private Tween _countTween;
        private int _displayedAmount;   // 현재 카운터 표시 값 — 카운트업 시작점

        /// <summary>프리팹 스폰 + 시퀀스 시작. 프리팹/부모 미존재 시 null (호출측은 null 이면 대기 없이 진행).
        /// diffMult/streakMult 는 1 이상으로 정규화. gainedPoints 는 최종 보정값(0 이면 보정 생략).</summary>
        public static PopupWinningStreakReward Play(int diffMult, int streakMult, int gainedPoints, bool showBadge)
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
            var ctrl = go.AddComponent<PopupWinningStreakReward>();
            ctrl.WireRefs();
            ctrl.StartCoroutine(ctrl.RunSequence(
                Mathf.Max(1, diffMult), Mathf.Max(1, streakMult), gainedPoints, showBadge));
            return ctrl;
        }

        private void WireRefs()
        {
            _txtAmountOutline = FindDeepComponent<TextMeshProUGUI>(transform, "TxtAmountOutline");
            _txtAmount        = FindDeepComponent<TextMeshProUGUI>(transform, "TxtAmount");
            _particleLight    = FindDeepGameObject(transform, "ParticleLight");
            _fxReward         = FindDeepGameObject(transform, "FXReward");

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
        }

        private IEnumerator RunSequence(int diffMult, int streakMult, int gainedPoints, bool showBadge)
        {
            IsShowing = true;
            _greenOutlineMat = Resources.Load<Material>(GreenOutlineMaterialPath);

            // 등장 — 카운터 빈 값, 비행체들은 출발 전 숨김 (FXBadge/FXMultiple 은 prefab 기본도 off).
            SetAmountText(string.Empty);
            bool flyMultiple = streakMult > 1;
            if (_fxBadge != null) _fxBadge.SetActive(false);
            if (_fxMultiple != null)
            {
                _fxMultiple.SetActive(false);
                string multText = $"x{streakMult}";
                if (_txtGaugeOutline != null) _txtGaugeOutline.text = multText;
                if (_txtGauge != null) _txtGauge.text = multText;
            }
            PlayFxOnce(_particleLight);

            yield return new WaitForSecondsRealtime(IntroHoldSeconds);

            // 1) 기본 포인트 — FXItem 비행 → "1".
            int amount = 1;
            yield return FlyAndApply(_fxItemFly, amount);

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
