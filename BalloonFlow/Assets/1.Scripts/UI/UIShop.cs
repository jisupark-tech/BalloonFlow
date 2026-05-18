using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Shop page — spawned inside UILobby PageContainer (left page).
    /// 상품 리스트는 PopupShopListItem 프리팹으로 동적 생성.
    /// Inspector 의 _products 가 비어있으면 BuildDefaultTempProducts() 임시 데이터 사용.
    /// 구매는 ShopManager.PurchaseProduct 로 라우팅.
    /// 구매 성공 popup은 IAP 성공 이벤트(OnPurchaseRewardGranted)를 받은 PurchaseRewardEffect가 spinner 닫은 후 표시한다.
    /// UI 스크립트는 생성·파괴 주기가 짧아 Singleton anti-pattern. Manager 계층만 Singleton 사용.
    /// </summary>
    public class UIShop : UIBase
    {
        [Header("[Shop Title]")]
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[Content — ScrollView]")]
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private Button _btnMoreProducts;
        [Tooltip("BtnMoreProducts 부모 컨테이너 GameObject — 클릭 후 전체 숨김 대상. 미할당 시 _btnMoreProducts.transform.parent.gameObject 폴백.")]
        [SerializeField] private GameObject _btnMoreProductsRoot;

        [Header("[List Item Prefab — 카테고리별]")]
        [Tooltip("Resources/UI/UIAssets/ShopListGold")]
        [SerializeField] private GameObject _prefabGold;
        [Tooltip("Resources/UI/UIAssets/ShopListGoldAlign.prefab (코인 상품들을 가로 정렬해 담는 컨테이너)")]
        [SerializeField] private GameObject _prefabGoldAlign;
        [Tooltip("Resources/UI/UIAssets/ShopListItem.prefab (일반/특가/번들/부스터)")]
        [SerializeField] private GameObject _prefabGeneral;
        [Tooltip("Resources/UI/UIAssets/ShopListAd.prefab")]
        [SerializeField] private GameObject _prefabAd;

        [Tooltip("Inspector 미할당 시 Resources 폴백 자동 로드.")]
        [SerializeField] private bool _autoLoadFromResources = true;

        [Header("[상품 데이터 — 비어있으면 임시 데이터 사용]")]
        [SerializeField] private ShopProductData[] _products;

        public RectTransform ContentRoot => _contentRoot;

        // 부모 컨테이너 전체 비활성/활성 — 미할당 시 Button의 부모로 자동 폴백.
        private GameObject MoreButtonRoot
        {
            get
            {
                if (_btnMoreProductsRoot != null) return _btnMoreProductsRoot;
                if (_btnMoreProducts != null && _btnMoreProducts.transform.parent != null)
                    return _btnMoreProducts.transform.parent.gameObject;
                return _btnMoreProducts != null ? _btnMoreProducts.gameObject : null;
            }
        }

        private const int ITEMS_PER_PAGE = 6;
        private const float DEFAULT_ITEM_HEIGHT = 200f;

        [Header("[Layout]")]
        [Tooltip("동적 아이템에 적용할 preferredHeight (LayoutElement). 프리팹 자체 size 사용 시 0.")]
        [SerializeField] private float _itemHeightOverride = 0f;

        [Header("[Appear Animation]")]
        [SerializeField] private float _itemAppearScaleDuration = 0.3f;
        [SerializeField] private float _itemAppearStagger = 0.06f;
        [SerializeField] private Ease _itemAppearEase = Ease.OutBack;

        private int _displayedCount;
        private bool _userExpandedMore;
        private readonly List<PopupShopListItem> _spawnedItems = new List<PopupShopListItem>();
        private int _lastLoadFrame = int.MinValue;

        // ScrollRect 캐시 — onValueChanged 리스너 등록/해제 + viewport overlap 컬링 기준점.
        private ScrollRect _scrollRect;
        private RectTransform _viewport;
        private bool _scrollListenerRegistered;

        protected override void Awake()
        {
            base.Awake();
            if (_txtTitle != null) _txtTitle.text = "Shop";
            if (_txtTitleOutline != null) _txtTitleOutline.text = "Shop";

            if (_btnMoreProducts != null)
                _btnMoreProducts.onClick.AddListener(OnMoreProductsClicked);

            // Resources 폴백 — Inspector 미할당 시 prefab 자동 로드
            if (_autoLoadFromResources)
            {
                if (_prefabGold == null)
                    _prefabGold = Resources.Load<GameObject>("UI/UIAssets/ShopListGold");
                if (_prefabGoldAlign == null)
                    _prefabGoldAlign = Resources.Load<GameObject>("UI/UIAssets/ShopListGoldAlign");
                if (_prefabGeneral == null)
                    _prefabGeneral = Resources.Load<GameObject>("UI/UIAssets/ShopListItem");
                if (_prefabAd == null)
                    _prefabAd = Resources.Load<GameObject>("UI/UIAssets/ShopListAd");
            }

            // 컨텐츠 루트의 VerticalLayoutGroup + ContentSizeFitter 보장 (UILobby 가
            // 미리 처리하지만, prefab 직접 띄우는 케이스 대비 fallback)
            EnsureContentLayout();

            // Firestore 카탈로그 우선. 매니저 미준비/실패 시 임시 데이터 fallback.
            SubscribeToCatalog();
        }

        private void OnDestroy()
        {
            if (ShopCatalogService.HasInstance)
                ShopCatalogService.Instance.OnCatalogLoaded -= OnCatalogReady;

            if (_scrollRect != null && _scrollListenerRegistered)
            {
                _scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
                _scrollListenerRegistered = false;
            }
        }

        /// <summary>ShopCatalogService 구독. 이미 로드 상태면 즉시 적용. 매니저 부재 시 fallback.</summary>
        private void SubscribeToCatalog()
        {
            // [2026-05-12] Awake 의 ResetAndLoadProducts 호출 제거 — spawn 은 ResetView (page 진입) 시점만.
            // 증상: Shop 진입 시 연출 2번 (Awake 의 spawn + ResetView 의 spawn).
            // Fix: Awake 는 data 만 set, spawn 안 함. catalog 로드 시 OnCatalogReady 가 Shop active 일 때만 spawn.
            if (ShopCatalogService.HasInstance)
            {
                ShopCatalogService.Instance.OnCatalogLoaded += OnCatalogReady;
                if (ShopCatalogService.Instance.IsLoaded)
                {
                    // 데이터만 set, spawn X (page 진입 시 ResetView 가 호출)
                    var user = (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                        ? UserDataService.Instance.CurrentUser
                        : null;
                    var visible = ShopCatalogService.Instance.GetVisibleForUser(user);
                    _products = visible.Select(ConvertDocToData).ToArray();
                }
                else
                {
                    // 로딩 대기 중. 임시 데이터 set 만 (spawn 은 ResetView 가 함)
                    if (_products == null || _products.Length == 0)
                        _products = BuildDefaultTempProducts();
                }
            }
            else
            {
                // 매니저 없음 (Editor 스탠드얼론 테스트 등) — 임시 데이터 set 만
                if (_products == null || _products.Length == 0)
                    _products = BuildDefaultTempProducts();
            }
        }

        /// <summary>Firestore 카탈로그 로드 완료 시 실행. UserData 기준 필터 + 변환 + 재구성.</summary>
        private void OnCatalogReady()
        {
            var user = (UserDataService.HasInstance && UserDataService.Instance.IsReady)
                ? UserDataService.Instance.CurrentUser
                : null;

            var visible = ShopCatalogService.Instance.GetVisibleForUser(user);
            _products = visible.Select(ConvertDocToData).ToArray();
            Debug.Log($"[UIShop] Catalog loaded — {_products.Length} products visible.");

            // [2026-05-12] Shop page active 일 때만 즉시 spawn. 비활성이면 다음 ResetView 호출 시 spawn.
            // 증상 방지: Awake 시점 spawn + 사용자 Shop 클릭 시 spawn = 2번 연출.
            if (gameObject.activeInHierarchy)
                ResetAndLoadProducts();
        }

        /// <summary>ShopProductDoc(서버 모델) → ShopProductData(UI 모델) 변환.</summary>
        private static ShopProductData ConvertDocToData(ShopProductDoc doc)
        {
            var category = MapCategory(doc.category);
            string title;
            if (!string.IsNullOrEmpty(doc.title_loc_key))
            {
                title = doc.title_loc_key;
            }
            else if (category == ShopItemCategory.Gold && doc.rewards != null && doc.rewards.coins > 0)
            {
                // coin 카테고리는 title_loc_key 비어있을 때 productId 대신 코인 수량 표시 — ShopListGold prefab의 TextPrice가 _txtTitle 슬롯에 wire 되어 있어 발생한 productId 노출 회귀 fix.
                title = doc.rewards.coins.ToString("N0");
            }
            else
            {
                title = doc.productId;
            }

            return new ShopProductData
            {
                productId        = doc.productId,
                title            = title,
                price            = $"${doc.priceUsd:F2}",
                hasDiscount      = doc.discountPercent > 0,
                discountPercent  = doc.discountPercent,
                hasTimeLimit     = doc.hasTimeLimit,
                timeLimitSeconds = doc.timeLimitSeconds,
                category         = category,
                imageKey         = doc.imageKey,
                goldIconKey      = doc.goldIconKey ?? string.Empty,
                rewards          = doc.rewards   // 동적 보상 표시용
            };
        }

        /// <summary>Firestore /products 의 카테고리 문자열 → UI prefab 분기 enum.
        /// 실제 시드 카테고리: coin / bundle / noads / offer (1.0 기준).</summary>
        private static ShopItemCategory MapCategory(string cat)
        {
            if (string.IsNullOrEmpty(cat)) return ShopItemCategory.General;
            switch (cat.ToLowerInvariant())
            {
                case "coin":   return ShopItemCategory.Gold;    // ShopListGold prefab
                case "noads":  return ShopItemCategory.Ad;      // ShopListAd prefab
                case "bundle":
                case "offer":
                default:       return ShopItemCategory.General; // ShopListItem prefab
            }
        }

        /// <summary>
        /// _contentRoot 의 VerticalLayoutGroup + ContentSizeFitter 가 설정돼있는지 확인.
        /// 없으면 자동 추가 — 동적 아이템이 뭉치는 문제 방지.
        /// BtnMoreProducts 도 _contentRoot 자식이면 LayoutElement 보장 + Awake 단계부터
        /// last sibling 강제. 이후 모든 분기(catalog 로드/dynamic spawn/재로드)에서도 유지.
        /// </summary>
        private void EnsureContentLayout()
        {
            if (_contentRoot == null) return;

            // Anchor 보정 — Top stretch
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(1f, 1f);
            _contentRoot.pivot     = new Vector2(0.5f, 1f);

            // VerticalLayoutGroup
            var vlg = _contentRoot.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = _contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            if (vlg.spacing < 1f) vlg.spacing = 20f;
            // 스크롤 끝에 마지막 카드가 붙지 않도록 breathing space — UIShop scroll 답답함 fix
            if (vlg.padding.bottom < 500) vlg.padding.bottom = 500;

            // ContentSizeFitter
            var csf = _contentRoot.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = _contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // ScrollRect 스크롤감 보정 — inertia + elastic + 적절한 sensitivity
            var sr = _contentRoot.GetComponentInParent<ScrollRect>();
            if (sr != null)
            {
                sr.horizontal = false;
                sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Elastic;
                sr.elasticity = 0.1f;          // 끝에서 부드럽게 튕김
                sr.inertia = true;             // 손가락 떼고도 관성 스크롤
                sr.decelerationRate = 0.135f;  // Unity 기본값 (관성 감속)
                if (sr.scrollSensitivity < 30f) sr.scrollSensitivity = 60f;

                // viewport overlap 컬링 기준점 캐시 + onValueChanged 1회 등록.
                _scrollRect = sr;
                _viewport = sr.viewport != null ? sr.viewport : sr.transform as RectTransform;
                if (!_scrollListenerRegistered)
                {
                    sr.onValueChanged.AddListener(OnScrollValueChanged);
                    _scrollListenerRegistered = true;
                }
            }

            // BtnMoreProducts 진단 + LayoutElement 보장
            if (_btnMoreProducts == null)
            {
                Debug.LogWarning("[UIShop] _btnMoreProducts 미할당 — Inspector 에서 More 버튼 드래그 필요. " +
                                 "(없으면 페이지네이션 비활성)");
                return;
            }

            var btnRT = _btnMoreProducts.transform as RectTransform;
            if (btnRT == null) return;

            // _contentRoot 자식이 아니면 경고 (VLG 가 처리 안 함)
            if (btnRT.parent != _contentRoot)
            {
                Debug.LogWarning($"[UIShop] _btnMoreProducts ({btnRT.name}) 가 _contentRoot 자식이 아님 — " +
                                 "VerticalLayoutGroup 처리 안 됨. 부모를 _contentRoot 로 옮기세요.");
                return;
            }

            // VLG 가 height 를 자동 0으로 처리하지 않도록 LayoutElement 부착 보장
            var le = _btnMoreProducts.GetComponent<LayoutElement>();
            if (le == null) le = _btnMoreProducts.gameObject.AddComponent<LayoutElement>();
            if (le.preferredHeight <= 0f)
            {
                float h = btnRT.rect.height;
                le.preferredHeight = h > 1f ? h : 120f; // 더보기 버튼 기본 높이
            }

            // Awake 단계부터 last sibling 강제 — 정적 placeholder 가 ShopContent 안에 있어도
            // BtnMore 가 항상 스크롤 최하단에 위치. (parent != _contentRoot 면 designer 의도일
            // 수 있으므로 reparent 는 하지 않고 위 LogWarning 에서 종료.)
            _btnMoreProducts.transform.SetAsLastSibling();
        }

        private GameObject GetPrefabForCategory(ShopItemCategory cat)
        {
            switch (cat)
            {
                case ShopItemCategory.Gold:    return _prefabGold != null ? _prefabGold : _prefabGeneral;
                case ShopItemCategory.Ad:      return _prefabAd != null ? _prefabAd : _prefabGeneral;
                case ShopItemCategory.General:
                default:                        return _prefabGeneral;
            }
        }

        // [2026-05-12] ResetView 자체 dedup — UILobby.GoToPage 즉시 호출 + AnimateToPage tween OnComplete → OnPageArrived 가 ResetView 두 번 호출.
        // tween duration (~0.3s ≈ 18 frame) 보다 큰 60 frame range 로 차단. 빠른 page 전환 시 stale 안 됨 (1초 차단 후 통과).
        // 초기값 -1 + `>= 0` check — int.MinValue 시 Time.frameCount - MinValue overflow 위험 회피 (첫 호출 차단 버그).
        private int _lastResetViewFrame = -1;
        private const int RESET_VIEW_DEDUP_FRAMES = 60;

        /// <summary>유저가 다른 페이지에서 Shop 탭으로 재진입할 때 호출 — 더보기 상태/리스트/스크롤 위치를 초기 상태로 되돌린다.</summary>
        public void ResetView()
        {
            if (_lastResetViewFrame >= 0 && Time.frameCount - _lastResetViewFrame < RESET_VIEW_DEDUP_FRAMES) return;
            _lastResetViewFrame = Time.frameCount;

            LogContentState("ResetView 진입");

            _userExpandedMore = false;

            // [2026-05-13] anchor/VLG/CSF/padding 매 진입 시 재적용 — 탭 재진입 시 Content Height 가 줄어드는 이슈 fix.
            // Awake 1회만 호출하면 다른 컴포넌트가 anchor/sizeDelta 를 덮어쓰거나 padding 이 초기화되는
            // 경우 ContentSizeFitter 의 preferredSize 계산이 작아질 수 있음. 멱등 호출이라 안전.
            EnsureContentLayout();

            // 누적된 스크롤 오프셋 wipe — 탭 재진입 시 상단 공백 fix
            if (_contentRoot != null)
                _contentRoot.anchoredPosition = Vector2.zero;

            ResetAndLoadProducts();

            // [2026-05-12] LayoutRebuilder 강제 — VerticalLayoutGroup + ContentSizeFitter 의 height 즉시 정착.
            // 증상: More 누른 후 다른 탭 → Shop 재진입 시 리스트가 하단으로 내려와 정렬. 원인: layout 미정착 상태로
            //       scroll reset → ContentSizeFitter 가 다음 frame 에 height 계산 → ScrollRect 의 content rect 변경 시
            //       normalizedPosition 1f 가 다른 위치로 매핑. ForceRebuildLayoutImmediate 로 즉시 정착 후 reset.
            if (_contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            // ScrollRect 내부 viewport/content 캐시 flush — 새 content size 기반 normalizedPosition 보장
            Canvas.ForceUpdateCanvases();

            // 캔버스 갱신 직후 viewport rect 기준으로 카드 particle 컬링 1회 재평가.
            RefreshAllParticleLights();

            ApplyScrollTop();

            // [2026-05-12] 다음 frame 에 scroll 재reset — ContentSizeFitter 가 다음 frame 에 height 갱신할 수도 있어 이중 보호.
            StartCoroutine(DelayedScrollReset());
        }

        /// <summary>ScrollRect + content anchoredPosition 을 상단으로 강제.</summary>
        private void ApplyScrollTop()
        {
            if (_contentRoot == null) return;
            var sr = _contentRoot.GetComponentInParent<ScrollRect>();
            if (sr != null)
            {
                sr.StopMovement();
                sr.verticalNormalizedPosition = 1f;
            }
            _contentRoot.anchoredPosition = Vector2.zero;
        }

        private System.Collections.IEnumerator DelayedScrollReset()
        {
            yield return null; // 1 frame 대기 — ContentSizeFitter / LayoutGroup 최종 정착
            if (_contentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
                Canvas.ForceUpdateCanvases();
                ApplyScrollTop();
            }
            LogContentState("DelayedScrollReset 완료(layout 정착)");
        }

        // [2026-05-13] ShopContent height 줄어듬 진단용 — Top stretch anchor, VLG/CSF/padding, spawned items 수 기록.
        // 첫 진입 vs 재진입 로그 비교로 어떤 값이 달라지는지 즉시 확인 가능. 추후 원인 확정되면 제거.
        private void LogContentState(string tag)
        {
            if (_contentRoot == null) { Debug.Log($"[UIShop][{tag}] _contentRoot=null"); return; }
            var vlg = _contentRoot.GetComponent<VerticalLayoutGroup>();
            var csf = _contentRoot.GetComponent<ContentSizeFitter>();
            string vlgInfo = vlg != null
                ? $"spacing={vlg.spacing} pad(L{vlg.padding.left}/R{vlg.padding.right}/T{vlg.padding.top}/B{vlg.padding.bottom}) ctrlH={vlg.childControlHeight} expandH={vlg.childForceExpandHeight}"
                : "VLG=null";
            string csfInfo = csf != null ? $"vFit={csf.verticalFit}" : "CSF=null";
            Debug.Log($"[UIShop][{tag}] " +
                      $"rect.h={_contentRoot.rect.height:F1} sizeDelta={_contentRoot.sizeDelta} " +
                      $"anchorMin={_contentRoot.anchorMin} anchorMax={_contentRoot.anchorMax} pivot={_contentRoot.pivot} " +
                      $"anchoredPos={_contentRoot.anchoredPosition} childCount={_contentRoot.childCount} spawned={_spawnedItems.Count} " +
                      $"VLG[{vlgInfo}] CSF[{csfInfo}]");
        }

        /// <summary>상품 리스트 초기화 + 첫 페이지 로드.</summary>
        private void ResetAndLoadProducts()
        {
            // 같은 프레임 중복 호출 방지 (Awake 경로 + UILobby.ResetView 동시 트리거 시 등장 연출 1회만)
            if (_lastLoadFrame == Time.frameCount) return;
            _lastLoadFrame = Time.frameCount;

            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                if (_spawnedItems[i] != null && _spawnedItems[i].gameObject != null)
                    Destroy(_spawnedItems[i].gameObject);
            }
            _spawnedItems.Clear();
            _displayedCount = 0;

            LoadMoreProducts();
            UpdateMoreButton();
        }

        /// <summary>사용자가 BtnMoreProducts를 직접 클릭했을 때만 호출 — 추가 로드 후 버튼 영구 숨김.</summary>
        private void OnMoreProductsClicked()
        {
            _userExpandedMore = true;
            // More 버튼 1회 클릭 시 남은 전체 로드 (6번째 coin 이상 잔존 상품 누락 fix)
            LoadMoreProducts(_products != null ? _products.Length - _displayedCount : -1);
            // 부모 컨테이너 전체 비활성 — Button GameObject 단독이 아닌 root 기준.
            if (MoreButtonRoot != null && MoreButtonRoot.activeSelf)
                MoreButtonRoot.SetActive(false);
        }

        /// <summary>다음 페이지 상품 추가. 카테고리별 prefab 자동 선택.
        /// 각 아이템에 LayoutElement 자동 부착 (preferredHeight) → VerticalLayoutGroup 정상 배치.
        /// 끝에 LayoutRebuilder 호출 → ScrollRect Content 크기 갱신.</summary>
        private void LoadMoreProducts(int loadOverride = -1)
        {
            if (_products == null || _contentRoot == null) return;

            GameObject goldContainer = null;
            int appearOrder = 0;

            int remaining = _products.Length - _displayedCount;
            int loadCount = (loadOverride > 0) ? Mathf.Min(loadOverride, remaining) : Mathf.Min(ITEMS_PER_PAGE, remaining);
            for (int i = 0; i < loadCount; i++)
            {
                int idx = _displayedCount + i;
                var data = _products[idx];

                if (data.category == ShopItemCategory.Gold && _prefabGoldAlign != null && _prefabGold != null)
                {
                    // Gold 그룹 컨테이너 1개를 만들고 코인 상품들을 그 자식으로 spawn.
                    if (goldContainer == null)
                    {
                        goldContainer = Instantiate(_prefabGoldAlign, _contentRoot);
                        goldContainer.SetActive(true);

                        // 부모 컨테이너 root 기준으로 sibling 정렬 — Blue 자식이 _contentRoot 직속이 아니므로 root 사용.
                        if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                            goldContainer.transform.SetSiblingIndex(MoreButtonRoot.transform.GetSiblingIndex());

                        var containerRT = goldContainer.transform as RectTransform;
                        var containerLE = goldContainer.GetComponent<LayoutElement>();
                        if (containerLE == null) containerLE = goldContainer.AddComponent<LayoutElement>();
                        if (containerLE.preferredHeight <= 0f)
                        {
                            if (_itemHeightOverride > 0f)
                                containerLE.preferredHeight = _itemHeightOverride;
                            else if (containerRT != null && containerRT.rect.height > 1f)
                                containerLE.preferredHeight = containerRT.rect.height;
                            else
                                containerLE.preferredHeight = DEFAULT_ITEM_HEIGHT;
                        }

                        // prefab 에 미리 박혀있는 placeholder 자식(ShopListGold(1)/(2)) 제거 —
                        // 데이터 없는 빈 카드 노출 방지.
                        for (int c = goldContainer.transform.childCount - 1; c >= 0; c--)
                            Destroy(goldContainer.transform.GetChild(c).gameObject);

                        PlayAppearAnimation(goldContainer.transform, appearOrder++);
                    }

                    var goldGo = Instantiate(_prefabGold, goldContainer.transform);
                    goldGo.SetActive(true);
                    // [2026-05-13] 동적 spawn item 의 Buy 버튼 등에 더블 클릭 가드 부착.
                    UIButtonClickGuard.AttachToHierarchy(goldGo);

                    var goldItem = goldGo.GetComponent<PopupShopListItem>();
                    if (goldItem != null)
                    {
                        goldItem.Setup(data, OnProductBuy);
                        _spawnedItems.Add(goldItem);
                    }
                    else
                    {
                        Debug.LogWarning($"[UIShop] {_prefabGold.name} 에 PopupShopListItem 컴포넌트 없음 — Setup 호출 불가. " +
                                         "Inspector에서 카드 자체 컴포넌트로 attach 필요.");
                    }
                    continue;
                }

                // 비-Gold 카테고리: 기존 직접-spawn 경로 유지.
                goldContainer = null;

                GameObject prefab = GetPrefabForCategory(data.category);
                if (prefab == null) continue; // prefab 미할당 → skip

                var go = Instantiate(prefab, _contentRoot);
                go.SetActive(true);
                // [2026-05-13] 동적 spawn item 의 Buy 버튼 등에 더블 클릭 가드 부착.
                UIButtonClickGuard.AttachToHierarchy(go);

                PlayAppearAnimation(go.transform, appearOrder++);

                // 부모 컨테이너 root 기준으로 그 직전에 배치 (스크롤 끝에 더보기 유지)
                if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                    go.transform.SetSiblingIndex(MoreButtonRoot.transform.GetSiblingIndex());

                // VerticalLayoutGroup 이 size 줄 수 있도록 LayoutElement 보장
                var rt = go.transform as RectTransform;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                if (le.preferredHeight <= 0f)
                {
                    if (_itemHeightOverride > 0f)
                        le.preferredHeight = _itemHeightOverride;
                    else if (rt != null && rt.rect.height > 1f)
                        le.preferredHeight = rt.rect.height;
                    else
                        le.preferredHeight = DEFAULT_ITEM_HEIGHT;
                }

                var item = go.GetComponent<PopupShopListItem>();
                if (item != null)
                {
                    item.Setup(data, OnProductBuy);
                    _spawnedItems.Add(item);
                }
                else
                {
                    Debug.LogWarning($"[UIShop] {prefab.name} 에 PopupShopListItem 컴포넌트 없음 — Setup 호출 불가. " +
                                     "Inspector에서 카드 자체 컴포넌트로 attach 필요.");
                }
            }

            _displayedCount += loadCount;

            // 동적 spawn 후에도 부모 컨테이너 root 가 항상 마지막 sibling 이 되도록 한 번 더 보정.
            if (MoreButtonRoot != null && MoreButtonRoot.transform.parent == _contentRoot)
                MoreButtonRoot.transform.SetAsLastSibling();

            // VerticalLayoutGroup + ContentSizeFitter 강제 재계산 → ScrollRect 활성
            if (_contentRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            // 레이아웃 확정 후 카드 rect 가 정확해진 시점에 1회 컬링 갱신.
            RefreshAllParticleLights();

            UpdateMoreButton();
        }

        /// <summary>ScrollRect.onValueChanged 콜백 — 스크롤마다 viewport 안/밖 카드 particle 컬링 갱신.</summary>
        private void OnScrollValueChanged(Vector2 _)
        {
            RefreshAllParticleLights();
        }

        /// <summary>spawn 직후 또는 스크롤 시 viewport 기준으로 각 카드의 _particleLight 활성 여부 결정.</summary>
        private void RefreshAllParticleLights()
        {
            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                var item = _spawnedItems[i];
                if (item == null) continue;
                item.RefreshParticleLightVisibility(_viewport);
            }
        }

        // ShopContent 행 단위 순차 등장 — Gold 그룹은 컨테이너 1개를 1행으로 묶어 한 번만 stagger.
        private void PlayAppearAnimation(Transform target, int orderIndex)
        {
            if (target == null) return;
            target.localScale = Vector3.zero;
            target.DOScale(Vector3.one, _itemAppearScaleDuration)
                  .SetEase(_itemAppearEase)
                  .SetDelay(orderIndex * _itemAppearStagger)
                  .SetLink(target.gameObject);
        }

        /// <summary>
        /// BtnMoreProducts 노출 정책 — 클릭 후에는 영구히 숨김.
        /// Awake 단계부터 last sibling 강제, 이후 모든 분기(활성/비활성)에서도 유지하여
        /// 다음 ResetAndLoadProducts 시 sibling 위치가 안정되도록 보장.
        /// </summary>
        private void UpdateMoreButton()
        {
            if (_btnMoreProducts == null) return;
            var root = MoreButtonRoot;
            if (_userExpandedMore)
            {
                // 부모 컨테이너 전체 비활성.
                if (root != null && root.activeSelf)
                    root.SetActive(false);
                // 비활성화돼도 마지막 sibling 위치 유지 — 다음 reload 안정.
                if (root != null && root.transform.parent == _contentRoot)
                    root.transform.SetAsLastSibling();
                return;
            }
            // 부모 컨테이너 전체 활성.
            if (root != null && !root.activeSelf)
                root.SetActive(true);
            if (root != null && root.transform.parent == _contentRoot)
                root.transform.SetAsLastSibling();
        }

        /// <summary>상품 구매 콜백 → 확인 popup 생략하고 즉시 로딩 스피너 + ShopManager 라우팅.</summary>
        private void OnProductBuy(ShopProductData product)
        {
            Debug.Log($"[UIShop] Buy clicked: {product.productId}, {product.title}, {product.price}");
            ProceedPurchase(product);
        }

        /// <summary>실제 구매 라우팅 — 확인 popup 의 Yes 콜백. Pre-routing 으로 로딩 스피너 노출(IAP 응답 대기 동안 입력 차단 + 시각적 피드백).
        /// 결제 성공 PopupError 표시는 PurchaseRewardEffect.HandleReward 가 spinner 를 닫은 후 단일 경로로 수행한다.
        /// 사용자 요구 순서: BuyClick → PopupLoadingSpinner(이 메서드) → IAP → PopupError(PurchaseRewardEffect.HandleReward 에서 spinner.SetCloseCallback 경유). 이 메서드는 1·2단계만 담당.</summary>
        private void ProceedPurchase(ShopProductData product)
        {
            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupLoadingSpinner>(Const.POPUP_LOADING_SPINNER);

            if (ShopManager.HasInstance)
                ShopManager.Instance.PurchaseProduct(product.productId);
        }

        /// <summary>
        /// Firestore /products fetch 가 미준비/실패한 짧은 시점에 빈 배열 반환.
        /// 실제 카탈로그는 ShopCatalogService 가 fetch 완료 시 OnCatalogReady 에서 채움.
        /// (이전 placeholder 들은 옛 ID 라 구매 시 IAPManager lookup 실패하므로 제거)
        /// </summary>
        private static ShopProductData[] BuildDefaultTempProducts()
        {
            return System.Array.Empty<ShopProductData>();
        }
    }
}
