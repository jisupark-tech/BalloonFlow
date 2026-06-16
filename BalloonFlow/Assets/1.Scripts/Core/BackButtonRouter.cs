using UnityEngine;
using UnityEngine.InputSystem;

namespace BalloonFlow
{
    /// <summary>
    /// 안드로이드 하드웨어/제스처 백버튼 중앙 라우터 (UX플로우 §5-3-0 매트릭스, v1.2.33 PO 2026-05-26).
    ///
    /// 신규 Input System 에서 안드로이드 백버튼은 <c>Key.Escape</c> 로 매핑된다.
    /// 매 프레임 입력을 감지해 다음 우선순위로 라우팅한다:
    ///   1) 광고/결제/스플래시 등 SDK·OS 처리 구간 → 무시 (진동 X — 우리 앱이 입력 캡처 X)
    ///   2) 최상단 팝업(ConsumesBackButton=true)이 열려 있으면 → 팝업의 OnBackPressed (닫기/차단)
    ///   3) 팝업이 없으면 씬 컨텍스트:
    ///        - 로비(로비/상점/세팅 탭 = 같은 씬) → Quit Game 팝업
    ///        - 인게임 → HUDController.HandleInGameBack (부스터취소/온보딩세팅/Quit Level)
    ///        - 타이틀/맵메이커 → 무시
    ///
    /// 진동: 우리 앱이 입력을 처리한 모든 경우 O (동작/차단/무동작 포함), SDK·OS 처리 시 X.
    /// 효과음은 없음 (PO 명시). 디바운스 없음 (자연 입력 처리).
    /// 부트스트랩: SdkBootstrap 의 부트 오브젝트에 부착 (DontDestroyOnLoad).
    /// </summary>
    public class BackButtonRouter : Singleton<BackButtonRouter>
    {
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb[Key.Escape].wasPressedThisFrame) return;

            HandleBackPressed();
        }

        private void HandleBackPressed()
        {
            // 1) SDK·OS 처리 구간 — 무시 (진동 X)
            if (AdManager.HasInstance && AdManager.Instance.IsShowingAd) return;
            if (UIManager.HasInstance && UIManager.Instance.IsFading) return;

            // 2) 최상단 팝업 우선
            if (UIManager.HasInstance)
            {
                var top = UIManager.Instance.GetTopmostBackConsumingUI();
                if (top != null)
                {
                    var result = top.OnBackPressed();
                    if (result != UIBase.BackResult.NotHandled)
                    {
                        Haptic();
                        return;
                    }
                }
            }

            // 3) 씬 컨텍스트
            string scene = GameManager.HasInstance ? GameManager.Instance.CurrentScene : null;
            switch (scene)
            {
                case GameManager.SCENE_LOBBY:
                    ShowQuitGame();
                    Haptic();
                    break;

                case GameManager.SCENE_INGAME:
                    if (HUDController.HasInstance)
                    {
                        HUDController.Instance.HandleInGameBack();
                        Haptic();
                    }
                    break;

                // Title / MapMaker / 스플래시·로딩 → 무시 (진동 X)
                default:
                    break;
            }
        }

        /// <summary>로비 컨텍스트 Quit Game 확인 팝업 (앱 종료).</summary>
        private void ShowQuitGame()
        {
            if (!UIManager.HasInstance) return;
            // 이미 Quit Game 팝업이 떠 있으면 중복 오픈 방지 — 위 (2) 단계가 잡지 못하는 경우 가드.
            var popup = UIManager.Instance.OpenUI<PopupDescription>(Const.POPUP_DESCRIPTION);
            if (popup != null)
                popup.Show(LocalizationService.Get("popup.txttitle.quit"),
                    LocalizationService.Get("popup.txtdescription.quit"),
                    LocalizationService.Get("ui.common.quit"),
                    () => Application.Quit(),
                    exitClosesOnly: true);
        }

        private static void Haptic()
        {
            // SettingsManager.HapticOn 토글 OFF 면 VibrationManager 내부에서 무시됨.
            VibrationManager.VibrateDefault();
        }
    }
}
