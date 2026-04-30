# Addressables 도입 가이드 — BalloonFlow

## 현재 상태

- `com.unity.addressables 2.7.6` 설치됨
- `AddressableSystem.cs` (런타임 wrapper) — Init / LoadAsset / Instantiate / Download / Release
- `Const.cs` — `ADDR_LABEL_*` 상수 영역 (CORE / UI / CDM / BGM / SFX)
- `AddressableSetupTool.cs` (Editor) — 그룹/라벨 자동 생성 + 우클릭 메뉴
- `TitleController` — "Downloading data..." 단계가 `AddressableSystem.DownloadDependenciesAsync(ADDR_LABEL_CDM)` 호출

## 1회 셋업 (사용자가 Unity 에디터에서)

1. **Window > Asset Management > Addressables > Groups** 한 번 열기 (AddressableAssetSettings 자동 생성됨, 이미 있으면 skip)
2. **메뉴 BalloonFlow > Addressables > Setup Groups & Labels** 실행
   - 그룹 3개 생성: `Local_Always`, `Local_OnDemand`, `Remote_CDM`
   - 라벨 5개 등록: core / ui / cdm / bgm / sfx
   - Remote_CDM 그룹은 BuildPath/LoadPath 가 RemoteBuildPath/RemoteLoadPath 로 설정됨
3. **Profile 설정** — Window > Asset Management > Addressables > Profiles
   - Default profile 의 `RemoteLoadPath` 를 실제 CDN URL 로 (e.g. `https://cdn.aimed.xyz/balloonflow/[BuildTarget]`)
   - 빌드 시 RemoteBuildPath 의 결과물을 CDN 에 업로드하는 워크플로 필요

## 마이그레이션 패턴 — SpriteAtlas 기반

BalloonFlow 의 UI sprite 들은 `Assets/4.Atlas/UI.spriteatlas` 1개 atlas 에 다 묶여있음.
개별 sprite 가 아니라 **atlas 단위로 Addressable 등록** + **런타임에 atlas 에서 이름으로 추출**.

### 1회 자동 마이그레이션

```
메뉴: BalloonFlow > Addressables > Auto-Migrate Sprite Atlases
```
동작:
- `Assets/4.Atlas/*.spriteatlas` 모두 `Local_OnDemand` 그룹 + `ui` 라벨 등록
- Address(key) = `atlas_<name>` (예: UI.spriteatlas → `atlas_ui`)
- 각 atlas 의 sprite 이름들 enumerate → Const.cs 의 ADDR_ATLAS_*, SPR_* 자동 생성
- atlas 추가/sprite 추가될 때마다 메뉴 재실행하면 Const 갱신

### 코드 사용

**Before:**
```csharp
[SerializeField] private Sprite _iconHand;
// prefab 에서 직접 Sprite 드래그
```

**After:**
```csharp
private Sprite _iconHand;

private async void Start()
{
    _iconHand = await AddressableSystem.GetSpriteAsync(
        Const.ADDR_ATLAS_UI, Const.SPR_ICONHAND);
    if (_iconHand != null) _imageComponent.sprite = _iconHand;
}
```

장점:
- Atlas 1개만 메모리에 로드되어 그 안의 모든 sprite 즉시 접근 가능
- Inspector 에서 prefab 의 Sprite 참조 안 끊어도 됨 — 점진 마이그레이션 가능
- atlas 자체 한 번 캐시되면 같은 atlas 의 다른 sprite 호출은 즉시 반환 (`AddressableSystem.GetSpriteAsync` 가 atlas 캐싱)

### B. Asset 을 Addressable 로 등록

방법 1 — 에디터 메뉴:
- Project 창에서 asset 우클릭 > **Addressables > Mark as Local_OnDemand (ui)** (또는 Local_Always / Remote_CDM)
- 또는 Addressables Groups 창에서 직접 드래그

방법 2 — 일괄 (Editor 스크립트):
- 폴더 선택 후 우클릭 메뉴 사용

### C. Resources/ 에서 빼내기

- 일반 sprite/audio 는 `Assets/Resources/` 밖으로 이동 → Addressables 가 빌드에 포함
- prefab 도 마찬가지. 기존 `Resources.Load<GameObject>(path)` 호출은 `AddressableSystem.LoadAssetAsync<GameObject>(key)` 로 교체
- 일부 매니저 prefab 은 Resources/ 에 둬도 됨 (Addressable 안 써도 무방. 점진 마이그레이션)

## CDM 워크플로

1. asset 을 Remote_CDM 그룹에 배정 + `cdm` 라벨 부여
2. **Window > Asset Management > Addressables > Groups > Build > New Build > Default Build Script** 실행
   - 결과: `ServerData/[BuildTarget]/` 에 `.bundle` + `catalog_*.json` 생성
3. 그 폴더를 CDN 에 업로드 (CI/CD 또는 수동)
4. Profile 의 `RemoteLoadPath` 가 그 CDN URL 가리키게
5. 클라가 Title 진입 시 `DownloadDependenciesAsync(ADDR_LABEL_CDM)` 으로 다운로드

## TitleController 동작

```
[Step 0 Initializing]   AddressableSystem.InitializeAsync (catalog 로드)
[Step 1 Connecting...]  Firebase Auth ping (TODO)
[Step 2 Downloading...] AddressableSystem.DownloadDependenciesAsync(ADDR_LABEL_CDM, progress)
[Step 3 Loading assets] (필요 시 추가 LoadAsset 호출)
[Step 4 Finalizing]
```

CDM size 0 (모두 cache) 일 땐 0.2s 대기 후 통과. 진행도 표시는 `lastReportedProgress` 를 향후 `_ui.SetSubProgress(p)` 같은 API 로 슬라이더 sub 업데이트 가능.

## 점진 마이그레이션 우선순위

1. **Phase 1 — UI atlas + popup icon ✅ 완료**
2. **Phase 2 — Popup prefab + UI prefab + InGame Prefab ✅ 완료** (도구 자동)
3. **Phase 3 — Audio (BGM/SFX) ✅ 완료** (Remote_CDM, cdm 라벨)
4. **Phase 4 — Material ✅ 완료** (Local_OnDemand, ui 라벨)
5. **Phase 5 — 시즌 콘텐츠** (미래 — Remote_CDM 그룹에 추가만)

## 일괄 마이그레이션 도구

Unity Editor 메뉴:
- **`BalloonFlow > Addressables > Migrate All`** — 모든 폴더 일괄 등록 + Const 자동 생성 (1번 실행)
- 개별: `Migrate Popups` / `Migrate UI` / `Migrate InGame Prefabs` / `Migrate Audio` / `Migrate Materials` / `Regenerate Const Keys`

KEEP (Resources/ 잔존): **UITitle.prefab**, **PopupError.prefab** — 항상 즉시 사용 가능 + Addressables 미준비 상태에서도 동작

## 코드 동작

- **UIManager.OpenUI**: Addressables cache 우선 → Resources.Load 폴백. UITitle/PopupError 는 Resources path 로 그대로 동작
- **ResourceManager.PreloadAddressablePrefabsAsync**: Title Step 0 에서 호출 — `core` + `ui` 라벨 모든 prefab 사전 로드 → cache 키 매핑 (popup_PopupResult ↔ "Popup/PopupResult")
- **ResourceManager.PreloadUIAtlasAsync**: UI atlas 사전 로드 → popup 들이 sync 로 sprite 추출
- **ObjectPoolManager**: 변경 없음 — Prefabs/* 는 Resources/ 에 잔존 + Addressables 동시 등록 (양쪽 모두 작동)
- **AudioManager**: 변경 없음 — Sound/ 는 Resources/ 에 잔존 (audio Addressable 은 6.Sound/ 만, future migration 포인트)

## 주의사항

- LoadAssetAsync 결과는 호출자가 `Release(key)` 책임. 안 그러면 메모리 누수. UI 가 destroy 될 때 OnDestroy 에서 release 권장.
- InstantiateAsync 로 만든 GameObject 는 일반 `Destroy()` 가 아닌 `AddressableSystem.ReleaseInstance(go)` 로 정리.
- 빌드 전 항상 **Build > New Build > Default Build Script** 한 번 실행해서 catalog/bundle 갱신. 안 하면 신규 asset 빌드에 안 들어감.
- Remote_CDM 그룹 사용 시 빌드된 bundle 을 CDN 에 올리지 않으면 클라에서 다운로드 실패.
