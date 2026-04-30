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

        /// <summary>로딩 단계 정의. (라벨, 가중치 0~1) — 가중치 합 = 1.0.</summary>
        private static readonly (string label, float weight)[] LoadingSteps = new[]
        {
            ("Initializing...",    0.10f),
            ("Connecting server...", 0.20f),
            ("Downloading data...",  0.40f),
            ("Loading assets...",    0.20f),
            ("Finalizing...",        0.10f),
        };

        private UITitle _ui;
        private bool _loadingStarted;
        private bool _loadingComplete;
        private bool _entered;
        private float _watchdogTimer;
        /// <summary>네트워크 대기 중일 때 watchdog 일시 정지 (오프라인이면 30s timeout 으로 Lobby 강제 진입 막기).</summary>
        private bool _isWaitingForNetwork;

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
                _ui.SetStatus(LoadingSteps[0].label);
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
        /// 로딩 흐름 — 단계별 진행도 누적해서 UI 갱신.
        /// 실제 다운로드/세팅 작업은 각 단계에서 호출 (현재는 시뮬레이션 + Firebase 초기화 hook).
        /// </summary>
        private IEnumerator LoadingFlow()
        {
            _loadingStarted = true;
            float accumulated = 0f;

            for (int i = 0; i < LoadingSteps.Length; i++)
            {
                var (label, weight) = LoadingSteps[i];

                // 네트워크 필요한 단계 (1: server connect, 2: download) 진입 전 연결 확인.
                // 끊겨있으면 여기서 PopupError 띄우고 연결될 때까지 hold.
                if (i == 1 || i == 2)
                    yield return EnsureInternet();

                if (_ui != null) _ui.SetStatus(label);

                // 단계별 실제 작업 hook
                yield return StartCoroutine(RunLoadingStep(i));

                accumulated += weight;
                if (_ui != null) _ui.SetProgress(accumulated);
            }

            _loadingComplete = true;
            if (_ui != null)
            {
                _ui.SetProgress(1f);
                _ui.SetStatus("Ready");
            }
        }

        /// <summary>
        /// CDM 다운로드 단계 — Addressables 의 ADDR_LABEL_CDM 라벨로 묶인 원격 콘텐츠 fetch.
        /// 진행도는 LoadingFlow 의 step weight 안에서 sub-progress 로 표시.
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
                yield return new WaitForSeconds(0.2f); // UX: 다운로드 0 이어도 UI 가 너무 빨리 넘어가지 않게
                yield break;
            }

            if (_ui != null) _ui.SetStatus($"Downloading data... ({FormatBytes(size)})");

            float lastReportedProgress = 0f;
            var dlTask = AddressableSystem.DownloadDependenciesAsync(Const.ADDR_LABEL_CDM,
                onProgress: p => lastReportedProgress = p);

            while (!dlTask.IsCompleted)
            {
                // 슬라이더 sub-progress 는 LoadingFlow 가 step weight 단위로 갱신 — 여기선 대기만.
                // 더 부드럽게 표시하려면 _ui.SetSubProgress(lastReportedProgress) 같은 API 추가 가능.
                yield return null;
            }

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
        /// 인덱스별 실제 로딩 작업.
        /// 0 Init / 1 Server / 2 Download / 3 Assets / 4 Finalize.
        /// 현재 시뮬레이션 — 실제 작업 추가 시 yield return 유지하며 추가 가능.
        /// </summary>
        private IEnumerator RunLoadingStep(int index)
        {
            switch (index)
            {
                case 0: // Initializing — Addressables init + UI atlas 사전 로드 + Firebase / Manager 초기화
                    {
                        var initTask = AddressableSystem.InitializeAsync();
                        while (!initTask.IsCompleted) yield return null;
                        if (!initTask.Result)
                            Debug.LogWarning("[TitleController] Addressables init 실패 — 로컬 빌드만 사용 가능할 수 있음");

                        // UI atlas + popup/UI/InGame prefab 일괄 사전 로드 — 이후 sync API 사용 가능
                        if (ResourceManager.HasInstance)
                        {
                            var rm = ResourceManager.Instance;
                            var atlasTask = rm.PreloadUIAtlasAsync();
                            while (!atlasTask.IsCompleted) yield return null;

                            var prefabsTask = rm.PreloadAddressablePrefabsAsync();
                            while (!prefabsTask.IsCompleted) yield return null;
                        }
                    }
                    yield return new WaitForSeconds(0.2f);
                    break;

                case 1: // Server connect — Firebase Auth / Firestore ping 등 hook
                    yield return new WaitForSeconds(0.6f);
                    break;

                case 2: // Download — Addressables CDM (Remote_CDM 그룹의 모든 의존성 fetch)
                    yield return DownloadCdmStep();
                    break;

                case 3: // Assets — 레벨/리소스 prefetch
                    yield return new WaitForSeconds(0.4f);
                    break;

                case 4: // Finalize — 마지막 검증
                    yield return new WaitForSeconds(0.2f);
                    break;

                default:
                    yield return null;
                    break;
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
