using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Winning Streak 안내 팝업. PopupCommonFrame 사용.
    /// 컨텐츠/디자인은 prefab 단의 SerializeField 로 들어옴.
    /// </summary>
    /// <remarks>Project-wide convention: flat 'namespace BalloonFlow' — do not nest. AimedPuzzle.* 형태는 본 프로젝트 컨벤션이 아님.</remarks>
    /// <remarks>Not a singleton — UIManager가 lifecycle 관리.</remarks>
    public class PopupWinningStreak : UIBase
    {
        private const int SLOT_COUNT = 25;

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Tooltip("Info 팝업 진입 버튼 — 클릭 시 PopupWinningStreakInfo 오픈. 미할당 시 silent skip.")]
        [SerializeField] private Button _btnInfo;

        [Header("[Key Blaze Slots]")]
        [Tooltip("SlotKeyBlaze 25개가 자식으로 생성될 부모 Transform. ScrollRect의 Content. 미할당 시 silent skip + warning.")]
        [SerializeField] private RectTransform _keyBlazeContents;
        [Tooltip("인스턴시에이트할 SlotKeyBlaze 프리팹. TextNumber/TextNumberOutline 라는 이름의 TMP_Text 자식을 포함해야 함.")]
        [SerializeField] private GameObject _slotKeyBlazePrefab;
        [Tooltip("스크롤 방향 '아래→위' 시작 위치(맨 아래=1번 노출)를 위해 verticalNormalizedPosition을 0으로 설정. 미할당 시 skip.")]
        [SerializeField] private ScrollRect _scrollRect;

        [Header("[Slot Click Info — Mask 바깥 표시]")]
        [Tooltip("WinningStreakClickInfo 가 reparent 될 부모. 반드시 ScrollRect/Mask 계층 바깥 (popup root 직속 권장). 미할당 시 silent skip.")]
        [SerializeField] private RectTransform _clickInfoOverlayParent;
        [Tooltip("prefab 단에서 비활성 상태로 미리 instantiate 된 WinningStreakClickInfo 인스턴스 (Resources/UI/UIAssets/WinningStreakClickInfo.prefab). _clickInfoOverlayParent 자식으로 배치 권장. 미할당 시 silent skip.")]
        [SerializeField] private GameObject _clickInfo;
        [Tooltip("슬롯의 top edge 위로 띄울 거리(px). pivot 차이 + 여유분 감안.")]
        [SerializeField] private float _clickInfoYOffset = 200f;
        [Tooltip("외부 클릭 시 click-info 를 닫는 full-screen 투명 Button. Image.color.a=0, raycastTarget=true. _clickInfo 보다 sibling 순서 '뒤(아래)' 에 배치되어야 click-info > dismiss 클릭 우선순위가 유지됨.")]
        [SerializeField] private Button _btnDismissArea;

        private bool _slotsBuilt = false;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(() => CloseUI());
            if (_btnInfo != null)
            {
                _btnInfo.onClick.RemoveAllListeners();
                _btnInfo.onClick.AddListener(() =>
                {
                    if (UIManager.HasInstance)
                        UIManager.Instance.OpenUI<PopupWinningStreakInfo>(Const.POPUP_WINNING_STREAK_INFO);
                });
            }
            if (_btnDismissArea != null)
            {
                _btnDismissArea.onClick.RemoveAllListeners();
                _btnDismissArea.onClick.AddListener(HideClickInfo);
            }
            // 초기 비활성 — prefab 단 상태에 의존하지 않고 코드에서 명시 보정.
            if (_clickInfo != null) _clickInfo.SetActive(false);
            if (_btnDismissArea != null) _btnDismissArea.gameObject.SetActive(false);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnInfo != null)
                _btnInfo.onClick.RemoveAllListeners();
            if (_btnDismissArea != null)
                _btnDismissArea.onClick.RemoveAllListeners();
            // 슬롯은 함께 destroy 되지만 listener 누수 방어 차원으로 명시 정리.
            if (_keyBlazeContents != null)
            {
                //var slotComps = _keyBlazeContents.GetComponentsInChildren<PopupWinningStreak>(true);
                //for (int i = 0; i < slotComps.Length; i++)
                //{
                //    if (slotComps[i] != null && slotComps[i].BtnReward != null)
                //        slotComps[i].BtnReward.onClick.RemoveAllListeners();
                //}
            }
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Winning Streak");
                _frame.ShowExitButton(true);
            }
            BuildKeyBlazeSlots();
            // 이전 상태 잔존 방지 — Open 마다 click-info 는 닫힌 상태에서 시작.
            HideClickInfo();
            base.OpenUI();
        }

        private void BuildKeyBlazeSlots()
        {
            if (_slotsBuilt) return;

            if (_keyBlazeContents == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _keyBlazeContents 미할당 — Editor에서 SerializeField 할당 누락. Inspector에서 ScrollRect Content를 연결해 주세요. (이번 빌드는 skip, 다음 OpenUI 재시도 가능)");
                return;
            }
            if (_slotKeyBlazePrefab == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _slotKeyBlazePrefab 미할당 — Editor에서 SerializeField 할당 누락. Inspector에서 SlotKeyBlaze 프리팹을 연결해 주세요. (이번 빌드는 skip, 다음 OpenUI 재시도 가능)");
                return;
            }
            if (_keyBlazeContents.childCount > 0)
            {
                Debug.LogWarning("[PopupWinningStreak] KeyBlazeContents already populated; skip.");
                _slotsBuilt = true;
                return;
            }

            // 25→1 역순 루프: siblingIndex 0이 top이므로 25를 먼저 생성 → top=25, bottom=1
            //for (int i = SLOT_COUNT; i >= 1; i--)
            //{
            //    var slot = Instantiate(_slotKeyBlazePrefab, _keyBlazeContents);
            //    slot.name = $"SlotKeyBlaze_{i:D2}";
            //    SetSlotNumber(slot, i);

            //    var slotComp = slot.GetComponent<PopupWinningStreak>();
            //    if (slotComp == null)
            //    {
            //        Debug.LogWarning($"[PopupWinningStreak] Slot {i}: SlotKeyBlaze 컴포넌트 없음 — 구버전 prefab 가능성. BtnReward listener 등록 skip.");
            //        continue;
            //    }
            //    if (slotComp.BtnReward == null) continue;

            //    var slotRt = slot.GetComponent<RectTransform>();
            //    slotComp.BtnReward.onClick.RemoveAllListeners();
            //    slotComp.BtnReward.onClick.AddListener(() => ShowClickInfoForSlot(slotRt));
            //}

            // verticalNormalizedPosition=0: 시작 위치 맨 아래(=1번 노출)
            if (_scrollRect == null)
                Debug.LogWarning("[PopupWinningStreak] _scrollRect 미할당 — Editor에서 SerializeField 할당 누락. Inspector에서 ScrollRect를 연결해 주세요. (슬롯은 빌드됐으나 시작 스크롤 위치 보정은 skip)");
            else
                _scrollRect.verticalNormalizedPosition = 0f;

            _slotsBuilt = true;
        }

        private void SetSlotNumber(GameObject slot, int number)
        {
            string s = number.ToString();
            var tmps = slot.GetComponentsInChildren<TMP_Text>(true);
            bool setMain = false, setOutline = false;
            foreach (var t in tmps)
            {
                if (t.name == "TextNumber") { t.text = s; setMain = true; }
                else if (t.name == "TextNumberOutline") { t.text = s; setOutline = true; }
            }
            if (!setMain || !setOutline)
                Debug.LogWarning($"[PopupWinningStreak] Slot {number}: TextNumber/Outline 누락 — main={setMain}, outline={setOutline}");
        }

        /// <summary>
        /// 슬롯 위쪽에 WinningStreakClickInfo 를 표시. Scroll Content 의 Mask 영향을 받지 않도록
        /// _clickInfoOverlayParent 로 reparent 후, 슬롯 top center 의 world 좌표를
        /// overlay 부모의 local 좌표로 환산해 anchoredPosition 에 적용한다.
        /// SerializeField 가 하나라도 미할당이면 silent skip — 기존 popup 컨벤션과 동일.
        /// </summary>
        private void ShowClickInfoForSlot(RectTransform slotRt)
        {
            if (slotRt == null) return;
            if (_clickInfoOverlayParent == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _clickInfoOverlayParent 미할당 — click-info 표시 skip. Inspector 에서 Mask 바깥 부모를 연결해 주세요.");
                return;
            }
            if (_clickInfo == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _clickInfo 미할당 — click-info 표시 skip. Inspector 에서 WinningStreakClickInfo 인스턴스를 연결해 주세요.");
                return;
            }
            if (_btnDismissArea == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _btnDismissArea 미할당 — click-info 표시 skip. 외부 클릭 닫기 보장 안 되므로 표시 자체를 중단.");
                return;
            }

            _clickInfo.transform.SetParent(_clickInfoOverlayParent, worldPositionStays: false);

            // 슬롯 top center: pivot 보정 포함. pivot.y=0.5 → top 까지 height*0.5 만큼 위.
            Vector3 worldTop = slotRt.TransformPoint(new Vector3(0f, slotRt.rect.height * (1f - slotRt.pivot.y), 0f));

            // ScreenSpaceOverlay 면 camera=null, ScreenSpaceCamera/WorldSpace 면 worldCamera 전달.
            var canvas = _clickInfoOverlayParent.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            Vector2 screenPt = RectTransformUtility.WorldToScreenPoint(cam, worldTop);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_clickInfoOverlayParent, screenPt, cam, out Vector2 localPt))
            {
                var ciRt = _clickInfo.GetComponent<RectTransform>();
                if (ciRt != null)
                    ciRt.anchoredPosition = localPt + new Vector2(0f, _clickInfoYOffset);
            }

            _clickInfo.SetActive(true);
            _btnDismissArea.gameObject.SetActive(true);
        }

        private void HideClickInfo()
        {
            if (_clickInfo != null) _clickInfo.SetActive(false);
            if (_btnDismissArea != null) _btnDismissArea.gameObject.SetActive(false);
        }
    }
}
