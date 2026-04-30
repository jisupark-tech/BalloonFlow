using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 신규 기믹 첫 등장 시 PopupNewFeature 발화.
    /// PlayerPrefs로 본 기믹 영구 저장. OnLevelLoaded 시 LevelConfig.gimmickTypes 검사 → 미경험 기믹 큐잉 → 순차 표시.
    /// </summary>
    public class NewFeatureManager : Singleton<NewFeatureManager>
    {
        private const string PREFS_KEY_PREFIX = "NewFeature.Seen.";
        private const string POPUP_PREFAB_PATH = "Popup/NewFeature";

        /// <summary>LevelConfig.gimmickTypes (MapMaker 명명) → PopupNewFeature.featureKey 매핑.
        /// 미매핑 기믹은 팝업 안 띄움.</summary>
        private static readonly Dictionary<string, string> GimmickToFeatureKey = new Dictionary<string, string>
        {
            { "Hidden",       "Hidden" },
            { "Pinata",       "Pinata" },
            { "Pinata_Box",   "Pinata" },
            { "Spawner_T",    "Spawner" },
            { "Spawner_O",    "Spawner" },
            { "Lock_Key",     "KeyLock" },
            { "Wall",         "IronBox" },
            { "Ice",          "FrozenLayer" },
            { "Frozen_Dart",  "FrozenBox" },
            { "Barricade",    "Baricade" }, // PopupNewFeature 측 typo 유지
        };

        private readonly Queue<string> _pendingFeatures = new Queue<string>();
        private bool _isShowingPopup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (HasInstance) return;
            var go = new GameObject("NewFeatureManager");
            go.AddComponent<NewFeatureManager>();
        }

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            base.OnDestroy();
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            if (!LevelManager.HasInstance) return;
            var config = LevelManager.Instance.CurrentLevel;
            if (config == null || config.gimmickTypes == null) return;

            var addedThisCall = new HashSet<string>();
            for (int i = 0; i < config.gimmickTypes.Length; i++)
            {
                string gimmick = config.gimmickTypes[i];
                if (string.IsNullOrEmpty(gimmick)) continue;
                if (!GimmickToFeatureKey.TryGetValue(gimmick, out string featureKey)) continue;
                if (addedThisCall.Contains(featureKey)) continue;
                if (IsFeatureSeen(featureKey)) continue;

                addedThisCall.Add(featureKey);
                _pendingFeatures.Enqueue(featureKey);
                MarkFeatureSeen(featureKey);
            }

            if (!_isShowingPopup) StartCoroutine(ShowPendingSequentially());
        }

        private IEnumerator ShowPendingSequentially()
        {
            _isShowingPopup = true;

            // 로딩 + fade-in 끝날 때까지 대기 — popup 이 로딩 화면 위로 보이는 것을 방지.
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;

            while (_pendingFeatures.Count > 0)
            {
                string featureKey = _pendingFeatures.Dequeue();
                PopupNewFeature popup = OpenPopup();
                if (popup == null)
                {
                    Debug.LogWarning($"[NewFeatureManager] Failed to open PopupNewFeature for '{featureKey}'");
                    continue;
                }

                popup.Show(featureKey);

                // CanvasGroup alpha=0 으로 닫힐 때까지 대기 (UIBase.CloseUI() 동작)
                CanvasGroup cg = popup.GetComponent<CanvasGroup>();
                yield return null; // 한 프레임 양보 — alpha=1 적용 시간
                while (cg != null && cg.alpha > 0.01f)
                    yield return null;
            }

            _isShowingPopup = false;
        }

        private PopupNewFeature OpenPopup()
        {
            if (!UIManager.HasInstance) return null;
            return UIManager.Instance.OpenUI<PopupNewFeature>(POPUP_PREFAB_PATH);
        }

        public bool IsFeatureSeen(string featureKey)
        {
            return PlayerPrefs.GetInt(PREFS_KEY_PREFIX + featureKey, 0) == 1;
        }

        public void MarkFeatureSeen(string featureKey)
        {
            PlayerPrefs.SetInt(PREFS_KEY_PREFIX + featureKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>디버그/테스트용 — 모든 본 기록 리셋.</summary>
        public void ResetAllSeen()
        {
            foreach (var featureKey in GimmickToFeatureKey.Values)
                PlayerPrefs.DeleteKey(PREFS_KEY_PREFIX + featureKey);
            PlayerPrefs.Save();
        }
    }
}
