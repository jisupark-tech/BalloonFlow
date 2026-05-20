using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 클리어 결과 팝업.
    /// PopupCommonFrame으로 프레임/난이도/버튼 관리.
    /// Single 레이아웃 (Next 1개 버튼).
    /// </summary>
    public class PopupResult : UIBase
    {
        #region Constants

        private const int MIN_COIN_COUNT = 20;
        private const int MAX_COIN_COUNT = 25;
        private const int SCORE_PER_COIN_STEP = 500;

        #endregion

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnNext;
        [SerializeField] private Button _btnHome;
        [SerializeField] private Button _btnExit;

        [Header("[난이도별 비주얼]")]
        [SerializeField] private Image _imageLight;
        [SerializeField] private Image _imageStage;
        [SerializeField] private Sprite _sprStageNormal;
        [SerializeField] private Sprite _sprStageHard;
        [SerializeField] private Sprite _sprStageSuperHard;

        [Header("[ImageBG — 난이도별 배경]")]
        [SerializeField] private Image _imageBG;
        [SerializeField] private Sprite _bgSpriteNormal;
        [SerializeField] private Sprite _bgSpriteHard;
        [SerializeField] private Sprite _bgSpriteSuperHard;

        [Header("[Hard Level Option — Hard/SuperHard 전용]")]
        [SerializeField] private GameObject _hardLevelOption;
        [SerializeField] private Image _iconSkull;
        [SerializeField] private Sprite _sprSkullHard;
        [SerializeField] private Sprite _sprSkullSuperHard;
        [SerializeField] private TMP_Text _txtHardLevel;
        [SerializeField] private TMP_Text _txtHardLevelOutline;
        [SerializeField] private Material _matHardLevelOutlineHard;
        [SerializeField] private Material _matHardLevelOutlineSuperHard;

        [Header("[난이도별 곱하기 라벨 — 표시용(내부 수치와 무관)]")]
        [SerializeField] private GameObject _multiplierLabel;
        [SerializeField] private TMP_Text _txtMultiplier;
        [SerializeField] private TMP_Text _txtMultiplierOutline;

        [Header("[HardOptionColor — Hard/SuperHard 전용]")]
        [SerializeField] private Image _imageHardOptionColor;
        [SerializeField] private Sprite _sprHardOptionHard;
        [SerializeField] private Sprite _sprHardOptionSuperHard;

        [Header("[Badge Image — Hard=x3 / SuperHard=x5 / Normal=비활성]")]
        [SerializeField] private Image _imageBadge;
        [SerializeField] private Sprite _sprBadgeX3;
        [SerializeField] private Sprite _sprBadgeX5;

        [Header("[코인 연출 — Gold HUD 위치]")]
        [SerializeField] private RectTransform _goldTarget;

        public Button NextButton => _btnNext != null ? _btnNext : (_frame != null ? _frame.BtnSingle : null);
        public Button RetryButton => null;
        public Button HomeButton => null;
        public RectTransform GoldTarget => _goldTarget;
        private Button FrameNextButton => _frame != null ? _frame.BtnSingle : null;

        public void ShowFail()
        {
            if (PopupManager.HasInstance)
                PopupManager.Instance.ShowPopup("popup_fail01", 50);
        }

        protected override void Awake()
        {
            base.Awake();

            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprHardOptionHard      = rm.UISpriteOr(Const.SPR_FRAMEHARD,      _sprHardOptionHard);
                _sprHardOptionSuperHard = rm.UISpriteOr(Const.SPR_FRAMESUPERHARD, _sprHardOptionSuperHard);
                _sprBadgeX3             = rm.UISpriteOr(Const.SPR_BADGEX3,        _sprBadgeX3);
                _sprBadgeX5             = rm.UISpriteOr(Const.SPR_BADGEX5,        _sprBadgeX5);
            }

            // 직접 할당 버튼 우선, 없으면 frame 버튼 fallback (CloseUI 후에도 listener 유지)
            AddNextListener(OnNextClicked);

            // ExitButton: 직접 할당 + frame 둘 다 와이어 (둘 중 보이는 쪽이 동작)
            if (_btnExit != null) _btnExit.onClick.AddListener(OnHomeClicked);
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnHomeClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnNext != null) _btnNext.onClick.RemoveAllListeners();
            if (_btnHome != null) _btnHome.onClick.RemoveAllListeners();
            if (_btnExit != null) _btnExit.onClick.RemoveAllListeners();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        public void SetGoldTarget(RectTransform target) { _goldTarget = target; }

        public void SetNextButtonListener(UnityAction listener)
        {
            ReplaceClickListener(_btnNext, listener);

            Button frameNext = FrameNextButton;
            if (frameNext != null && frameNext != _btnNext)
                ReplaceClickListener(frameNext, listener);
        }

        public void RemoveNextButtonListener(UnityAction listener)
        {
            RemoveClickListener(_btnNext, listener);

            Button frameNext = FrameNextButton;
            if (frameNext != null && frameNext != _btnNext)
                RemoveClickListener(frameNext, listener);
        }

        public void ShowWin(int score, DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            ShowWin(score, DifficultyToRewardStarCount(difficulty), difficulty);
        }

        public void ShowWin(int score, int starCount, DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            if (_frame != null)
            {
                _frame.ApplyDifficulty(difficulty);
                _frame.SetTitle("Level Clear!");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Next");
                // ExitButton은 직접 할당된 게 있으면 그걸 보이도록, 없으면 frame 것 표시
                _frame.ShowExitButton(_btnExit == null);
            }

            // 직접 할당된 ExitButton 강제 활성화 (다른 ShowXxx 호출에서 꺼졌을 가능성 대비)
            if (_btnExit != null)
            {
                _btnExit.gameObject.SetActive(true);
                _btnExit.interactable = true;
            }

            // Next(Single) 버튼 강제 활성화 (interactable 미설정/prefab 기본값 false 방어)
            if (_btnNext != null)
            {
                _btnNext.gameObject.SetActive(true);
                _btnNext.interactable = true;
            }
            Button frameNext = FrameNextButton;
            if (frameNext != null && frameNext != _btnNext)
            {
                frameNext.gameObject.SetActive(true);
                frameNext.interactable = true;
            }

            UpdateHardLevelOption(difficulty);
            ApplyBadge(difficulty);
            EnsureRewardVisible(starCount);
            ApplyDifficultyBackground(difficulty);

            OpenUI();

            // 애니메이션 상태와 무관하게 즉시 클릭 가능
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }

            TriggerCoinFly(score);
        }

        #region Button Handlers

        private void AddNextListener(UnityAction listener)
        {
            AddClickListener(_btnNext, listener);

            Button frameNext = FrameNextButton;
            if (frameNext != null && frameNext != _btnNext)
                AddClickListener(frameNext, listener);
        }

        private static void AddClickListener(Button button, UnityAction listener)
        {
            if (button == null || listener == null) return;
            button.onClick.AddListener(listener);
        }

        private static void ReplaceClickListener(Button button, UnityAction listener)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            if (listener != null)
                button.onClick.AddListener(listener);
        }

        private static void RemoveClickListener(Button button, UnityAction listener)
        {
            if (button == null || listener == null) return;
            button.onClick.RemoveListener(listener);
        }

        private void OnNextClicked()
        {
            CloseUI();

            if (GameManager.IsTestPlayMode)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();
                if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_MAPMAKER);
                return;
            }

            if (LifeManager.HasInstance && LifeManager.Instance.CurrentLives <= 0)
            {
                if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
                return;
            }

            if (LevelManager.HasInstance)
            {
                int nextId = LevelManager.Instance.GetNextLevelId();
                int currentId = LevelManager.Instance.CurrentLevelId;

                // 마지막 레벨 클리어 → 다음 레벨이 없으면 축하 팝업
                if (nextId <= currentId && UIManager.HasInstance)
                {
                    var popup = UIManager.Instance.OpenUI<PopupDescription>("Popup/PopupDescription");
                    if (popup != null)
                        popup.Show("Congratulations!", "You've cleared all levels!", "OK",
                            () => { if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY); });
                    return;
                }

                LevelManager.Instance.LoadLevel(nextId);
            }
        }

        private void OnHomeClicked()
        {
            CloseUI();
            if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();
            if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
        }

        #endregion

        #region Difficulty Background

        private void ApplyDifficultyBackground(DifficultyPurpose difficulty)
        {
            if (_imageBG == null) return;

            Sprite chosen = difficulty switch
            {
                DifficultyPurpose.Hard      => _bgSpriteHard,
                DifficultyPurpose.SuperHard => _bgSpriteSuperHard,
                _                           => _bgSpriteNormal
            };

            if (chosen != null) _imageBG.sprite = chosen;
        }

        #endregion

        #region Hard Level Option

        // 난이도별 ImageLight 색상
        private static readonly Color LIGHT_NORMAL    = new Color(0x00 / 255f, 0x9B / 255f, 0xFF / 255f); // #009BFF
        private static readonly Color LIGHT_HARD      = new Color(0xAF / 255f, 0x20 / 255f, 0xE5 / 255f); // #AF20E5
        private static readonly Color LIGHT_SUPERHARD  = new Color(0xFF / 255f, 0x59 / 255f, 0x00 / 255f); // #FF5900

        private void UpdateHardLevelOption(DifficultyPurpose difficulty)
        {
            // ImageLight 색상
            if (_imageLight != null)
            {
                _imageLight.color = difficulty switch
                {
                    DifficultyPurpose.Hard      => LIGHT_HARD,
                    DifficultyPurpose.SuperHard  => LIGHT_SUPERHARD,
                    _                            => LIGHT_NORMAL
                };
            }

            // ImageStage 스프라이트
            if (_imageStage != null)
            {
                Sprite stageSpr = difficulty switch
                {
                    DifficultyPurpose.Hard      => _sprStageHard ?? _sprStageNormal,
                    DifficultyPurpose.SuperHard  => _sprStageSuperHard ?? _sprStageNormal,
                    _                            => _sprStageNormal
                };
                if (stageSpr != null) _imageStage.sprite = stageSpr;
            }

            // HardOptionColor: Normal 숨김 / Hard·SuperHard 노출 + 스프라이트 교체
            if (_imageHardOptionColor != null)
            {
                if (difficulty == DifficultyPurpose.Normal)
                {
                    _imageHardOptionColor.gameObject.SetActive(false);
                }
                else
                {
                    _imageHardOptionColor.gameObject.SetActive(true);
                    Sprite hardOptSpr = difficulty == DifficultyPurpose.SuperHard
                        ? _sprHardOptionSuperHard
                        : _sprHardOptionHard;
                    if (hardOptSpr != null) _imageHardOptionColor.sprite = hardOptSpr;
                }
            }

            // HardLevelOption 표시: Normal=숨김, Hard/SuperHard=노출
            bool show = difficulty == DifficultyPurpose.Hard || difficulty == DifficultyPurpose.SuperHard;
            if (_hardLevelOption != null) _hardLevelOption.SetActive(show);

            // 방어적 처리: _hardLevelOption 루트가 프리팹에 미할당이거나 부분 영역만 가리키는 경우를 대비,
            // 하위 구성 요소(IconSkull, TxtHardLevel, Outline)도 개별적으로 가시 상태를 제어.
            if (_iconSkull != null) _iconSkull.gameObject.SetActive(show);
            if (_txtHardLevel != null) _txtHardLevel.gameObject.SetActive(show);
            if (_txtHardLevelOutline != null) _txtHardLevelOutline.gameObject.SetActive(show);

            // 곱하기 라벨: Normal 없음 / Hard x3 / SuperHard x5
            string multiplier = difficulty switch
            {
                DifficultyPurpose.SuperHard => "x5",
                DifficultyPurpose.Hard      => "x3",
                _                            => ""
            };
            bool showMultiplier = !string.IsNullOrEmpty(multiplier);
            if (_multiplierLabel != null) _multiplierLabel.SetActive(showMultiplier);
            if (_txtMultiplier != null)
            {
                _txtMultiplier.gameObject.SetActive(showMultiplier);
                _txtMultiplier.text = multiplier;
            }
            if (_txtMultiplierOutline != null)
            {
                _txtMultiplierOutline.gameObject.SetActive(showMultiplier);
                _txtMultiplierOutline.text = multiplier;
            }

            if (show)
            {
                string label = difficulty == DifficultyPurpose.SuperHard ? "SuperHard" : "Hard";
                if (_txtHardLevel != null) _txtHardLevel.text = label;
                if (_txtHardLevelOutline != null) _txtHardLevelOutline.text = label;

                // IconSkull 스프라이트
                if (_iconSkull != null)
                {
                    Sprite skullSpr = difficulty == DifficultyPurpose.SuperHard ? _sprSkullSuperHard : _sprSkullHard;
                    if (skullSpr != null) _iconSkull.sprite = skullSpr;
                }

                // TxtHardLevelOutline 머티리얼
                if (_txtHardLevelOutline != null)
                {
                    Material mat = difficulty == DifficultyPurpose.SuperHard
                        ? _matHardLevelOutlineSuperHard
                        : _matHardLevelOutlineHard;
                    UIOutlineStyle.ApplyMaterialOrColor(_txtHardLevelOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));
                    UIOutlineStyle.ApplyMaterialOrColor(_txtMultiplierOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));
                }
            }
        }

        private void ApplyBadge(DifficultyPurpose difficulty)
        {
            if (_imageBadge == null) return;

            Sprite badge = difficulty switch
            {
                DifficultyPurpose.SuperHard => _sprBadgeX5,
                DifficultyPurpose.Hard      => _sprBadgeX3,
                _                           => null
            };

            if (badge != null)
            {
                _imageBadge.sprite = badge;
                _imageBadge.gameObject.SetActive(true);
            }
            else
            {
                _imageBadge.gameObject.SetActive(false);
            }
        }

        private void EnsureRewardVisible(int starCount)
        {
            // ROLLBACK_POPUP_RESULT_REWARD_BINDING
            // PopupResult reward visuals are prefab-driven, so make the required gold nodes visible
            // and bind the same clear reward amount that CurrencyManager grants.
            int reward = 0;
            if (CurrencyManager.HasInstance)
                reward = CurrencyManager.Instance.GetCoinRewardForStars(starCount);

            // [2026-05-20] Reward 의 부모 체인 (Contents, PopupCommonFrame, ...) 활성화 보장.
            Transform rewardRoot = FindChildRecursive(transform, "Reward");
            if (rewardRoot != null)
            {
                ActivateNodeWithAncestors("Reward");
                // Reward 의 모든 자식 (ImageStage / FX / Gold / TxtGoldOutline / ...) subtree 강제 가시화 +
                // baked-in Canvas overrideSorting 정상화 + CanvasGroup alpha / Image·TMP enabled 보정.
                ForceVisibleSubtree(rewardRoot);
                // 디자이너 의도 z-order 복원 — back(ImageStage) → middle(FX) → front(Gold/Txt).
                ApplyRewardLayerOrder(rewardRoot);
            }

            SetRewardText("TxtGold", reward);
            SetRewardText("TxtGoldOutline", reward);
        }

        /// <summary>
        /// Reward subtree 의 z-order 를 PopupCanvas sortingOrder 기준 상대 offset 으로 재할당.
        /// 옛 prefab 의 baked 값 (ImageStage=1, Gold=2 등) 은 PopupCanvas=10 스킴 기준. 새 스킴(=200) 에선
        /// 절대값이 200 보다 작아 부모 뒤로 묻힘 — ForceVisibleSubtree 에서 모두 false 로 정리한 뒤 이 메서드가
        /// 각 노드에 새 sortingOrder 를 명시 부여.
        ///
        /// 디자이너 의도 layering (back → front):
        ///   ImageStage       base+1
        ///   FX ParticleSystem base+2
        ///   Gold             base+3
        ///   TxtGoldOutline   base+3 (Gold 와 같은 layer, sibling order 로 micro z 결정)
        ///   TxtGold          base+4 (outline 위)
        /// </summary>
        private void ApplyRewardLayerOrder(Transform rewardRoot)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            int baseOrder = parentCanvas != null ? parentCanvas.sortingOrder : 0;
            string layer = parentCanvas != null ? parentCanvas.sortingLayerName : "Default";

            AssignChildCanvasOrder(rewardRoot, "ImageStage", baseOrder + 1, layer);
            // FX subtree 의 세부 노드들은 사용자 검증된 절대 sortingOrder 4/5/6 사용 — base+2 (=202) 에선 미작동.
            // 디자이너가 FX 안쪽 micro layering 을 prefab 에 의도해둔 값. FX 부모 자체는 건드리지 않음.
            AssignChildCanvasOrder(rewardRoot, "FX_Glow",       4, layer);
            AssignChildCanvasOrder(rewardRoot, "FX_BackLightR", 5, layer);
            AssignChildParticleOrder(rewardRoot, "ParticleLight", 6, layer);
            AssignChildCanvasOrder(rewardRoot, "Gold", baseOrder + 3, layer);
            AssignChildCanvasOrder(rewardRoot, "TxtGoldOutline", baseOrder + 3, layer);
            AssignChildCanvasOrder(rewardRoot, "TxtGold", baseOrder + 4, layer);
        }

        private static void AssignChildCanvasOrder(Transform root, string nodeName, int order, string layer)
        {
            Transform node = FindChildRecursive(root, nodeName);
            if (node == null) return;
            var canvas = node.GetComponent<Canvas>();
            if (canvas == null) canvas = node.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerName = layer;
            canvas.sortingOrder = order;
        }

        private static void AssignChildParticleOrder(Transform root, string nodeName, int order, string layer)
        {
            Transform node = FindChildRecursive(root, nodeName);
            if (node == null) return;
            var psrs = node.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < psrs.Length; i++)
            {
                if (psrs[i] == null) continue;
                psrs[i].sortingLayerName = layer;
                psrs[i].sortingOrder = order;
            }
        }

        /// <summary>
        /// root 부터 시작해 전체 subtree 를 순회하며 가시성 차단 요소를 모두 보정.
        /// 1) GameObject 비활성 → SetActive(true)
        /// 2) Canvas overrideSorting=true (낮은 sortingOrder) → false (부모 PopupCanvas=200 상속)
        /// 3) CanvasGroup alpha 0 → 1
        /// 4) Image / TMP_Text enabled=false → true
        /// 5) ParticleSystemRenderer sortingOrder 가 0 이면 부모 캔버스 위로 올림 (FX 노드 대응)
        /// </summary>
        private void ForceVisibleSubtree(Transform root)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            int parentCanvasOrder = parentCanvas != null ? parentCanvas.sortingOrder : 0;
            string parentCanvasLayer = parentCanvas != null ? parentCanvas.sortingLayerName : "Default";

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t == null) continue;

                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);

                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && canvas.overrideSorting)
                    canvas.overrideSorting = false;

                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 1f) cg.alpha = 1f;

                var img = t.GetComponent<Image>();
                if (img != null && !img.enabled) img.enabled = true;

                var tmp = t.GetComponent<TMP_Text>();
                if (tmp != null && !tmp.enabled) tmp.enabled = true;

                // ParticleSystemRenderer 가 ScreenSpaceOverlay 캔버스 안에서 묻히는 케이스 보정.
                // PopupCanvas 의 sortingOrder=200 보다 한 단계 위로 올려야 파티클이 보임.
                var psr = t.GetComponent<ParticleSystemRenderer>();
                if (psr != null && psr.sortingOrder < parentCanvasOrder)
                {
                    psr.sortingOrder = parentCanvasOrder + 1;
                    psr.sortingLayerName = parentCanvasLayer;
                }
            }
        }

        private void ActivateNodeWithAncestors(string nodeName)
        {
            Transform node = FindChildRecursive(transform, nodeName);
            if (node == null) return;

            Transform popupRoot = transform;
            Transform cursor = node;
            while (cursor != null && cursor != popupRoot)
            {
                if (!cursor.gameObject.activeSelf)
                    cursor.gameObject.SetActive(true);

                // CanvasGroup alpha 0 인 부모만 보정 — interactable/blocksRaycasts 는 원본 의도 보존.
                var cg = cursor.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 1f) cg.alpha = 1f;

                cursor = cursor.parent;
            }
        }

        private static int DifficultyToRewardStarCount(DifficultyPurpose difficulty) => difficulty switch
        {
            DifficultyPurpose.SuperHard => 3,
            DifficultyPurpose.Hard => 2,
            _ => 1
        };

        private void SetRewardText(string nodeName, int reward)
        {
            Transform node = FindChildRecursive(transform, nodeName);
            if (node == null) return;

            node.gameObject.SetActive(true);
            var tmp = node.GetComponent<TMP_Text>();
            if (tmp != null && reward > 0)
                tmp.text = reward.ToString("N0");
        }

        private static Transform FindChildRecursive(Transform root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName)) return null;
            var children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && child.name == nodeName) return child;
            }
            return null;
        }

        #endregion


        #region Coin Fly

        private void TriggerCoinFly(int score)
        {
            RectTransform target = _goldTarget;
            if (target == null)
            {
                var hud = FindAnyObjectByType<UIHud>();
                if (hud != null && hud.GoldText != null) target = hud.GoldText.rectTransform;
            }
            if (target == null) { Debug.LogWarning("[CoinFly] target null"); return; }

            int coinCount = Mathf.Clamp(MIN_COIN_COUNT + (score / SCORE_PER_COIN_STEP), MIN_COIN_COUNT, MAX_COIN_COUNT);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            // target의 screen 좌표 (어떤 Canvas에 있든 동작)
            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            Camera targetCam = (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? targetCanvas.worldCamera : null;
            Vector2 screenTarget = RectTransformUtility.WorldToScreenPoint(targetCam, target.position);

            CoinFlyEffect.Play(screenCenter, screenTarget, coinCount,
                onEachLand: () => EventBus.Publish(new OnCoinFlyLanded()));
        }

        #endregion
    }

    /// <summary>독립 코루틴 실행용 헬퍼. 완료 후 풀로 반환.</summary>
    internal class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;

        public static CoroutineRunner Get()
        {
            if (_instance != null && _instance.gameObject != null)
            {
                _instance.gameObject.SetActive(true);
                return _instance;
            }

            var go = new GameObject("CoroutineRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CoroutineRunner>();
            return _instance;
        }

        /// <summary>이미 생성된 인스턴스가 있으면 반환, 없으면 null. StopAll 등 생성 없이 참조만 할 때 사용.</summary>
        public static CoroutineRunner GetIfExists()
        {
            return _instance != null && _instance.gameObject != null ? _instance : null;
        }

        public void Run(System.Collections.IEnumerator routine)
        {
            StartCoroutine(routine);
        }
    }
}
