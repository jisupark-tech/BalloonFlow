using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

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

        // ROLLBACK_TITLE_NET_GATE_20260713: 절대 하드캡 — 위 워치독은 네트워크 대기(_isWaitingForNetwork)
        //   중 멈추므로, 어떤 단계가 네트워크에 매달려도 부팅 후 이 시간을 넘으면 '무조건' 강제 입장한다.
        //   L1~20은 로컬(Episode1)이라 강제 입장해도 첫 레벨 플레이 가능. belt-and-suspenders 안전망.
        private const float HARD_LOADING_CAP = 20.0f;

        // ROLLBACK_BOOT_CHECKPOINTS_20260713: 부팅→첫플레이 퍼널 계측(어느 단계서 이탈하는지 진단). 필요 없어지면 false.
        private const bool BOOT_CHECKPOINTS_ENABLED = true;

        /// <summary>[#9] 로딩 완료 후 "Tap to Start" 노출 시간 — 이 시간 내 탭하면 즉시 진입, 미탭 시 자동 진입(doc: 자동 진입 유지).</summary>
        private const float TITLE_AUTO_ENTER_DELAY = 1.0f;

        /// <summary>실제 작업이 너무 빠를 때 사용자가 볼 수 있도록 step 마다 보장하는 최소 시간 (초).</summary>
        // ROLLBACK_LOADTIME_STEP_MIN_20260615: 로딩 단축 — IAP/카탈로그 대기 제거 후 step 2·4 가 즉시 끝나
        //   이 인위적 최소시간이 로딩 바닥(6 step × MIN + STEP_HOLD)으로 남는다. 0.4→0.2 / 0.12→0.06 으로
        //   ~1.5s 추가 절감. (바 가시성은 유지.) 롤백: 0.4f / 0.12f 로 환원.
        private const float MIN_STEP_DURATION = 0.2f;

        /// <summary>step 완료 후 100% 상태로 잠깐 보여주고 다음 단계로.</summary>
        private const float STEP_HOLD_DURATION = 0.06f;

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

        // ROLLBACK_LOADING_LOCALIZE_20260714: 로딩 라벨 지역화 키(LoadingStepLabels 와 1:1).
        //   ★ 표시는 LocalizationService.Get(키) 로, EmitBootCp/로그는 계속 영어 LoadingStepLabels(식별자 안정 = 분석 오염 방지).
        //   언어는 LocalizationService.AutoSelectDeviceLanguage([RuntimeInitializeOnLoadMethod(BeforeSceneLoad)])가
        //   이 씬보다 먼저 세팅하므로 첫 표시부터 KO 반영. 한글은 Poppins-Bold SDF + ChironGoRoundTC fallback 으로 렌더.
        private static readonly string[] LoadingStepKeys = new[]
        {
            "loading.init",
            "loading.connect_server",
            "loading.sdk",
            "loading.data",
            "loading.assets",
            "loading.finalize",
        };

        private UITitle _ui;
        private bool _loadingStarted;
        private bool _loadingComplete;
        // ROLLBACK_TITLE_PERMISSION_AFTER_LOADING_20260623: 로딩 완료 후 알림 권한 응답까지 받은 뒤에야 씬 진입 허용.
        private bool _permissionResolved;
        // ROLLBACK_PRIVACY_TERM_PARALLEL_20260714: 동의 팝업을 로딩과 병렬로 띄우되, 씬 전환은 동의 완료를 게이트로 요구.
        private bool _consentResolved;
        private bool _entered;
        private float _watchdogTimer;
        // ROLLBACK_TITLE_NET_GATE_20260713: 절대 하드캡 타이머 — 네트워크 대기와 '무관하게' 증가.
        private float _absLoadTimer;
        private float _loadingFlowStartTime;
        /// <summary>네트워크 대기 중일 때 watchdog 일시 정지 (오프라인이면 30s timeout 으로 Lobby 강제 진입 막기).</summary>
        private bool _isWaitingForNetwork;
        /// <summary>[#9] 로딩 완료 후 "Tap to Start" hint 노출 1회 가드 + 자동 진입 카운트다운.</summary>
        private bool _completeHintShown;
        private float _completeTimer;

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
                _ui.SetStatus(LocalizationService.Get(LoadingStepKeys[0])); // ROLLBACK_LOADING_LOCALIZE_20260714
                _ui.SetTapHintVisible(false);
            }

            // [#3] 알림 권한 — 로딩 "시작 전"에 1회 요청하고, 결정될 때까지 대기한 뒤 로딩 진행.
            //   - NotificationManager 가 아직 없으면(AfterSceneLoad 타이밍) EnsureCreated 로 생성 보장.
            //   - RequestPermissionAsync 는 OS 가 NotDetermined 일 때만 실제 다이얼로그. await 로 응답까지 대기.
            StartCoroutine(StartupFlow());
        }

        /// <summary>로딩을 먼저 끝낸 뒤(바 100%), 씬 전환 직전에 알림 권한을 띄워 선택받고 진입.</summary>
        private IEnumerator StartupFlow()
        {
            // ROLLBACK_PRIVACY_TERM_PARALLEL_20260714:
            // 동의 팝업을 로딩과 '병렬'로 진행(로딩바가 뒤에서 채워짐). 단 씬 전환(Enter)은 동의 완료를 게이트로 요구.
            //   이전(ROLLBACK_PRIVACY_TERM_GATE_20260626)은 동의 완료까지 로딩 시작을 막았음 → 체감 대기 증가.
            //   롤백: 아래 StartCoroutine 을 `yield return PrivacyTermConsentRoutine();` 로 되돌리고 while 게이트 제거.
            StartCoroutine(PrivacyTermConsentRoutine());       // 병렬 — 완료 시 _consentResolved=true

            // 순서: 로딩(병렬) → 로딩 완료 후에도 '동의' 전이면 대기 → (씬 직전) 알림 권한 → 진입.
            //   RequestPermissionAsync 는 OS 가 NotDetermined(첫 실행)일 때만 실제 다이얼로그 → 재실행 시 즉시 통과.
            yield return LoadingFlow();                        // 1) 로딩바 채움 (동의와 무관하게 즉시 진행)
            while (!_consentResolved) yield return null;       // 2) 씬 전환 게이트 — 동의해야 통과(로딩 끝나도 대기)
            yield return RequestNotificationPermissionRoutine(); // 3) 동의+로딩 후에만 권한 다이얼로그(팝업 겹침 방지)
            _permissionResolved = true;                        // 4) 권한 결정 후에야 진입 허용
        }

        private IEnumerator PrivacyTermConsentRoutine()
        {
            string acceptedVersion = PlayerPrefs.GetString(Const.PREFS_PRIVACY_TERM_VERSION, string.Empty);
            if (acceptedVersion == Const.PRIVACY_TERM_VERSION)
            {
                _consentResolved = true;   // 재실행/기동의 유저 → 팝업 없이 즉시 게이트 통과
                yield break;
            }

            bool consentResolved = false;
            PopupPrivacyTerm popup = null;

            if (UIManager.HasInstance)
            {
                GameObject go = UIManager.Instance.LoadPrefab(Const.POPUP_PRIVACY_TERM, UIManager.Instance.PopupTr);
                if (go != null)
                {
                    popup = go.GetComponent<PopupPrivacyTerm>();
                    if (popup == null) popup = go.AddComponent<PopupPrivacyTerm>();
                    popup.Show(() => consentResolved = true);
                }
            }

            if (popup == null)
            {
                Debug.LogWarning("[TitleController] PopupPrivacyTerm load failed. Continuing title loading without consent popup.");
                _consentResolved = true;   // 팝업 로드 실패 → 소프트락 방지(씬 전환 허용)
                yield break;
            }

            while (!consentResolved)
                yield return null;

            _consentResolved = true;       // 동의 완료 → 씬 전환 게이트 해제
        }

        private IEnumerator RequestNotificationPermissionRoutine()
        {
            NotificationManager.EnsureCreated();
            if (!NotificationManager.HasInstance) yield break;

            var task = NotificationManager.Instance.RequestPermissionAsync();
            // OS 권한 다이얼로그 응답(또는 이미 결정 시 즉시) 완료까지 대기.
            while (task != null && !task.IsCompleted) yield return null;
        }

        void Update()
        {
            if (_entered) return;

            // ROLLBACK_PRIVACY_TERM_PARALLEL_20260714: 동의 전에는 어떤 경로로도 씬 전환 금지.
            //   (완료진입·로딩 워치독·절대 하드캡까지 전부 스킵 — 로딩 코루틴은 계속 돌아 바는 채워진다.)
            //   동의는 사용자 액션이라 시간 무제한 대기가 정상 → 하드캡 타임아웃이 동의를 우회하지 못하게 막는다.
            if (!_consentResolved) return;

            // [#9] 로딩 완료 → "Tap to Start" 노출. 탭하면 즉시 진입, 미탭 시 짧은 지연 후 자동 진입(doc: 자동 진입 유지).
            // ROLLBACK_TITLE_PERMISSION_AFTER_LOADING_20260623: 알림 권한 응답 전엔 진입 보류(_permissionResolved 게이트).
            if (_loadingComplete && _permissionResolved)
            {
                if (!_completeHintShown)
                {
                    _completeHintShown = true;
                    _completeTimer = 0f;
                    // "Tap to Start" 문구는 표시하지 않음(사용자 요구) — 탭/자동 진입 동작은 유지.
                    if (_ui != null) _ui.SetTapHintVisible(false);
                }
                _completeTimer += Time.deltaTime;
                if (AnyTapThisFrame() || _completeTimer >= TITLE_AUTO_ENTER_DELAY)
                    Enter();
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
                    Enter();
                    return;
                }
            }

            // ROLLBACK_TITLE_NET_GATE_20260713: 절대 하드캡 — _isWaitingForNetwork 여부와 '무관하게' 부팅 후
            //   HARD_LOADING_CAP 초과 시 무조건 강제 입장. 위 워치독은 네트워크 대기 중 멈춰 무한 대기 위험이
            //   있으므로, 어떤 단계가 네트워크에 매달려도 사용자가 타이틀에 갇히지 않도록 보장. Enter()는
            //   _entered 가드로 멱등, L1~20 로컬이라 강제 입장해도 첫 레벨 플레이 가능.
            if (_loadingStarted)
            {
                _absLoadTimer += Time.deltaTime;
                if (_absLoadTimer >= HARD_LOADING_CAP)
                {
                    Debug.LogWarning("[TitleController] Absolute loading hard-cap → 강제 입장 (offline/slow network)");
                    if (_ui != null) _ui.SetProgress(1f);
                    Enter();
                }
            }
        }

        /// <summary>[#9] 탭/클릭/키 입력 감지 (신규 Input System). "Tap to Start" 즉시 진입용.</summary>
        private static bool AnyTapThisFrame()
        {
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>
        /// 로딩 흐름 — 각 단계별로 progress bar 가 **0→100% reset & 다시 채움** 패턴 (n번의 로딩바).
        /// 실제 작업은 RunLoadingStep 코루틴이 진행. step 내에서 _stepProgress 를 0~1 로 갱신하면
        /// 본 함수가 매 프레임 UITitle.SetProgress 에 반영. 작업이 빨라도 MIN_STEP_DURATION 만큼 보여줌.
        /// </summary>
        private IEnumerator LoadingFlow()
        {
            _loadingStarted = true;
            _loadingFlowStartTime = Time.realtimeSinceStartup;

            for (int i = 0; i < LoadingStepLabels.Length; i++)
            {
                string label = LoadingStepLabels[i];
                float stepLogStart = Time.realtimeSinceStartup;
                EmitBootCp(label, i); // ROLLBACK_BOOT_CHECKPOINTS_20260713: step i 도달

                // 네트워크 필요한 단계 (Connecting server / Downloading data) 진입 전 연결 확인.
                if (NeedsInternet(i))
                    yield return EnsureInternet();

                if (_ui != null)
                {
                    _ui.SetStatus(LocalizationService.Get(LoadingStepKeys[i])); // ROLLBACK_LOADING_LOCALIZE_20260714: 표시만 지역화(label 은 로그/체크포인트용 영어 유지)
                    _ui.SetProgress(0f);
                }

                _stepProgress = 0f;
                _stepStartTime = Time.realtimeSinceStartup;

                // 단계 작업 + UI progress 동기화 코루틴 동시 실행 — 작업 끝나면 progressDriver 종료
                bool workDone = false;
                StartCoroutine(StepProgressDriver(() => workDone));
                yield return StartCoroutine(RunLoadingStepRelease(i));
                workDone = true;
                LogLoadStepTiming(label, stepLogStart);

                // 100% 도달 보장 + 잠깐 hold
                _stepProgress = 1f;
                if (_ui != null) _ui.SetProgress(1f);
                yield return new WaitForSecondsRealtime(STEP_HOLD_DURATION);
            }

            _loadingComplete = true;
            EmitBootCp("loading_complete", 6); // ROLLBACK_BOOT_CHECKPOINTS_20260713
            Debug.Log($"[TitleLoad] complete total={(Time.realtimeSinceStartup - _loadingFlowStartTime):F2}s");
            if (_ui != null)
            {
                _ui.SetProgress(1f);
                _ui.SetStatus(LocalizationService.Get("loading.ready")); // ROLLBACK_LOADING_LOCALIZE_20260714
            }
        }

        private static void LogLoadStepTiming(string label, float startTime)
        {
            Debug.Log($"[TitleLoad] step='{label}' elapsed={(Time.realtimeSinceStartup - startTime):F2}s");
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
            // ROLLBACK_RELEASE_TITLE_LOADTIME_20260616:
            // Remote catalog/cache size checks can be slow on device. Do a short soft wait, then
            // continue Title with local content and let CDM finish in the background.
            const float SIZE_CHECK_SOFT_TIMEOUT = 1.0f;
            float sizeWait = 0f;
            while (!sizeTask.IsCompleted && sizeWait < SIZE_CHECK_SOFT_TIMEOUT)
            {
                sizeWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!sizeTask.IsCompleted)
            {
                StartCoroutine(DownloadCdmInBackground(sizeTask));
                yield break;
            }

            long size = sizeTask.Result;
            if (size <= 0)
            {
                yield break; // cache hit — StepProgressDriver 가 시간 기반으로 0→1 처리
            }

            StartCoroutine(DownloadCdmInBackground(sizeTask, size));
            yield break;
        }

        private IEnumerator DownloadCdmInBackground(Task<long> sizeTask, long knownSize = -1)
        {
            while (knownSize < 0 && sizeTask != null && !sizeTask.IsCompleted)
                yield return null;

            long size = knownSize;
            if (size < 0 && sizeTask != null && sizeTask.Status == TaskStatus.RanToCompletion)
                size = sizeTask.Result;
            if (size <= 0) yield break;

            Debug.Log($"[TitleLoad] CDM background download started ({FormatBytes(size)})");

            var dlTask = AddressableSystem.DownloadDependenciesAsync(Const.ADDR_LABEL_CDM);

            while (!dlTask.IsCompleted) yield return null;

            if (dlTask.Result)
                Debug.Log("[TitleLoad] CDM background download complete");

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
            // ROLLBACK_TITLE_NET_GATE_20260713: 오프라인 '영구 갇힘' 함정 제거 (P0 리텐션 최대 누수).
            //   [배경] L1~20은 로컬(Episode1)이라 네트워크 없이도 첫 레벨 플레이 가능(Enter→FtueGate 로컬 경로).
            //   그런데 과거엔 이 메서드가 NotReachable 인 동안 while 루프로 "No Internet" 팝업을 '무한' 재노출 +
            //   그 사이 _isWaitingForNetwork=true 로 로딩 워치독까지 정지(Update) → 네트워크 없음/끊김이면
            //   타이틀에 '영구히 갇힘' → 첫 레벨 진입 실패(BQ 실측: 설치 23%가 play_start 0, 그 유력 원인).
            //   [수정] 팝업을 '1회'만 안내하고, 유저가 닫으면(또는 그 사이 연결되면) 오프라인이어도 '진행'한다.
            //   network 단계(server/SDK/CDM)는 각자 soft-timeout(~2s)·백그라운드 폴백을 이미 가지므로 로컬
            //   콘텐츠로 진입 가능하고, 재연결 시 백그라운드로 동기된다. (무한 재시도 while 루프 제거가 핵심.)
            if (Application.internetReachability != NetworkReachability.NotReachable)
                yield break; // 온라인 — 게이트 통과

            _isWaitingForNetwork = true;
            EmitBootCp("net_gate_offline", -1); // ROLLBACK_BOOT_CHECKPOINTS_20260713: 오프라인 게이트 진입(P0 측정 핵심)
            if (_ui != null) _ui.SetStatus(LocalizationService.Get("loading.connect_internet")); // ROLLBACK_LOADING_LOCALIZE_20260714

            PopupError popup = null;
            if (UIManager.HasInstance)
            {
                popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                if (popup != null) popup.ShowNoInternet();
            }

            // 팝업을 유저가 닫을 때까지 '한 번'만 대기 — 닫으면(또는 그 사이 연결되면) 오프라인이어도 진행.
            //   (팝업 로드 실패 시 짧은 폴백 대기 후 진행.) 절대 하드캡(Update)도 이중 안전망으로 동작.
            if (popup != null)
            {
                while (popup != null && popup.gameObject.activeSelf)
                    yield return null;
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }

            _isWaitingForNetwork = false; // 오프라인이어도 진행 — 로컬 L1~20 플레이 보장
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
                    // 알림 권한 요청은 Start() 로 이동 (게임 시작 직후 노출).
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

        /// <summary>Release loading step runner.</summary>
        // ROLLBACK_RELEASE_TITLE_LOADTIME_20260616:
        // Release-friendly loading path. The old RunLoadingStep method is left above as a
        // direct rollback reference; switch LoadingFlow back to RunLoadingStep(i) to restore.
        private IEnumerator RunLoadingStepRelease(int index)
        {
            switch (index)
            {
                case 0:
                    {
                        var initTask = AddressableSystem.InitializeAsync();
                        while (!initTask.IsCompleted) yield return null;
                        if (!initTask.Result)
                            Debug.LogWarning("[TitleController] Addressables init failed. Local build content may still be used.");

                        if (ResourceManager.HasInstance)
                        {
                            var rm = ResourceManager.Instance;
                            var atlasTask = rm.PreloadUIAtlasAsync();
                            while (!atlasTask.IsCompleted) yield return null;

                            // ROLLBACK_RELEASE_TITLE_LOADTIME_20260616:
                            // Full core/ui prefab preload warms cache but can make device Title slow.
                            // Keep the warm-up, but do not block first lobby entry on it.
                            // Rollback: replace StartCoroutine(...) with the old wait loop.
                            var prefabsTask = rm.PreloadAddressablePrefabsAsync();
                            StartCoroutine(WaitForBackgroundTask(prefabsTask, "Addressable prefab preload"));
                        }
                    }
                    break;

                case 1:
                    yield return WaitForUserDataReady();
                    break;

                case 2:
                    yield return WaitForSdkReady();
                    break;

                case 3:
                    yield return DownloadCdmStep();
                    break;

                case 4:
                    yield return WaitForCatalogReady();
                    break;

                case 5:
                    yield return null;
                    break;

                default:
                    yield return null;
                    break;
            }
        }

        private IEnumerator WaitForBackgroundTask(Task task, string label)
        {
            while (task != null && !task.IsCompleted)
                yield return null;

            if (task == null) yield break;

            if (task.IsFaulted)
                Debug.LogWarning($"[TitleLoad] background {label} failed: {task.Exception?.GetBaseException().Message}");
            else if (task.IsCanceled)
                Debug.LogWarning($"[TitleLoad] background {label} cancelled");
            else
                Debug.Log($"[TitleLoad] background {label} complete");
        }

        private IEnumerator WaitForUserDataReady()
        {
            // ROLLBACK_RELEASE_TITLE_LOADTIME_20260616:
            // Firestore/UserData can be slow on cold launch. Lobby can enter with the local
            // fallback and services finish in background, so keep this as a short soft wait.
            // Rollback: restore TIMEOUT to 8f if server user data must block Title.
            const float TIMEOUT = 2f;
            float t = 0f;
            while (t < TIMEOUT)
            {
                if (UserDataService.HasInstance && UserDataService.Instance.IsReady) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>SDK 단계 — IAP/ShopCatalog 는 로비 진입을 차단하지 않는다(샵 전용, 클릭 시 retry).</summary>
        private IEnumerator WaitForSdkReady()
        {
            // ROLLBACK_LOADTIME_IAP_NONBLOCKING_20260615:
            // IAP 는 '샵 전용'이고 구매 클릭 시 retry 하므로 로비 진입을 막을 이유가 없다(기존 최대 20s 대기 제거 → 로딩 −최대20s).
            // SDK init 자체는 SdkBootstrap(BeforeSceneLoad)에서 이미 백그라운드로 진행 중. 여기선 catalog fetch 만 1회 nudge 후 즉시 통과.
            // 롤백: 본문을 종전 20s polling 루프(while t<20: if IsIapReadyForShop() yield break; ...)로 복원.
            if (ShopCatalogService.HasInstance && !ShopCatalogService.Instance.IsLoaded)
                ShopCatalogService.Instance.RetryFetch();
            yield break;
        }

        /// <summary>
        /// ShopCatalogService 13개 fetch + 현재 진행 에피소드 prefetch.
        /// 에피소드: UserData.highestClearedLevel+1 의 packageId 를 LevelEpisodeService 에 캐싱.
        /// </summary>
        private IEnumerator WaitForCatalogReady()
        {
            // 다음 플레이 레벨 = FtueGate.HighestClearedLevel + 1 (단일 진실 소스: PlayerPrefs).
            // 온보딩 범위(1~5) 내로 클램프 — Lv.5 클리어 이후엔 Lv.5 에피소드를 prefetch (Lobby 진입 후 별도 처리).
            int nextLevel = Mathf.Clamp(FtueGate.HighestClearedLevel + 1, 1, FtueGate.ONBOARDING_CLEAR_LEVEL);

            System.Threading.Tasks.Task<bool> episodeTask = null;
            if (LevelEpisodeService.HasInstance)
            {
                episodeTask = LevelEpisodeService.Instance.EnsureEpisodeForLevelAsync(nextLevel);
            }

            // ROLLBACK_LOADTIME_CATALOG_NONBLOCKING_20260615: START
            // 로비 진입 게이트에서 ShopCatalog(13개)·IAP 를 제외(둘 다 샵 전용 — 백그라운드 init + 클릭 시 retry).
            // 온보딩(Lv.5 미클리어)은 여기서 InGame 으로 직행하므로 '에피소드 prefetch' 만 대기한다(첫 레벨 데이터 필요).
            // 타임아웃도 8s→5s. 효과: 로딩 −(카탈로그/IAP 대기분).
            // 롤백: shopOk/iapOk 조건을 epOk 와 다시 AND 로 묶고 TIMEOUT 8f 로 복원.
            // ROLLBACK_RELEASE_TITLE_LOADTIME_20260616:
            // Episode prefetch is a convenience warm-up; if it misses, LevelManager retries
            // when play starts. Keep Title responsive on release builds.
            // Rollback: restore TIMEOUT to 5f for longer prefetch waiting.
            const float TIMEOUT = 2f;
            float t = 0f;
            while (t < TIMEOUT)
            {
                if (episodeTask == null || episodeTask.IsCompleted)
                {
                    if (episodeTask != null && episodeTask.IsCompletedSuccessfully && !episodeTask.Result)
                        Debug.LogWarning($"[TitleController] 에피소드 prefetch 실패 (level {nextLevel}). 게임은 진행 — LevelManager 가 폴백 처리.");
                    yield break;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            // ROLLBACK_LOADTIME_CATALOG_NONBLOCKING_20260615: END

            if (episodeTask != null && !episodeTask.IsCompleted)
                Debug.LogWarning($"[TitleController] 에피소드 prefetch timeout (level {nextLevel}). 게임 진입 후 LevelManager 가 캐시 miss 시 다시 시도.");
        }

        /// <summary>Returns true only after IAPManager exists and Unity IAP finished initialization.</summary>
        private static bool IsIapReadyForShop()
        {
            return IAPManager.HasInstance && IAPManager.Instance.IsInitialized();
        }

        /// <summary>로딩 완료 시 1회 호출 — 온보딩(Lv.5 미클리어) 세션이면 인게임으로 직행, 그 외엔 Lobby.</summary>
        private void Enter()
        {
            if (_entered) return;
            _entered = true;
            if (!GameManager.HasInstance) return;

            if (ShouldEnterFirstLevel())
            {
                EmitBootCp("enter_firstlevel", 7); // ROLLBACK_BOOT_CHECKPOINTS_20260713
                // 스플래시 유지: 스플래시 배경을 전환 오버레이로 그대로 이어 보여주며 InGame 진입.
                // 별도 전환 이미지를 띄우지 않아 splash→레벨 사이 단색/이중 노출이 없음.
                if (_ui != null && _ui.SplashSprite != null)
                    GameManager.Instance.SetTransitionImage(_ui.SplashSprite);

                // 사용자가 이전 세션에서 Lv.3까지 깼다면 Lv.4부터 시작. 온보딩 범위(1~5) 내로 클램프.
                int resumeLevel = Mathf.Clamp(GetHighestClearedLevel() + 1, 1, FtueGate.ONBOARDING_CLEAR_LEVEL);
                GameManager.Instance.StartLevel(resumeLevel);
            }
            else
            {
                EmitBootCp("enter_lobby", 7); // ROLLBACK_BOOT_CHECKPOINTS_20260713
                GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            }
        }

        /// <summary>온보딩 미완료(Lv.5 미클리어) 세션이면 인게임 직행. 결정 기준은 오직 highestClearedLevel 하나.</summary>
        private bool ShouldEnterFirstLevel()
        {
            return GetHighestClearedLevel() < FtueGate.ONBOARDING_CLEAR_LEVEL;
        }

        /// <summary>온보딩 진행도 단일 소스 = FtueGate(PlayerPrefs 기반). UserDataService.highestClearedLevel은 현재 미사용 — Firestore 동기화는 별도 작업.</summary>
        private static int GetHighestClearedLevel()
        {
            return FtueGate.HighestClearedLevel;
        }

        // ROLLBACK_BOOT_CHECKPOINTS_20260713: 부팅 체크포인트 1건 발화(경과 ms + 네트워크 도달 여부 포함).
        //   session_start(앱 열기)→각 로딩step→loading_complete→enter→play_start(레벨 로드) 퍼널로,
        //   "설치 후 첫 레벨 미도달" 유저가 어느 단계서 이탈하는지 BQ 에서 stage_index MAX 로 규명.
        private void EmitBootCp(string stage, int stageIndex)
        {
            if (!BOOT_CHECKPOINTS_ENABLED) return;
            int elapsedMs = Mathf.Max(0, Mathf.RoundToInt((Time.realtimeSinceStartup - _loadingFlowStartTime) * 1000f));
            bool net = Application.internetReachability != NetworkReachability.NotReachable;
            Analytics.AnalyticsSessionTracker.EmitBootCheckpoint(stage, stageIndex, elapsedMs, net);
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
