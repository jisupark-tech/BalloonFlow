using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Winning Streak 'Info' 팝업. PopupWinningStreak 에서 BtnInfo 클릭 시 진입.
    /// PopupCommonFrame 의 BtnExit 만 listener 등록.
    /// _frame 이 prefab 단에서 미할당이면 silent skip — 런타임 NPE 회피 (BtnInfo 미배치 시나리오 동일).
    /// 컨텐츠/디자인은 prefab 단의 SerializeField 로 들어옴.
    ///
    /// [자식 등장 연출]
    /// OpenUI 시 _popSequenceTargets 에 명시 할당된 자식 오브젝트들을
    /// (디자이너 의도 순서: ImageTxt → Info1 → ImageArrow1 → Info2 → ImageArrow2 → Info3 → Info4 → TextClose)
    /// localScale 0 → 1.1 → 1 OutQuad/InQuad 단계로 순차 팝업.
    /// 이름 기반 Find 가 아닌 prefab 단 명시 할당 방식 — 자식 이름/계층 변경에 내성.
    ///
    /// _btnArea: 배경 클릭 닫기, 등장 연출 종료 후에만 활성.
    /// </summary>
    public class PopupWinningStreakInfo : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[BtnArea — 배경 클릭 닫기]")]
        [Tooltip("팝업 전체 영역 뒤의 투명 버튼. 클릭 시 닫기. 등장 연출 종료 후에만 활성화")]
        [SerializeField] private Button _btnArea;

        [Header("[자식 순차 팝업 — 대상]")]
        [Tooltip("등장 순서대로 명시 할당. ImageTxt → Info1 → ImageArrow1 → Info2 → ImageArrow2 → Info3 → Info4 → TextClose")]
        [SerializeField] private Transform[] _popSequenceTargets;

        [Header("[자식 순차 팝업 — 튜닝]")]
        [Tooltip("Scale 0 → 1.1 구간 지속 시간 (초)")]
        [SerializeField] private float _popOvershootDuration = 0.18f;
        [Tooltip("Scale 1.1 → 1 구간 지속 시간 (초)")]
        [SerializeField] private float _popSettleDuration = 0.10f;
        [Tooltip("다음 오브젝트가 등장하기까지의 간격 (초)")]
        [SerializeField] private float _popStagger = 0.08f;
        [Tooltip("Time.timeScale 영향을 받지 않게 함. 일시정지 상태에서도 동작하려면 true 권장")]
        [SerializeField] private bool _popIgnoreTimeScale = true;

        private Sequence _popSequence;

        // ROLLBACK_WS_INTRO_SCROLL_THEN_INFO_20260619: 닫힐 때 1회 콜백 — 인트로 플로우에서 이 Info 를 닫으면
        //   동반 PopupWinningStreak 도 함께 닫아 로비로 복귀시키기 위함. 일반 진입(Streak BtnInfo)에선 미설정.
        private System.Action _onCloseCallback;
        public void SetCloseCallback(System.Action onClose) => _onCloseCallback = onClose;

        public override void CloseUI()
        {
            base.CloseUI();
            if (_onCloseCallback != null)
            {
                var cb = _onCloseCallback;
                _onCloseCallback = null;
                cb.Invoke();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(() => CloseUI());
            if (_btnArea != null)
                _btnArea.onClick.AddListener(() => CloseUI());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            KillPopSequence();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnArea != null)
                _btnArea.onClick.RemoveAllListeners();
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                // ROLLBACK_WS_INFO_TITLE_PURPLE_OUTLINE_20260624:
                // Keep Winning Streak Info title outline consistent with the main Winning Streak popup.
                Material purpleOutline = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_PURPLE_OUTLINE);
                if (purpleOutline != null)
                    _frame.OverrideTitleOutlineAllDifficultyMaterials(purpleOutline);
                _frame.SetTitle("Winning Streak Info");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
            PlayChildPopSequence();
        }

        /// <summary>
        /// _popSequenceTargets 에 할당된 자식들을 순서대로 Scale 0 → 1.1 → 1 OutQuad/InQuad 로 팝업.
        /// Sequence 구성 전 모든 타겟의 localScale 을 동기적으로 0 으로 세팅 — 첫 프레임에 나머지 타겟이
        /// scale 1 로 잠깐 보이는 깜빡임을 방지. null 엔트리는 skip.
        /// 디자이너가 _popSequenceTargets 를 미할당하면 silent return.
        /// </summary>
        private void PlayChildPopSequence()
        {
            KillPopSequence();

            if (_btnArea != null) _btnArea.interactable = false;

            if (_popSequenceTargets == null || _popSequenceTargets.Length == 0)
            {
                if (_btnArea != null) _btnArea.interactable = true;
                return;
            }

            for (int i = 0; i < _popSequenceTargets.Length; i++)
            {
                var t = _popSequenceTargets[i];
                if (t != null) t.localScale = Vector3.zero;
            }

            _popSequence = DOTween.Sequence().SetUpdate(_popIgnoreTimeScale);
            int count = _popSequenceTargets.Length;
            for (int i = 0; i < count; i++)
            {
                var t = _popSequenceTargets[i];
                if (t == null) continue;

                _popSequence.Append(t.DOScale(1.1f, _popOvershootDuration).SetEase(Ease.OutQuad));
                _popSequence.Append(t.DOScale(1f, _popSettleDuration).SetEase(Ease.InQuad));
                if (i < count - 1)
                    _popSequence.AppendInterval(_popStagger);
            }

            _popSequence.OnComplete(() => { if (_btnArea != null) _btnArea.interactable = true; });
        }

        private void KillPopSequence()
        {
            if (_popSequence != null)
            {
                _popSequence.Kill();
                _popSequence = null;
            }
        }

        private void OnDisable()
        {
            KillPopSequence();

            if (_btnArea != null) _btnArea.interactable = false;

            // 재오픈 시 자식이 scale 0 으로 남아 안 보이는 회귀 방지 — 모두 1 로 복구.
            if (_popSequenceTargets == null) return;
            for (int i = 0; i < _popSequenceTargets.Length; i++)
            {
                var t = _popSequenceTargets[i];
                if (t != null) t.localScale = Vector3.one;
            }
        }
    }
}
