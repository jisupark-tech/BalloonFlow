using System.Collections;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Title 씬 컨트롤러.
    /// - GameManager / CameraManager / UIManager 는 SceneBuilder 가 씬에 배치 → Awake 에서 Instance 자동 설정
    /// - UITitle 프리팹을 UIManager.OpenUI 로 로드
    /// - CDM 다운로드 + 서버 세팅 단계별 실행 → 슬라이더로 진행도 표시
    /// - 100% 도달 시 자동으로 Lobby 씬 진입
    /// - 탭 입력은 로딩 중엔 무시 (단, 완료 후엔 즉시 진입 가능)
    /// </summary>
    public class TitleController : MonoBehaviour
    {
        /// <summary>안전 timeout — 어떤 단계가 너무 오래 걸려도 결국 진입.</summary>
        private const float MAX_LOADING_TIME = 30.0f;

        /// <summary>실제 작업이 너무 빠를 때 사용자가 볼 수 있도록 step 마다 보장하는 최소 시간 (초).</summary>
        private const float MIN_STEP_DURATION = 0.4f;

        /// <summary>step 완료 후 100% 상태로 잠깐 보여주고 다음 단계로.</summary>
        private const float STEP_HOLD_DURATION = 0.12f;

        /// <summary>로딩 단계 정의. 각 step 마다 progress bar 가 0→100% 채워진 뒤 다음으로.</summary>
        private static readonly string[] LoadingStepLabels = new[]
        {
            "Initializing...",
            "Connecting server...",
            "Loading SDKs...",
            "Downloading data...",
            "Loading assets...",
            "Finalizing...",
        };

        private UITitle _ui;
        private bool _loadingStarted;
        private bool _loadingComplete;
        private bool _entered;
        private float _watchdogTimer;
        /// <summary>네트워크 대기 중일 때 watchdog 일시 정지 (오프라인이면 30s timeout 으로 Lobby 강제 진입 막기).</summary>
        private bool _isWaitingForNetwork;

        /// <summary>현재 step 의 0~1 진행도 — step 작업이 직접 갱신. StepProgressDriver 가 매 프레임 UITitle 에 반영.</summary>
        private float _stepProgress;
        private float _stepStartTime;

        void Start()
        {
            // 카메라 설정
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureTitle();

            // 직전 씬의 UI/Popup 정리 (Title 진입 시 캐시된 잔여 UI 제거)
            if (UIManager.HasInstance) UIManager.Instance.DestroyAllUI();
            if (PopupManager.HasInstance) PopupManager.Instance.UnregisterAll();

            // 씬 캔버스를 UIManager에 등록
            if (UIManager.HasInstance)
            {
                var _uiCanvas = GameObject.Find("UICanvas");
                if (_uiCanvas == null) _uiCanvas = GameObject.Find("Canvas");
                if (_uiCanvas == null) _uiCanvas = CreateCanvas("UICanvas", 0);

                var _popupCanvas = GameObject.Find("PopupCanvas");
                if (_popupCanvas == null) _popupCanvas = CreateCanvas("PopupCanvas", 10);

                var _effectCanvas = GameObject.Find("EffectCanvas");
                if (_effectCanvas == null) _effectCanvas = CreateCanvas("EffectCanvas", 15);

                UIManager.Instance.SetSceneCanvas(_uiCanvas.transform, _popupCanvas.transform, _effectCanvas.transform);
                _ui = UIManager.Instance.OpenUI<UITitle>("UI/UITitle");
            }

            if (_ui != null)
            {
                _ui.SetProgress(0f);
                _ui.SetStatus(LoadingStepLabels[0]);
                _ui.SetTapHintVisible(false);
            }

            StartCoroutine(LoadingFlow());
        }

        void Update()
        {
            if (_entered) return;

            // 로딩 완료 후 자동 입장 (모든 진행도 == 1.0)
            if (_loadingComplete)
            {
                EnterLobby();
                return;
            }

            // 로딩 중 watchdog — 정의된 max time 초과 시 강제 입장. 네트워크 대기 중에는 일시 정지.
            if (_loadingStarted && !_isWaitingForNetwork)
            {
                _watchdogTimer += Time.deltaTime;
                if (_watchdogTimer >= MAX_LOADING_TIME)
                {
                    Debug.LogWarning("[TitleController] Loading watchdog timeout → 강제 입장");
                    if (_ui != null) _ui.SetProgress(1f);
                    EnterLobby();
                }
            }
        }

        /// <summary>
        /// 로딩 흐름 — 각 단계별로 progress bar 가 **0→100% reset & 다시 채움** 패턴 (n번의 로딩바).
        /// 실제 작업은 RunLoadingStep 코루틴이 진행. step 내에서 _stepProgress 를 0~1 로 갱신하면
        /// 본 함수가 매 프레임 UITitle.SetProgress 에 반영. 작업이 빨라도 MIN_STEP_DURATION 만큼 보여줌.
        /// </summary>
        private IEnumerator LoadingFlow()
        {
            _loadingStarted = true;

            for (int i = 0; i < LoadingStepLabels.Length; i++)
            {
                string label = LoadingStepLabels[i];

                // 네트워크 필요한 단계 (Connecting server / Downloading data) 진입 전 연결 확인.
                if (NeedsInternet(i))
                    yield return EnsureInternet();

                if (_ui != null)
                {
                    _ui.SetStatus(label);
                    _ui.SetProgress(0f);
                }

                _stepProgress = 0f;
                _stepStartTime = Time.realtimeSinceStartup;

                // 단계 작업 + UI progress 동기화 코루틴 동시 실행 — 작업 끝나면 progressDriver 종료
                bool workDone = false;
                StartCoroutine(StepProgressDriver(() => workDone));
                yield return StartCoroutine(RunLoadingStep(i));
                workDone = true;

                // 100% 도달 보장 + 잠깐 hold
                _stepProgress = 1f;
                if (_ui != null) _ui.SetProgress(1f);
                yield return new WaitForSecondsRealtime(STEP_HOLD_DURATION);
            }

            _loadingComplete = true;
            if (_ui != null)
            {
                _ui.SetProgress(1f);
                _ui.SetStatus("Ready");
            }
        }

        private static bool NeedsInternet(int stepIndex)
        {
            // server connect / SDK init / CDM download — 인터넷 필요
            return stepIndex == 1 || stepIndex == 2 || stepIndex == 3;
        }

        /// <summary>
        /// step 진행 중 progress bar 를 부드럽게 채움.
        /// 실제 작업이 빨라 _stepProgress 갱신이 없어도 시간 기반으로 천천히 차오름 (시각 피드백 보장).
        /// 작업이 느리면 시간 진행분과 작업 보고분 중 큰 값을 사용 (실 progress 가 시각보다 빠를 때 따라감).
        /// </summary>
        private IEnumerator StepProgressDriver(System.Func<bool> isWorkDone)
        {
            const float MAX_TIME_BASED = 0.95f; // 시간만으로 95% 까지만, 마지막 5% 는 작업 완료 시점에
            while (!isWorkDone())
            {
                float elapsed = Time.realtimeSinceStartup - _stepStartTime;
                float timeRatio = Mathf.Clamp01(elapsed / MIN_STEP_DURATION) * MAX_TIME_BASED;
                float p = Mathf.Max(timeRatio, _stepProgress);
                if (_ui != null) _ui.SetProgress(p);
                yield return null;
            }

            // 작업 완료 후 minimum 시간 보장
            float remain = MIN_STEP_DURATION - (Time.realtimeSinceStartup - _stepStartTime);
            if (remain > 0f) yield return new WaitForSecondsRealtime(remain);
        }

        /// <summary>
        /// CDM 다운로드 단계 — Addressables 의 ADDR_LABEL_CDM 라벨로 묶인 원격 콘텐츠 fetch.
        /// 다운로드 progress 를 _stepProgress 에 직접 반영 — UI 슬라이더가 실시간 0→1 으로 채워짐.
        /// 라벨에 등록된 콘텐츠 없거나 모두 cache 됐으면 즉시 통과.
        /// </summary>
        private IEnumerator DownloadCdmStep()
        {
            // 다운로드 사이즈 확인 — 0 이면 cache hit, skip
            var sizeTask = AddressableSystem.GetDownloadSizeAsync(Const.ADDR_LABEL_CDM);
            while (!sizeTask.IsCompleted) yield return null;

            long size = sizeTask.Result;
            if (size <= 0)
            {
                yield break; // cache hit — StepProgressDriver 가 시간 기반으로 0→1 처리
            }

            if (_ui != null) _ui.SetStatus($"Downloading data... ({FormatBytes(size)})");

            var dlTask = AddressableSystem.DownloadDependenciesAsync(Const.ADDR_LABEL_CDM,
                onProgress: p => _stepProgress = Mathf.Clamp01(p));

            while (!dlTask.IsCompleted) yield return null;

            if (!dlTask.Result)
                Debug.LogWarning("[TitleController] CDM 다운로드 실패 — 로컬 콘텐츠만 사용");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024 * 1024)} MB";
            return $"{bytes / (1024L * 1024 * 1024)} GB";
        }

        /// <summary>
        /// 인터넷 연결 확인 — 끊겨있으면 PopupError(Wifi) 띄우고 status text 를 "Connecting to internet..." 로.
        /// 사용자가 OK 누르면 popup 닫히고 재확인 → 여전히 끊겨있으면 다시 popup. 연결되면 진행.
        /// </summary>
        private IEnumerator EnsureInternet()
        {
            while (Application.internetReachability == NetworkReachability.NotReachable)
            {
                _isWaitingForNetwork = true;
                if (_ui != null) _ui.SetStatus("Connecting to internet...");

                PopupError popup = null;
                if (UIManager.HasInstance)
                {
                    popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                    if (popup != null) popup.ShowNoInternet();
                }

                // 사용자가 OK 또는 X 로 popup 을 닫을 때까지 대기 — UIBase.CloseUI 가 SetActive(false) 처리.
                if (popup != null)
                {
                    while (popup != null && popup.gameObject.activeSelf)
                        yield return null;
                }
                else
                {
                    // popup 로드 실패 시 폴백 — 1초 간격 재시도
                    yield return new WaitForSeconds(1f);
                }

                // 짧은 대기 후 재확인 (상태 갱신 시간 확보)
                yield return new WaitForSeconds(0.5f);
            }
            _isWaitingForNetwork = false;
        }

        /// <summary>
        /// 인덱스별 실제 로딩 작업. LoadingStepLabels 와 1:1 매핑.
        /// 0 Initializing / 1 Connecting server / 2 Loading SDKs / 3 Downloading data (CDM)
        /// 4 Loading assets / 5 Finalizing
        /// 작업이 빠른 step 은 StepProgressDriver 의 시간 기반 채움에 의존.
        /// 작업 progress 를 직접 갱신하려면 _stepProgress 를 0~1 로 set.
        /// </summary>
        private IEnumerator RunLoadingStep(int index)
        {
            switch (index)
            {
                case 0: // Initializing — Addressables init + UI atlas 사전 로드 + prefab cache
                    {
                        var initTask = AddressableSystem.InitializeAsync();
                        while (!initTask.IsCompleted) yield return null;
                        if (!initTask.Result)
                            Debug.LogWarning("[TitleController] Addressables init 실패 — 로컬 빌드만 사용 가능할 수 있음");

                        if (ResourceManager.HasInstance)
                        {
                            var rm = ResourceManager.Instance;
                            var atlasTask = rm.PreloadUIAtlasAsync();
                            while (!atlasTask.IsCompleted) yield return null;

                            var prefabsTask = rm.PreloadAddressablePrefabsAsync();
                            while (!prefabsTask.IsCompleted) yield return null;
                        }
                    }
                    break;

                case 1: // Connecting server — Firebase Auth + Firestore /users/{uid} 로드 대기
                    yield return WaitForUserDataReady();
                    break;

                case 2: // Loading SDKs — AppsFlyer / MAX / Facebook / Analytics / IAP 등
                    yield return WaitForSdkReady();
                    break;

                case 3: // Downloading data — Addressables CDM
                    yield return DownloadCdmStep();
                    break;

                case 4: // Loading assets — 레벨/카탈로그 prefetch
                    yield return WaitForCatalogReady();
                    break;

                case 5: // Finalizing
                    yield return null;
                    break;

                default:
                    yield return null;
                    break;
            }
        }

        /// <summary>UserDataService.IsReady 까지 대기 — 8초 timeout (Firestore 미연결이면 그냥 진행).</summary>
        private IEnumerator WaitForUserDataReady()
        {
            const float TIMEOUT = 8f;
            float t = 0f;
            while (t < TIMEOUT)
            {
                if (UserDataService.HasInstance && UserDataService.Instance.IsReady) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>IAPManager 초기화 까지 대기 — 5초 timeout. (AdManager 는 자체 비동기, 차단하지 않음.)</summary>
        private IEnumerator WaitForSdkReady()
        {
            const float TIMEOUT = 5f;
            float t = 0f;
            while (t < TIMEOUT)
            {
                bool iapOk = !IAPManager.HasInstance || IAPManager.Instance.IsInitialized();
                if (iapOk) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>ShopCatalogService 13개 fetch + Addressable atlas/prefab 사전 로드 보강 대기.</summary>
        private IEnumerator WaitForCatalogReady()
        {
            const float TIMEOUT = 5f;
            float t = 0f;
            while (t < TIMEOUT)
            {
                bool shopOk = !ShopCatalogService.HasInstance || ShopCatalogService.Instance.IsLoaded;
                if (shopOk) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>로딩 완료 시 1회 호출 — Lobby 씬 로드.</summary>
        private void EnterLobby()
        {
            if (_entered) return;
            _entered = true;
            if (GameManager.HasInstance)
                GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
        }

        static GameObject CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1242f, 2688f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return go;
        }
    }
}
