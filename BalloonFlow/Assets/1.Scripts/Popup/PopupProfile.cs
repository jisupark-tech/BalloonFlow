using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 프로필 팝업. 사용자 아이콘/프레임 선택. 클릭 즉시 UserData 에 저장.
    ///
    /// 구조:
    ///  - _profileAssets: 아이콘/프레임 sprite 카탈로그 (ScriptableObject).
    ///  - _iconSlotContainer: 자식으로 슬롯 button 들이 동적 spawn 됨. _slotPrefab 1개를 _profileAssets.IconCount 만큼 instantiate.
    ///  - _frameSlotContainer: 위와 동일, frames 용.
    ///  - _previewIcon / _previewFrame: 현재 선택 미리보기 Image. 클릭 즉시 갱신.
    ///
    /// 슬롯 prefab 요구사항:
    ///  - Button 컴포넌트
    ///  - 자식 Image (sprite 표시용) — name="Image" 또는 첫 GetComponentInChildren&lt;Image&gt; 결과
    ///  - 선택 강조용 자식 GameObject (name="Selected", 선택 안 됨이면 SetActive(false)) — optional
    ///
    /// 데이터/리소스 미준비 상태에서는 _profileAssets 가 null 이거나 빈 배열이라 spawn 0개 → UI 표면만 노출.
    /// </summary>
    public class PopupProfile : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Profile Assets]")]
        [SerializeField] private ProfileAssets _profileAssets;

        [Header("[Slot Spawn]")]
        [Tooltip("Icon/Frame 슬롯 공용 prefab. Button + 자식 Image + (optional) Selected 자식.")]
        [SerializeField] private GameObject _slotPrefab;
        [SerializeField] private Transform _iconSlotContainer;
        [SerializeField] private Transform _frameSlotContainer;

        [Header("[Preview]")]
        [SerializeField] private Image _previewIcon;
        [SerializeField] private Image _previewFrame;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;

        private readonly List<Button> _iconSlots = new List<Button>();
        private readonly List<Button> _frameSlots = new List<Button>();
        private bool _slotsBuilt;

        protected override void Awake()
        {
            base.Awake();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnExitClickedSelf);
        }

        private void OnExitClickedSelf()
        {
            CloseUI();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveListener(OnExitClickedSelf);
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle(LocalizationService.Get("popupprofile.txttitle"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.ShowExitButton(true);
            }

            base.OpenUI();

            // 애니메이션 사용 시 base.OpenUI 가 interactable=false 로 시작 → ExitButton 클릭 안 됨.
            // 즉시 클릭 가능하도록 강제 (PopupSettings 와 동일 패턴).
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
            if (_frame != null && _frame.BtnExit != null)
            {
                _frame.BtnExit.interactable = true;
                _frame.BtnExit.gameObject.SetActive(true);
            }

            BuildSlotsIfNeeded();
            RefreshPreviewAndHighlight();
        }

        // ────────────────────────────────────────────────────────
        // Slot spawn (1회) + 매 OpenUI 시 selection highlight refresh.
        // ────────────────────────────────────────────────────────

        private void BuildSlotsIfNeeded()
        {
            if (_slotsBuilt) return;
            if (_profileAssets == null || _slotPrefab == null)
            {
                // 리소스 미준비 — 스켈레톤 UI 만 노출.
                _slotsBuilt = true;
                return;
            }

            SpawnSlots(_profileAssets.IconCount, _iconSlotContainer, _iconSlots,
                       index => _profileAssets.GetIcon(index),
                       OnIconSlotClicked);
            SpawnSlots(_profileAssets.FrameCount, _frameSlotContainer, _frameSlots,
                       index => _profileAssets.GetFrame(index),
                       OnFrameSlotClicked);

            _slotsBuilt = true;
        }

        private void SpawnSlots(int count, Transform parent, List<Button> outList,
                                System.Func<int, Sprite> spriteAt, System.Action<int> onClick)
        {
            if (parent == null) return;
            for (int i = 0; i < count; i++)
            {
                int slotIndex = i; // closure capture
                var go = Instantiate(_slotPrefab, parent);
                go.SetActive(true);

                UIButtonClickGuard.AttachToHierarchy(go);

                var img = go.GetComponentInChildren<Image>(true);
                if (img != null) img.sprite = spriteAt(slotIndex);

                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.GetComponentInChildren<Button>(true);
                if (btn != null) btn.onClick.AddListener(() => onClick(slotIndex));
                outList.Add(btn);
            }
        }

        private void OnIconSlotClicked(int iconIndex)
        {
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                UserDataService.Instance.SetProfileIconNumber(iconIndex);
            RefreshPreviewAndHighlight();
        }

        private void OnFrameSlotClicked(int frameIndex)
        {
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                UserDataService.Instance.SetProfileFrameNumber(frameIndex);
            RefreshPreviewAndHighlight();
        }

        private void RefreshPreviewAndHighlight()
        {
            int curIcon = 0, curFrame = 0;
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady
                && UserDataService.Instance.CurrentUser != null)
            {
                curIcon  = UserDataService.Instance.CurrentUser.profileIconNumber;
                curFrame = UserDataService.Instance.CurrentUser.profileFrameNumber;
            }

            if (_previewIcon != null && _profileAssets != null)
            {
                var sp = _profileAssets.GetIcon(curIcon);
                _previewIcon.sprite = sp;
                _previewIcon.enabled = sp != null;
            }
            if (_previewFrame != null && _profileAssets != null)
            {
                var sp = _profileAssets.GetFrame(curFrame);
                _previewFrame.sprite = sp;
                _previewFrame.enabled = sp != null;
            }

            UpdateSelectedHighlight(_iconSlots, curIcon);
            UpdateSelectedHighlight(_frameSlots, curFrame);
        }

        // 슬롯 prefab 자식 중 name="Selected" GameObject 가 있으면 현재 선택 슬롯만 SetActive(true).
        private static void UpdateSelectedHighlight(List<Button> slots, int selectedIndex)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var btn = slots[i];
                if (btn == null) continue;
                var sel = btn.transform.Find("Selected");
                if (sel != null) sel.gameObject.SetActive(i == selectedIndex);
            }
        }
    }
}
