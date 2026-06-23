using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 계정 삭제(탈퇴) 확인 전용 팝업. Horizontal 레이아웃 — Green=Stay(취소), Red=Delete(삭제).
    /// 기존 PopupError.ShowDeleteAccountConfirm 의 동작을 전용 프리팹(Popup/PopupDeleteAccount)으로 분리.
    /// UISetting.OnDeleteAccountClicked 가 OpenUI&lt;PopupDeleteAccount&gt; 후 Show(onDelete) 호출.
    /// onDelete 는 Red 버튼 클릭 시에만 발화(닫은 뒤 호출). Green/X 는 닫기만(탈퇴 안 함).
    ///
    /// ROLLBACK_POPUP_DELETE_ACCOUNT_DEDICATED_20260623:
    /// 전용 팝업 폐기 시 이 스크립트 제거 + UISetting 을 PopupError.ShowDeleteAccountConfirm 로 환원.
    /// ※ 활성화: 프리팹 Popup/PopupDeleteAccount 에 이 컴포넌트 부착 + PopupCommonFrame(_frame)·설명텍스트(_txtDescription) 할당.
    /// </summary>
    public class PopupDeleteAccount : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        // 설명 3개(Description / Description (1) / Description (2)). 각각 deleteaccount1/2/3 키.
        // 인스펙터 미할당이어도 자식에서 이름(TxtDescription*/Description*)으로 자동 매칭 시도.
        [SerializeField] private TMP_Text _txtDescription1;
        [SerializeField] private TMP_Text _txtDescription2;
        [SerializeField] private TMP_Text _txtDescription3;
        [SerializeField] private TMP_Text _txtDescription; // (선택) 단일 설명 프리팹 호환용
        [SerializeField] private Image _imgIcon;
        [SerializeField] private Sprite _sprIconCancel;

        private bool _descriptionsResolved;

        /// <summary>탈퇴 확인 팝업 표시. Red(삭제) 확정 시에만 onDelete 발화.</summary>
        public void Show(System.Action onDelete)
        {
            if (_frame != null)
            {
                _frame.SetTitle(LocalizationService.Get("popup.txttitle.deleteaccount"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText(LocalizationService.Get("ui.common.stay"));
                _frame.SetHorizRedText(LocalizationService.Get("ui.common.delete"));
                _frame.ShowExitButton(true);
            }

            // ── 설명 3개 세팅 ──
            ResolveDescriptionsIfNeeded();
            SetDescription(_txtDescription1, "popup.txtdescription.deleteaccount1");
            SetDescription(_txtDescription2, "popup.txtdescription.deleteaccount2");
            SetDescription(_txtDescription3, "popup.txtdescription.deleteaccount3");
            // 단일 설명 프리팹 호환: 3개 모두 없고 _txtDescription 만 있으면 통합 설명.
            if (_txtDescription != null
                && _txtDescription1 == null && _txtDescription2 == null && _txtDescription3 == null)
                _txtDescription.text = LocalizationService.Get("popup.txtdescription.deleteaccount");

            if (_imgIcon != null)
            {
                if (_sprIconCancel != null)
                {
                    _imgIcon.sprite = _sprIconCancel;
                    _imgIcon.gameObject.SetActive(true);
                }
                else
                {
                    _imgIcon.gameObject.SetActive(false);
                }
            }

            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null)
                {
                    _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                    _frame.BtnHorizGreen.onClick.AddListener(CloseUI); // Stay = 닫기만
                }
                if (_frame.BtnHorizRed != null)
                {
                    _frame.BtnHorizRed.onClick.RemoveAllListeners();
                    _frame.BtnHorizRed.onClick.AddListener(() =>
                    {
                        CloseUI();
                        onDelete?.Invoke(); // Delete = 닫고 실제 탈퇴 콜백
                    });
                }
                if (_frame.BtnExit != null)
                {
                    _frame.BtnExit.onClick.RemoveAllListeners();
                    _frame.BtnExit.onClick.AddListener(CloseUI); // X = 닫기만(탈퇴 안 함)
                }
            }

            OpenUI();
        }

        private static void SetDescription(TMP_Text label, string key)
        {
            if (label != null) label.text = LocalizationService.Get(key);
        }

        /// <summary>인스펙터 미할당 시 자식에서 설명 텍스트를 이름순으로 자동 매칭(TxtDescription*/Description*).</summary>
        private void ResolveDescriptionsIfNeeded()
        {
            if (_descriptionsResolved) return;
            _descriptionsResolved = true;
            if (_txtDescription1 != null || _txtDescription2 != null || _txtDescription3 != null) return;

            var all = GetComponentsInChildren<TMP_Text>(true);
            var found = new System.Collections.Generic.List<TMP_Text>(4);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].gameObject.name;
                if (n.StartsWith("TxtDescription") || n.StartsWith("Description")) found.Add(all[i]);
            }
            if (found.Count > 0) _txtDescription1 = found[0];
            if (found.Count > 1) _txtDescription2 = found[1];
            if (found.Count > 2) _txtDescription3 = found[2];
        }
    }
}
