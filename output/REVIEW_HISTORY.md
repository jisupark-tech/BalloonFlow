# BalloonFlow 개발 리뷰 히스토리

## 프로젝트 정보
- **프로젝트**: BalloonFlow (풍선 팝 퍼즐 게임)
- **Unity 버전**: 6000.2.7f2 (New Input System)
- **작업 시작**: 2026-03-12
- **현재 Phase**: Phase 15 완료
- **렌더링**: 3D (XZ 평면, Perspective 카메라)

---

## Phase 1: 크리티컬 통합 버그 수정

### Issue 1-1: LevelManager.SetupLevel() 서브시스템 미초기화
- **발견**: 이전 세션 감사에서 발견
- **원인**: SetupLevel()이 OnLevelLoaded 이벤트만 발행하고, BalloonController/HolderManager/BoardStateManager/GimmickManager 등의 초기화 메서드를 직접 호출하지 않음
- **수정**: SetupLevel()에 모든 서브시스템 직접 초기화 호출 추가 (DartManager.ResetAll, PopProcessor.ResetAll, HolderManager.InitializeHolders, BalloonController.SetupBalloons, BoardStateManager.InitializeBoard, GimmickManager.InitializeGimmicks)
- **영향 파일**: `LevelManager.cs`

### Issue 1-2: GimmickManager Singleton 미전환
- **발견**: 이전 세션 감사에서 발견
- **원인**: MonoBehaviour를 상속하여 GimmickManager.HasInstance/Instance 접근 불가
- **수정**: Singleton<GimmickManager>로 변경, OnSingletonAwake() 추가
- **영향 파일**: `GimmickManager.cs`

### Issue 1-3: _CONTRACTS.yaml 업데이트
- **수정**: LevelManager.SetupLevel → 서브시스템 호출 6건 추가
- **영향 파일**: `_CONTRACTS.yaml`

---

## Phase 2: Prefab Missing Script 에러

### Issue 2-1: 프리팹에 "Missing Script" 경고 (1차 시도)
- **증상**: ObjectPool prewarm 시 Balloon/Dart/Holder 프리팹 모두 "The referenced script on this Behaviour is missing!" 에러
- **1차 원인 추정**: 프리팹이 오래된 스크립트 GUID로 저장됨
- **1차 수정**: PrefabBuilder PREFS_KEY v1→v2 버전업, "skip if exists" → "delete and recreate" 로직 변경
- **결과**: ❌ 실패 — 문제 지속

### Issue 2-2: Prefab Missing Script 근본 원인 발견 (2차 시도)
- **근본 원인**: `BalloonIdentifier` 클래스가 `DartManager.cs` 내부에 정의, `HolderIdentifier` 클래스가 `InputHandler.cs` 내부에 정의. **Unity는 MonoBehaviour 클래스명 == 파일명**이어야 프리팹 스크립트 참조 해결 가능
- **수정**:
  1. `BalloonIdentifier.cs` 독립 파일 생성
  2. `HolderIdentifier.cs` 독립 파일 생성
  3. DartManager.cs, InputHandler.cs에서 내장 클래스 정의 제거
  4. PrefabBuilder v2→v3 버전업
  5. 기존 프리팹 파일 물리 삭제
- **검증**: 프리팹 GUID가 새 독립 파일의 .meta GUID와 일치 확인
- **결과**: ✅ 성공
- **영향 파일**: `BalloonIdentifier.cs`(신규), `HolderIdentifier.cs`(신규), `DartManager.cs`, `InputHandler.cs`, `Editor/PrefabBuilder.cs`

---

## Phase 3: Main 페이지 빈 화면 문제

### Issue 3-1: Title → Main 전환 시 아무것도 안 보임 (1차 시도)
- **증상**: "[GameBootstrap] Game started. Title page shown." 로그 후 Main 화면 공백
- **1차 원인 추정**: GameBootstrap.Awake()가 `_titlePage == null`만 체크하여 SceneBuilder가 _titlePage만 연결하면 나머지 참조 누락
- **1차 수정**: 모든 페이지 + _playButton null 체크, 불완전 시 파괴 후 Tier 3 재빌드
- **결과**: ❌ 실패 — 문제 지속

### Issue 3-2: output/GameBootstrap.cs에 Awake/ResolveOrBuildUI 자체가 없음 발견
- **근본 원인**: Unity 프로젝트 버전에는 3-tier UI 해석(Awake + ResolveOrBuildUI + BuildFullUI + EnsureManagers)이 있었지만, output/ 버전에는 이 코드가 전혀 없었음 (Start()만 있고 Awake()가 없음)
- **수정**:
  1. Unity 프로젝트 → output 역동기화
  2. 다단계 안전장치 추가: Tier 1(SerializeField) → Tier 2(이름 검색) → Tier 3(런타임 생성) → 최종 강제 빌드
  3. 폰트 로딩 3단계: LegacyRuntime.ttf → Arial.ttf → OS 폰트
  4. 상세 디버그 로그 추가
  5. SceneBuilder v5→v6 버전업 + 빌드 후 자동 씬 열기
- **결과**: ✅ Title → Main → Play 동작 확인
- **영향 파일**: `GameBootstrap.cs`, `Editor/SceneBuilder.cs`

---

## Phase 4: 인게임 비주얼 레이어 구현

### Issue 4-1: 게임씬에서 HUD 외 아무것도 안 보임
- **증상**: Play 버튼 후 Game Page 진입하지만, 풍선/레일/홀더 모두 화면에 보이지 않음
- **원인 분석**:
  - BalloonController: 풀에서 소환하지만 색상 미설정 (흰색 원 스프라이트)
  - HolderManager: 순수 데이터 레이어 (비주얼 GameObject 생성 안함)
  - RailManager: 경로 데이터만 저장 (화면 렌더링 없음, Gizmo만 에디터 뷰)
  - LevelDataProvider: LevelDatabase ScriptableObject 필요 (비어있음)
  - DartManager: 레일 경로 따라 이동하지만 방향별 타겟팅 없음

### Issue 4-2: 비주얼 레이어 구현 (3개 Agent 병렬)
- **Agent A**: LevelGenerator.cs(프로시저럴 레벨 생성) + GameEvents.cs(이벤트 추가)
- **Agent B**: RailRenderer.cs(컨베이어 시각화) + HolderVisualManager.cs(홀더 비주얼) + DirectionalTargeting.cs(방향별 타겟팅)
- **Agent C**: BalloonController.cs(색상 표시) + LevelManager.cs(제너레이터 연동) + SceneBuilder.cs/PrefabBuilder.cs/GameBootstrap.cs(매니저 등록)
- **결과**: ✅ 모든 파일 생성/수정 완료, 크로스 레퍼런스 검증 통과

### Issue 4-3: SceneBuilder NullReferenceException (line 210)
- **증상**: `lr.startWidth = 0.8f`에서 NullReferenceException
- **원인**: RailRenderer에 `[RequireComponent(typeof(LineRenderer))]`가 있어서 AddComponent<RailRenderer>() 시 LineRenderer 자동 추가됨. 이후 `AddComponent<LineRenderer>()` 중복 호출 문제
- **수정**: `AddComponent<LineRenderer>()` → `GetComponent<LineRenderer>()` + null 체크 + Shader.Find null 체크
- **결과**: ✅ 수정 완료
- **영향 파일**: `Editor/SceneBuilder.cs`

### Phase 4 신규 파일
| 파일 | 역할 |
|------|------|
| `LevelGenerator.cs` | 프로시저럴 레벨 생성 (그리드+레일+홀더) |
| `RailRenderer.cs` | LineRenderer 기반 레일 시각화 |
| `HolderVisualManager.cs` | 홀더 비주얼 (대기열+레일이동+다트발사) |
| `DirectionalTargeting.cs` | 방향별 최외곽 풍선 타겟팅 |

### Phase 4 수정 파일
| 파일 | 변경 |
|------|------|
| `BalloonController.cs` | BalloonColors 팔레트 + 스폰 시 색상 설정 + 쿼리 메서드 |
| `LevelManager.cs` | LevelGenerator 폴백 |
| `GameEvents.cs` | OnHolderPlacedOnRail, OnDartFiredAtTarget 추가 |
| `GameBootstrap.cs` | HolderVisualManager, LevelGenerator 싱글톤 등록 |
| `Editor/SceneBuilder.cs` | v7, 새 매니저 GO + LineRenderer 설정 |
| `Editor/PrefabBuilder.cs` | v4, Holder에 MagazineText 추가 |

---

## Phase 5: 2D → 3D 렌더링 전환 ✅ 완료

### Issue 5-1: 레퍼런스 게임과 근본적 렌더링 방식 불일치
- **발견**: 사용자가 레퍼런스 이미지 제공 — 3D 퍼스펙티브 아이소메트릭 환경
- **GAP 분석**:

| 요소 | 레퍼런스 | Before (2D) | After (3D) |
|------|---------|-------------|------------|
| 카메라 | 3D Perspective, ~50° 하향 | 2D Orthographic | Perspective 45°FoV, (0,12,-8), 55° down |
| UI 카메라 | 별도 오버레이 | 없음 (Overlay) | Orthographic UICamera, depth=10, UI only |
| 풍선 | 3D Sphere mesh | Circle SpriteRenderer | Sphere primitive + MeshRenderer |
| 레일 | 3D Track mesh (두께/깊이) | LineRenderer | Cylinder 세그먼트 메쉬 트랙 |
| 홀더 | 3D Cube mesh | Rect SpriteRenderer | Cube primitive (0.8, 0.5, 0.8) |
| 다트 | 3D Pin 오브젝트 | Diamond SpriteRenderer | Cylinder primitive (0.15, 0.4, 0.15) |
| 조명 | Directional Light + 그림자 | 없음 | Directional Light, soft shadows |
| 보드 | 3D 플랫폼 | 없음 | Cube platform (12, 0.2, 12) |
| 좌표계 | XZ 평면 (Y=높이) | XY 평면 (Z=0) | XZ 평면 (Y=높이) |
| 물리 | 3D Collider | 2D Collider + 3D fallback | 3D Collider only |
| Canvas | ScreenSpaceCamera | ScreenSpaceOverlay | ScreenSpaceCamera + UICamera |

### Issue 5-2: 3D 전환 구현 (4개 Agent 병렬)
- **Agent 1 (Foundation)**: PrefabBuilder v5 + SceneBuilder v8
  - 프리팹: Sphere/Cylinder/Cube primitive + Standard material + 3D Collider
  - 씬: 듀얼 카메라 + Directional Light + Board Platform
- **Agent 2 (Coordinates)**: LevelGenerator + LevelManager + DirectionalTargeting
  - 모든 좌표 XY→XZ 변환 (BoardCenterY→Z, waypoints Y→Z, gridPosition.y→world Z)
  - DirectionalTargeting: movementDirection.y→.z, WorldToGrid Y→Z
- **Agent 3 (Visuals)**: RailRenderer + BalloonController + HolderVisualManager
  - RailRenderer: LineRenderer 완전 제거 → Cylinder 세그먼트 트랙
  - BalloonController: SpriteRenderer→Renderer, 인접 방향 XY→XZ
  - HolderVisualManager: SpriteRenderer→Renderer, 3D 다트 방향 (LookRotation)
- **Agent 4 (Cleanup)**: InputHandler + DartManager + GameBootstrap
  - Physics2D 폴백 완전 제거, Canvas ScreenSpaceCamera 전환
- **결과**: ✅ 11개 파일 수정, 크로스 레퍼런스 검증 통과

### Phase 5 수정 파일
| 파일 | 변경 |
|------|------|
| `Editor/PrefabBuilder.cs` | v4→v5, 2D Sprite→3D Primitive, 2D Collider→3D Collider |
| `Editor/SceneBuilder.cs` | v7→v8, 듀얼 카메라+조명+보드+ScreenSpaceCamera |
| `LevelGenerator.cs` | XY→XZ 좌표 (BoardCenterZ, waypoints XZ, HolderAreaZ) |
| `LevelManager.cs` | balloon position (x,y,0)→(x,0.5,y) |
| `DirectionalTargeting.cs` | ScanDirection Y→Z, WorldToGrid Y→Z |
| `RailRenderer.cs` | LineRenderer 완전 제거 → Cylinder 세그먼트 3D 트랙 |
| `BalloonController.cs` | SpriteRenderer→Renderer, 인접 방향 XZ |
| `HolderVisualManager.cs` | SpriteRenderer→Renderer, XZ 위치, LookRotation |
| `InputHandler.cs` | Physics2D fallback 제거 |
| `DartManager.cs` | Physics2D fallback 제거 |
| `GameBootstrap.cs` | Canvas ScreenSpaceCamera + UICamera 탐색 |
| `HolderIdentifier.cs` | 코멘트 Physics2D→Physics 수정 |

---

## Phase 6: 게임플레이 리뷰 1차 ✅ 완료

### 리뷰 항목 7건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | 클릭한 홀더가 컨베이어 벨트로 부드럽게 이동 | `HolderVisualManager.cs` | MoveToRailEntryCoroutine: 5 units/sec 부드러운 이동 |
| 2 | 다트가 컨베이어 벨트 대신 풍선으로 직선 이동 | `DartManager.cs` | OnHolderSelected 구독 제거, HolderVisualManager가 직선 발사 담당 |
| 3 | 보관함 발사 시 Scale Up/Down 애니메이션 | `HolderVisualManager.cs` | ScalePunchCoroutine: 1.3x → 0.08s up, 0.12s down |
| 4 | 풍선 아래쪽 홀더 갯수 증가 | `LevelGenerator.cs` | MinHolders 4→6, MaxHolders 8→12 |
| 5 | A* 방식 장애물 회피 이동 | `HolderVisualManager.cs` | CalculateAvoidanceOffset: 주변 홀더 회피 (1.2f radius) |
| 6 | 앞 홀더 이동 시 뒤 홀더가 빈자리로 슬라이드 | `HolderVisualManager.cs` | RepositionWaitingHoldersSmooth: SmoothMoveCoroutine 3 units/sec |
| 7 | GamePage에서 BoardArea/HolderArea 패널 제거 (HUD만 유지) | `SceneBuilder.cs`, `GameBootstrap.cs` | BoardArea, HolderArea 생성 코드 제거 |

### Phase 6 수정 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | 스케일 펀치, A* 이동, 부드러운 큐 시프트, HOLDERS_PER_ROW 5→7 |
| `DartManager.cs` | OnHolderSelected 구독 제거 (HolderVisualManager로 단일화) |
| `LevelGenerator.cs` | MinHolders 4→6, MaxHolders 8→12 |
| `Editor/SceneBuilder.cs` | v8→v9, BoardArea/HolderArea 제거 |
| `GameBootstrap.cs` | BuildFullUI에서 BoardArea/HolderArea 제거 |

---

## Phase 7: 게임플레이 리뷰 2차 ✅ 완료

### 리뷰 항목 5건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | 다트 소진된 보관함은 레일 완주 후 사라짐 | `HolderVisualManager.cs` | CompleteRailLoop: magazine=0 시 ReturnHolderToPool + 제거 |
| 2 | 큐 시프트 방향 동→서가 아닌 남→북 | `HolderVisualManager.cs` | RepositionWaitingHoldersSmooth: Z 내림차순 정렬로 남→북 이동 |
| 3 | 홀더 배치 6x1 → 5x5 그리드 | `HolderVisualManager.cs` | HOLDERS_PER_ROW=5, FRONT_ROW_Z=-5, ROW_Z_SPACING=1.5 |
| 4 | 방향별 최외곽 풍선 타겟팅 (동→동쪽, 서→서쪽) | `HolderVisualManager.cs` | TryFireDart: 보드 중심→홀더 벡터로 스캔 방향 결정 |
| 5 | UICamera Overlay + Canvas UI 레이어 | `SceneBuilder.cs`, `GameBootstrap.cs` | URP Overlay(리플렉션), 모든 UI 오브젝트 layer=UI |

### Phase 7 수정 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | 5x5 그리드, 남→북 시프트, 다트 소진 제거, 보드 기준 방향 타겟팅 |
| `Editor/SceneBuilder.cs` | v9→v10, UICamera URP Overlay, 모든 UI 오브젝트 layer=UI |
| `GameBootstrap.cs` | 런타임 UI 생성 시 모든 오브젝트 layer=UI |

---

## Phase 8: 게임플레이 리뷰 3차 ✅ 완료

### 리뷰 항목 4건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | 5x5 그리드 실제 25개 홀더 배치 | `LevelGenerator.cs` | MinHolders=10, MaxHolders=25 |
| 2 | 큐 시프트 세로(남→북) 방향 — 컬럼 보존 | `HolderVisualManager.cs` | RepositionWaitingHoldersSmooth: X 컬럼별 그룹화 → 컬럼 내 Z 정렬 → 행 위치만 변경 (수평 이동 없음) |
| 3 | 다른 색 풍선이 시야 차단 시 발사 불가 | `DirectionalTargeting.cs` | HasClearLineOfSight: holder→target 경로에 다른 색 풍선 존재 확인, PERPENDICULAR_TOLERANCE 2.5→1.2 |
| 4 | 순차 다트 발사 (한 번에 한 발) | `HolderVisualManager.cs` | RailTraversalCoroutine: fireCooldown(dartFlightTime+0.15s), TryFireDart returns bool |

### Phase 8 수정 파일
| 파일 | 변경 |
|------|------|
| `LevelGenerator.cs` | MinHolders 6→10, MaxHolders 12→25 |
| `HolderVisualManager.cs` | 컬럼 보존 큐시프트, 순차 발사 쿨다운, TryFireDart→bool |
| `DirectionalTargeting.cs` | HasClearLineOfSight LOS 체크, PERPENDICULAR_TOLERANCE 2.5→1.2, LOS_CHECK_RADIUS 0.4 |

---

## Phase 9: 다트 공격 플로우 변경 ✅ 완료

### 리뷰 항목 1건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | 레일 한 면당 최외각 1발만 공격, 다음 랩에서 그 다음 풍선 공격 | `HolderVisualManager.cs` | RailTraversalCoroutine: 멀티랩(매거진 소진까지), GetRailSide로 4면 감지, 면당 1발 제한 |

### 변경 상세
- **이전**: 레일 1바퀴 동안 쿨다운 간격(0.35s)으로 연속 발사 → 한 면에서 여러 풍선 동시 공격
- **이후**: 레일 4면(South/East/North/West) 각각에서 최외각 매칭 풍선 1개만 공격. 매거진 남으면 다음 랩 진행. 다음 랩 때 이전에 터진 풍선 뒤의 풍선이 새 최외각이 되어 공격 대상
- `GetRailSide(direction)`: 이동 방향으로 현재 면 판별 (0=S, 1=E, 2=N, 3=W)
- `lastFiredSide`: 랩마다 리셋, 같은 면에서 중복 발사 방지
- `MAX_LAPS=50`: 안전 제한 (무한 루프 방지)

### Phase 9 수정 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | RailTraversalCoroutine 멀티랩 + 면당 1발, GetRailSide 추가 |

---

## Phase 10: 다트 다중 공격 + 속도 + 결과 팝업 시도1 ✅ 완료

### 리뷰 항목 3건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | Result Popup 미표시 (시도1) | `HolderVisualManager.cs` | WaitForDartsToLand: 레일 완료 전 모든 다트 착탄 대기 |
| 2 | 컨베이어/보관함 이동 속도 느림 | `HolderVisualManager.cs` | DEFAULT_RAIL_SPEED 3→7, moveSpeed 5→10, DEFAULT_DART_FLIGHT_TIME 0.2→0.12 |
| 3 | 같은 면 최외각이 여러개면 모두 공격 | `HolderVisualManager.cs`, `DirectionalTargeting.cs` | Phase 9의 면당 1발 제한 제거 → 같은 면의 모든 최외각 순차 공격 |

### 변경 상세 — Issue 1 (Result Popup 시도1)
- **원인 추정**: 마지막 다트 비행 중 CompleteRailLoop → RemoveHolder → OnAllHoldersEmpty → Fail 판정
- **수정**: `WaitForDartsToLand()` 코루틴 — `_activeDartProjectiles.Count == 0`까지 대기 (최대 3초)
- **결과**: ⚠️ 부분 해결 — 타이밍에 따라 여전히 Fail이 먼저 발동 (Phase 13에서 근본 수정)

### 변경 상세 — Issue 3 (최외각 다중 공격)
- **예시**: 4×4 보드, 남쪽 최외각 행에 빨간 풍선 (1,1)(2,1)(3,1)
- **이전**: 면당 1발 → (1,1)만 공격, 다음 랩에서 (2,1)
- **이후**: 남쪽 면 지나며 (1,1)→쿨다운→(2,1)→쿨다운→(3,1) 순차 공격. 다음 랩에서 2행이 새 최외각
- `FindTarget(excludeIds)`: 이미 발사한 balloonId 제외하여 다음 타겟 선택
- `firedThisSide`: 면 변경 시 Clear, 같은 면에서 중복 타겟 방지

### Phase 10 수정 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | WaitForDartsToLand, 속도 증가, firedThisSide 다중 공격, TryFireDart 파라미터 추가 |
| `DirectionalTargeting.cs` | FindTarget에 excludeIds 파라미터 추가 |

---

## Phase 11: 첫째 행만 클릭 가능 ✅ 완료

### 리뷰 항목 1건

| # | 리뷰 내용 | 수정 파일 | 변경 |
|---|-----------|-----------|------|
| 1 | 첫째 행(front row)만 클릭 가능, 뒤쪽 행은 앞으로 이동 후 클릭 가능 | `HolderVisualManager.cs`, `InputHandler.cs` | IsInFrontRow 판정 + 탭 차단 |

### 변경 상세
- `HolderVisualManager.IsInFrontRow(holderId)`: holder의 Z 위치가 `FRONT_ROW_Z - ROW_Z_SPACING * 0.5` 이상이면 front row
- `InputHandler.TryRaycastHolder`: 탭 시 `IsInFrontRow` 체크 → false면 `OnHolderTapped` 미발행
- 앞 행 홀더가 레일로 이동 → 컬럼 보존 남→북 시프트 → 뒤 행 홀더가 front row로 진입 → 자동으로 클릭 가능

### Phase 11 수정 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | IsInFrontRow(holderId) public 메서드 추가 |
| `InputHandler.cs` | TryRaycastHolder에 front row 체크 추가 |

---

## Phase 12: 레벨 에디터 + 결과 팝업 진단 ✅ 완료

### 레벨 에디터 (신규)

| 기능 | 설명 |
|------|------|
| 레일 설정 | 방향(CW/CCW), Board Center(X,Z), Padding, Height |
| 풍선 그리드 | N×M 사이즈 조절(2~10), 셀 클릭으로 색상 페인트, 비어있는 셀 지원, 기믹 선택(hidden/chain/frozen/bomb/spawner) |
| 홀더 그리드 | N×M 사이즈(1~8), 셀 클릭 색상, 비어있는 슬롯, 우클릭 매거진 변경, 기본 매거진 설정 |
| 색상 팔레트 | 8색 브러시 + 지우개, numColors 제한 |
| 내보내기 | LevelDatabase SO 저장, JSON 내보내기, Play Test(에디터 프리뷰) |
| 검증 표시 | 풍선 수, 홀더 수, 총 다트 수 표시 + 솔버블 경고 |

### Result Popup 진단 로그 추가
- `LevelManager.CompleteLevel/FailLevel`: `_levelActive` 상태 경고 로그
- `GameBootstrap.HandleLevelCompleted`: 이벤트 수신 확인 로그
- `GameBootstrap.ShowResultPage`: _resultPage null 시 동적 재생성 복구

### Phase 12 수정 파일
| 파일 | 변경 |
|------|------|
| `Editor/LevelEditorWindow.cs` | **신규** — 레벨 에디터 윈도우 (BalloonFlow > Level Editor 메뉴) |
| `LevelManager.cs` | Test level 로딩(EditorPrefs), 진단 로그 |
| `GameBootstrap.cs` | ShowResultPage null 복구, 진단 로그 |

---

## Phase 13: Result Popup 근본 수정 ✅ 완료

### 근본 원인 확정
진단 로그로 확인된 문제:
```
[LevelManager] CompleteLevel called but _levelActive=false (already completed/failed). Level=1
[BoardStateManager] Board cleared! Level=1, Score=1500, Stars=0
```

**타이밍 문제**: 마지막 다트가 아직 비행 중일 때 이벤트 체인이 꼬임
1. 홀더 매거진 소진 → `CompleteRailLoop` → `OnRailLoopComplete{magazine=0}`
2. `HolderManager.RemoveHolder` → `AreAllHoldersEmpty()` = true → `OnAllHoldersEmpty`
3. `BoardStateManager.EvaluateNoMovesCondition` → `_remainingBalloons > 0` (다트 아직 비행 중!) → **`TriggerFail`** → state=Failed → `OnBoardFailed` → `LevelManager.FailLevel` → **`_levelActive = false`**
4. 다트 도착 → `PopBalloon` → `OnBalloonPopped` → `_remainingBalloons = 0` → `OnBoardCleared`
5. `LevelManager.CompleteLevel` → **`_levelActive` 이미 false → 무시됨** → Result Popup 미표시

`WaitForDartsToLand`만으로는 불충분 — 다중 홀더 시나리오나 이벤트 처리 순서에 따라 여전히 발생 가능

### 수정 3곳 — "클리어는 항상 Fail보다 우선"

| # | 수정 | 파일 | 변경 |
|---|------|------|------|
| 1 | 풍선 Pop 시 Failed 상태에서도 클리어 체크 | `BoardStateManager.cs` | `HandleBalloonPopped`: `_currentState == Playing \|\| Failed` 조건으로 `EvaluateClearCondition` 항상 실행 |
| 2 | CompleteLevel은 항상 발행 | `LevelManager.cs` | `_levelActive` 가드 제거 — 보드 클리어 = 무조건 승리 |
| 3 | Fail 팝업 도중 Clear 도착 시 Clear로 교체 | `GameBootstrap.cs` | `_pendingResultIsWin` 플래그 — Clear 도착 시 Fail 코루틴 중단 + Clear 팝업 표시. Fail 핸들러는 Clear 대기 중이면 무시 |

### 변경 상세

**BoardStateManager.HandleBalloonPopped** (기존 → 수정):
```csharp
// 기존: if (_currentState != BoardState.Playing) return;
// 수정: 항상 remaining 업데이트 + Playing 또는 Failed 상태에서 클리어 체크
if (_currentState == BoardState.Playing || _currentState == BoardState.Failed)
{
    EvaluateClearCondition();
}
```

**LevelManager.CompleteLevel** (기존 → 수정):
```csharp
// 기존: if (!_levelActive) return;
// 수정: 가드 제거 — 클리어는 무조건 발행
_levelActive = false;
EventBus.Publish(new OnLevelCompleted { ... });
```

**GameBootstrap** (신규 필드 + 로직):
```csharp
private bool _pendingResultIsWin;

HandleLevelCompleted: _pendingResultIsWin = true; → Fail 코루틴 중단 → Clear 표시
HandleLevelFailed: if (_pendingResultIsWin) return; → Clear 대기 중이면 무시
OnPlayClicked/OnNextClicked/OnRetryClicked: _pendingResultIsWin = false; → 리셋
```

### Phase 13 수정 파일
| 파일 | 변경 |
|------|------|
| `BoardStateManager.cs` | HandleBalloonPopped: Failed 상태에서도 클리어 체크 실행 |
| `LevelManager.cs` | CompleteLevel: `_levelActive` 가드 제거, 항상 OnLevelCompleted 발행 |
| `GameBootstrap.cs` | `_pendingResultIsWin` 플래그, Clear>Fail 우선순위, DelayedShowResultCoroutine 리네임 |

---

## Phase 14: 홀더 겹침 수정 + 실패조건 확인 + 30레벨 생성 ✅ 완료

### Issue 14-1: 여러 홀더 빠르게 클릭 시 겹침 현상
- **증상**: 홀더에서 보드(레일)로 넘어가는 속도는 괜찮으나, 여러 홀더를 연속 클릭하면 이동 시작점에서 겹쳐지는 느낌
- **원인**: `HandleHolderSelected` → `MoveHolderToRail` → `MoveToRailEntryCoroutine`가 클릭할 때마다 즉시 독립 실행. 여러 코루틴이 동시에 같은 레일 진입점으로 이동
- **수정**: 배포 큐(Deployment Queue) 시스템 도입
  - `_deploymentQueue` (Queue<OnHolderSelected>): 클릭된 홀더를 순서대로 대기
  - `_isDeployingHolder` (bool): 현재 홀더가 레일 진입 중인지 추적
  - `TryProcessDeploymentQueue()`: 현재 배포 완료 후 다음 홀더 처리
  - 레일 진입 완료 시 `_isDeployingHolder = false` + 다음 큐 항목 처리
- **영향 파일**: `HolderVisualManager.cs`

### Issue 14-2: 게임 실패 조건 확인
기획서(beat_chart.yaml) 대비 현재 구현 상태:

| 실패 조건 | 기획서 | 현재 구현 | 상태 |
|-----------|--------|-----------|------|
| **홀더 오버플로우** (대기 홀더 > 5) | ✅ 유일한 실패 조건 | `BoardStateManager.HandleHolderOverflow` + `HolderManager.PublishOverflow` | ✅ 일치 |
| **NoMovesLeft** (모든 홀더 소진 + 풍선 남음) | ❌ 기획서 미기재 | `BoardStateManager.EvaluateNoMovesCondition` | ⚠️ 추가 구현됨 |

**분석**:
- 기획서에는 "홀더 초과(>5)"만 실패 조건으로 명시
- 그러나 NoMovesLeft(모든 다트 소진 + 풍선 잔존)은 논리적으로 교착 상태 → 사실상 실패
- **현재 구현이 더 완전함** — 두 조건 모두 유지하는 것이 맞음
- 홀더 용량: `MAX_HOLDER_SLOTS = 5` (HolderManager), `HolderOverflowThreshold = 5` (BoardStateManager) → 6개 이상 시 실패

### Issue 14-3: 30레벨 생성 (기획서 데이터 기반)
- **요구**: beat_chart.yaml의 패키지/포지션별 파라미터에 맞춘 30레벨 제작
- **구현**: `Editor/LevelDatabaseGenerator.cs` — 메뉴: `BalloonFlow > Generate Level Database (30 Levels)`
- **산출물**: `Assets/Resources/LevelDatabase.asset` (ScriptableObject)
- LevelDataProvider가 이 SO를 참조하면 프로시저럴 폴백(LevelGenerator) 대신 사전 설계된 레벨 사용

**30레벨 요약 (beat_chart.yaml 기반)**:

| 레벨 | PKG | 유형 | 색상 | 그리드 | 풍선 | 홀더 | 기믹 |
|------|-----|------|------|--------|------|------|------|
| 1-2 | 1 | Tutorial | 2 | 3×3 | 9 | 6-7 | - |
| 3 | 1 | Intro 3색 | 3 | 4×3 | 12 | 8 | - |
| 4 | 1 | Hard | 3 | 5×4 | 20 | 12 | - |
| 5 | 1 | Rest | 2 | 3×3 | 9 | 6 | - |
| 6 | 1 | Intro 4색 | 4 | 4×4 | 16 | 10 | - |
| 7 | 1 | Normal | 4 | 5×4 | 20 | 12 | - |
| 8 | 1 | Intro 5색 | 5 | 4×4 | 16 | 10 | - |
| 9 | 1 | Hard | 5 | 5×5 | 25 | 14 | - |
| 10 | 1 | Rest | 3 | 3×3 | 9 | 6 | - |
| 11 | 1 | Tutorial | 4 | 4×4 | 16 | 10 | Hidden(30%) |
| 12 | 1 | Normal | 4 | 5×4 | 20 | 12 | - |
| 13 | 1 | Normal | 4 | 5×4 | 20 | 12 | Hidden(25%) |
| 14 | 1 | Normal | 5 | 5×5 | 25 | 14 | - |
| 15 | 1 | Rest | 3 | 4×3 | 12 | 8 | - |
| 16 | 1 | Normal | 5 | 5×5 | 25 | 14 | Hidden(30%) |
| 17 | 1 | Normal | 5 | 6×5 | 30 | 16 | - |
| 18 | 1 | Normal | 5 | 6×5 | 30 | 16 | Hidden(35%) |
| 19 | 1 | Hard | 5 | 6×6 | 36 | 18 | Hidden(40%) |
| 20 | 1 | Rest | 3 | 4×3 | 12 | 8 | - |
| 21 | 2 | Tutorial | 4 | 5×4 | 20 | 12 | Spawner_T(15%) |
| 22 | 2 | Normal | 5 | 5×5 | 25 | 14 | - |
| 23 | 2 | Normal | 5 | 6×5 | 30 | 16 | Spawner_T(10%) |
| 24 | 2 | Hard | 5 | 6×6 | 36 | 18 | Hidden(35%) |
| 25 | 2 | Rest | 4 | 4×4 | 16 | 10 | - |
| 26 | 2 | Intro 6색 | 6 | 5×5 | 25 | 14 | - |
| 27 | 2 | Normal | 5 | 6×5 | 30 | 16 | Hidden(30%) |
| 28 | 2 | Normal | 6 | 6×6 | 36 | 18 | Spawner_T(12%) |
| 29 | 2 | Normal | 5 | 7×5 | 35 | 18 | Hidden(35%) |
| 30 | 2 | Normal | 6 | 6×6 | 36 | 20 | Hidden(30%) |

### Phase 14 수정/생성 파일
| 파일 | 변경 |
|------|------|
| `HolderVisualManager.cs` | 배포 큐 시스템 추가 (_deploymentQueue, _isDeployingHolder, TryProcessDeploymentQueue) |
| `Editor/LevelDatabaseGenerator.cs` | **신규** — 30레벨 생성 에디터 도구 (beat_chart 기반) |

---

## Phase 15: 아웃게임 UI 개선 (상점, 스테이지 표시) ✅ 완료

### Issue 15-1: 상점 UI 구현
- **요구**: 상점 페이지 추가, 상품 목록 표시, 구매 기능
- **구현**: `_shopPage` (5번째 페이지) 추가 — 헤더(SHOP 타이틀 + 닫기 버튼 + 재화 표시) + 스크롤 가능한 상품 목록
- **상품**: ShopManager 카탈로그 연동 — Coin Packs(IAP), Bundles, Boosters(코인 구매), Ad Removal, Heart Refill
- **각 상품 아이템**: 이름 + 설명 + 가격/구매 버튼
- **영향 파일**: `GameBootstrap.cs`

### Issue 15-2: 골드/잼 버튼 → 상점 열기
- **요구**: CurrencyBar의 골드/잼 텍스트를 클릭하면 상점 UI 열림
- **구현**: 기존 Text 위젯을 Button으로 교체 (`_coinButton`, `_gemButton`)
  - `COL_COIN_BTN` (황금색) / `COL_GEM_BTN` (파란색) 배경
  - 클릭 시 `OnShopClicked()` → `BuildShopItems()` → `ShowPage(_shopPage)`
- **영향 파일**: `GameBootstrap.cs`

### Issue 15-3: 상점 닫기 → 메인 복귀
- **요구**: 상점의 X 버튼(HomeButton 역할) 클릭 시 상점 닫고 메인 복귀
- **구현**: `_shopCloseButton` ("X") 우상단 배치
  - `OnShopCloseClicked()` → `ShowPage(_mainPage)` + `RefreshMainPageInfo()` + `ShopManager.CloseShop()`
- **영향 파일**: `GameBootstrap.cs`

### Issue 15-4: PlayButton에 현재 스테이지 표시
- **요구**: "PLAY" 대신 현재 스테이지 번호 표시
- **구현**: `_playButtonLabel` 참조 추가, `UpdatePlayButtonLabel()` 메서드
  - `LevelManager.GetHighestCompletedLevel()` + 1 = 현재 스테이지
  - 표시: "Stage 1", "Stage 5" 등
  - `RefreshMainPageInfo()`가 재화 + 스테이지 모두 갱신
- **영향 파일**: `GameBootstrap.cs`

### Phase 15 수정 파일
| 파일 | 변경 |
|------|------|
| `GameBootstrap.cs` | 상점 페이지 추가, 골드/잼 버튼화, 상점 열기/닫기, 스테이지 표시, RefreshMainPageInfo |

---

## 버전 히스토리

| 컴포넌트 | 버전 | 변경 내용 |
|----------|------|-----------|
| PrefabBuilder PREFS_KEY | v1→v2→v3→v4→v5 | 강제 재빌드, 독립 파일 대응, MagazineText, 3D Primitive |
| SceneBuilder PREFS_KEY | v4→v5→v6→v7→v8→v9→v10 | UI 수정, 자동 씬 열기, 새 매니저, 3D 듀얼카메라+조명, HUD only, UICamera Overlay+UI layer |

---

## 파일 변경 총 목록

### 신규 생성
| 파일 | Phase | 역할 |
|------|-------|------|
| `BalloonIdentifier.cs` | 2 | Prefab 스크립트 참조 수정 |
| `HolderIdentifier.cs` | 2 | Prefab 스크립트 참조 수정 |
| `LevelGenerator.cs` | 4 | 프로시저럴 레벨 생성 |
| `RailRenderer.cs` | 4 | 레일 시각화 (3D Cylinder 트랙) |
| `HolderVisualManager.cs` | 4 | 홀더 비주얼 관리 (대기열+레일+다트) |
| `DirectionalTargeting.cs` | 4 | 방향별 최외곽 풍선 타겟팅 + LOS 차단 |
| `Editor/LevelEditorWindow.cs` | 12 | 레벨 에디터 (레일/풍선/홀더/색상 설정) |
| `Editor/LevelDatabaseGenerator.cs` | 14 | 30레벨 생성 에디터 도구 (beat_chart 기반) |

### 수정 이력
| 파일 | 수정 Phase | 주요 변경 |
|------|------------|-----------|
| `LevelManager.cs` | 1,4,5,12,13 | 서브시스템 초기화, LevelGenerator 폴백, XZ 좌표, Test level, CompleteLevel 가드 제거 |
| `GimmickManager.cs` | 1 | Singleton 전환 |
| `DartManager.cs` | 2,5,6 | 내장 클래스 제거, Physics2D 제거, OnHolderSelected 구독 제거 |
| `InputHandler.cs` | 2,5,11 | 내장 클래스 제거, Physics2D 제거, front row 체크 |
| `BalloonController.cs` | 4,5 | 색상 팔레트, 쿼리 메서드, 3D Renderer, XZ 인접 |
| `GameEvents.cs` | 4 | OnHolderPlacedOnRail, OnDartFiredAtTarget 추가 |
| `GameBootstrap.cs` | 3,4,5,6,7,12,13,15 | 3-tier UI, 매니저 등록, ScreenSpaceCamera, UI layer, null 복구, Clear>Fail 우선순위, 상점/스테이지 |
| `Editor/SceneBuilder.cs` | 3,4,5,6,7 | v5~v10, 씬 자동열기, 매니저 GO, 3D 듀얼카메라, HUD only, UICamera Overlay |
| `Editor/PrefabBuilder.cs` | 2,4,5 | v2~v5, 독립 파일, MagazineText, 3D Primitive |
| `HolderVisualManager.cs` | 5,6,7,8,9,10,11,14 | 3D전환, 스케일펀치, A*이동, 큐시프트, 5x5그리드, 멀티랩, 속도, WaitForDarts, firedThisSide, IsInFrontRow, 배포큐 |
| `DirectionalTargeting.cs` | 5,8,10 | XZ좌표, HasClearLineOfSight, PERPENDICULAR_TOLERANCE 1.2, excludeIds |
| `LevelGenerator.cs` | 5,6,8 | XZ좌표, MinHolders/MaxHolders 증가 |
| `RailRenderer.cs` | 5 | LineRenderer→Cylinder 세그먼트 |
| `BoardStateManager.cs` | 13 | HandleBalloonPopped: Failed 상태에서도 클리어 체크 |
| `_CONTRACTS.yaml` | 1 | 메서드 호출 계약 추가 |

---

## 핵심 버그 패턴 요약

| 패턴 | Phase | 근본 원인 | 해결 |
|------|-------|-----------|------|
| Missing Script | 2 | MonoBehaviour 클래스명 ≠ 파일명 | 독립 파일 분리 |
| 빈 화면 | 3 | output/ 코드와 Unity 코드 불일치 | 역동기화 + 3-tier UI |
| Result Popup 미표시 | 10,13 | 다트 비행 중 Fail→Clear 타이밍 경합 | Clear>Fail 절대 우선순위 |
| 이벤트 경합 | 10,13 | EventBus 동기 처리 + 코루틴 비동기 충돌 | 상태 가드 완화 + 우선순위 플래그 |
| 홀더 겹침 | 14 | 다중 MoveToRailEntryCoroutine 동시 실행 | 배포 큐(Deployment Queue) |
