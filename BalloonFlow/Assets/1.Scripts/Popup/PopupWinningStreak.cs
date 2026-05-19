using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Winning Streak 안내 팝업. PopupCommonFrame 사용.
    /// 컨텐츠/디자인은 prefab 단의 SerializeField 로 들어옴.
    /// </summary>
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
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnInfo != null)
                _btnInfo.onClick.RemoveAllListeners();
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Winning Streak");
                _frame.ShowExitButton(true);
            }
            BuildKeyBlazeSlots();
            base.OpenUI();
        }

        private void BuildKeyBlazeSlots()
        {
            if (_slotsBuilt) return;
            _slotsBuilt = true;

            if (_keyBlazeContents == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _keyBlazeContents is null; skip slot build.");
                return;
            }
            if (_slotKeyBlazePrefab == null)
            {
                Debug.LogWarning("[PopupWinningStreak] _slotKeyBlazePrefab is null; skip slot build.");
                return;
            }
            if (_keyBlazeContents.childCount > 0)
            {
                Debug.LogWarning("[PopupWinningStreak] KeyBlazeContents already populated; skip.");
                return;
            }

            // 25→1 역순 루프: siblingIndex 0이 top이므로 25를 먼저 생성 → top=25, bottom=1
            for (int i = SLOT_COUNT; i >= 1; i--)
            {
                var slot = Instantiate(_slotKeyBlazePrefab, _keyBlazeContents);
                slot.name = $"SlotKeyBlaze_{i:D2}";
                SetSlotNumber(slot, i);
            }

            // verticalNormalizedPosition=0: 시작 위치 맨 아래(=1번 노출)
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 0f;
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
    }
}
