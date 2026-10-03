# Unity 동적 리소스·프리팹 풀링 규칙

> 카탈로그: [재사용 아키텍처](README.md)
> 상태: 프로젝트 독립 적용 지침
> 문서 종류: 아키텍처 패키지
> 적용 대상: `Dignus.Unity` 리소스 관리자와 오브젝트 풀을 사용하는 Unity 클라이언트

## 적용 조건

다음 조건을 모두 만족할 때 이 문서를 Unity 클라이언트 담당 세션에 전달한다.

- 화면과 월드 오브젝트를 프리팹으로 제작한다.
- 런타임 리소스는 `DignusUnityResourceManager`로 로드한다.
- 반복 생성되는 프리팹 인스턴스는 `DignusUnityObjectPool` 또는 `InstantiateWithPool`로 생성하고 `Recycle`로 회수한다.
- Sprite 묶음은 `SpriteAtlas`로 패킹하고 화면 또는 씬 생명주기에 맞춰 로드한다.

프로젝트가 Addressables, AssetBundle 전용 관리자 또는 다른 풀 구현을 표준으로 사용한다면 이 문서를 그대로 적용하지 않는다. 그 프로젝트의 동등한 로드·회수 계약으로 먼저 매핑한다.

## 불변식

- Scene과 상위 Controller는 개별 Sprite, Material, Font와 반복 생성 프리팹을 직접 직렬화 참조하지 않는다.
- World 프리팹 안에 독립적인 콘텐츠 객체를 자식으로 합쳐 두지 않는다. 캐릭터, 투척물, 장치와 충돌 영역처럼 생명주기를 독립적으로 초기화할 수 있는 객체는 각각 루트 컴포넌트와 프리팹을 가진다.
- 런타임에 생성되는 표현 오브젝트는 프리팹을 먼저 제작하고 리소스 관리자로 프리팹을 로드한 뒤 풀에서 꺼낸다.
- `Object.Instantiate`와 `Object.Destroy`로 풀 경계를 우회하지 않는다.
- `[PrefabPath]`에는 프리팹 파일명이 아니라 프리팹을 찾을 Resources 상위 경로를 지정한다.
- 프리팹 내부 직렬화 참조는 같은 프리팹에 포함된 Transform, Renderer, Collider와 UI 컴포넌트로 제한한다.
- SpriteRenderer의 Sprite, 기능 전용 Font, 공유 Material과 PhysicsMaterial 같은 외부 에셋은 초기화 경계에서 동적으로 할당한다.
- 프로젝트 전역 TMP 기본 폰트는 TMP 설정이 소유할 수 있다. 이 경우 UI별로 같은 기본 폰트를 다시 로드·해제하지 않는다.
- 로드한 에셋과 풀에서 꺼낸 인스턴스는 소유한 생명주기에서 각각 해제하고 회수한다.
- 에셋 참조를 Renderer나 Collider에서 먼저 제거한 뒤 리소스 관리자에 해제를 요청한다.
- 데이터 값, 문자열과 게임 결과를 리소스 누락 폴백으로 만들지 않는다. 필수 리소스가 없으면 경로와 타입이 포함된 오류를 발생시킨다.

## 책임 경계

| 책임 | 소유자 |
|---|---|
| Resources 경로 상수 | 프로젝트의 클라이언트 경로 상수 타입 |
| 프리팹 경로 선언 | 프리팹 루트 컴포넌트의 `[PrefabPath]` |
| 프리팹 에셋 로드 | `DignusUnityResourceManager` |
| 프리팹 인스턴스 생성·재사용 | `DignusUnityObjectPool` 또는 `InstantiateWithPool` |
| 인스턴스 초기화·런타임 참조 설정 | 프리팹 루트 컴포넌트 |
| SpriteAtlas 로드와 Sprite 이름 조회 | 화면 생명주기의 Atlas 관리자 |
| 기능 전용 UI Font·Material 연결 | 해당 UI 프리팹 컴포넌트 |
| 프로젝트 전역 TMP 기본 폰트 | TMP 설정과 프로젝트 초기화 경계 |
| 인스턴스 회수 | 인스턴스를 풀에서 꺼낸 Scene 또는 World 소유자 |
| 에셋 해제 | 에셋을 로드한 관리자 또는 컴포넌트 |

## 표준 생명주기

1. Scene이 필요한 Atlas 관리자를 DI에서 얻는다.
2. Scene 진입 시 해당 화면의 `SpriteAtlas`를 동적으로 로드한다.
3. 프리팹 루트 타입의 `[PrefabPath]`를 통해 프리팹 에셋을 동적으로 로드한다.
4. Scene 또는 World가 프리팹 인스턴스를 풀에서 꺼낸다.
5. 프리팹의 `Init`에 Camera, Atlas 관리자와 현재 화면에 필요한 런타임 의존성을 전달한다.
6. 프리팹은 `Init`에서 Sprite, Material과 기능 전용 Font를 동적으로 조회해 내부 Renderer와 UI 컴포넌트에 연결한다. 프로젝트 전역 TMP 기본 폰트는 다시 조회하지 않는다.
7. Scene 종료 시 이벤트와 Coroutine Handle을 먼저 정리한다.
8. 프리팹이 외부 에셋 참조를 제거하고 자신이 직접 로드한 에셋을 해제한다.
9. 소유자가 프리팹 인스턴스를 `Recycle`한다.
10. 마지막 인스턴스 회수 뒤 Scene이 Atlas의 Sprite 참조를 해제한다.

초기화 순서와 종료 순서를 뒤집지 않는다. 풀 회수 뒤 컴포넌트에 접근하거나, Renderer가 참조 중인 에셋을 먼저 해제하지 않는다.

## 프리팹 로드 예시

```csharp
[PrefabPath(ClientPaths.GamePrefab)]
internal class WorldView : MonoBehaviour
{
    public void Init(Camera worldCamera, AtlasManager atlasManager)
    {
        if (worldCamera == null)
        {
            throw new ArgumentNullException(nameof(worldCamera));
        }
        if (atlasManager == null)
        {
            throw new ArgumentNullException(nameof(atlasManager));
        }

        // 같은 프리팹 내부 컴포넌트는 직렬화하고 외부 에셋은 여기서 동적으로 연결한다.
    }
}
```

```csharp
var prefab = DignusUnityResourceManager.Instance.LoadAsset<WorldView>();
if (prefab == null)
{
    throw new InvalidOperationException("WorldView prefab is missing.");
}

_worldView = this.InstantiateWithPool(prefab);
_worldView.Init(Camera.main, _atlasManager);
```

```csharp
_worldView.Dispose();
_worldView.Recycle();
_worldView = null;
_atlasManager.Unload();
```

예시의 실제 타입명, 경로, 초기화 인수와 해제 메서드는 대상 프로젝트에 맞게 교체한다. 예시를 이유로 프로젝트에 불필요한 공통 인터페이스나 Manager를 추가하지 않는다.

## SpriteAtlas 규칙

- Atlas 파일은 리소스 관리자가 찾을 수 있는 `[PROJECT-SPECIFIC: Atlas Resources 경로]`에 둔다.
- Atlas 이름은 화면 또는 독립 생명주기 단위로 정한다.
- 원본 Texture는 Atlas의 packable로만 참조하고 Scene·Prefab이 같은 Texture의 Sprite를 직접 참조하지 않게 한다.
- 회전 패킹이 화면 배치나 픽셀 기준 판정에 영향을 줄 수 있으면 비활성화한다.
- 런타임 조회 이름은 원본 Sprite 이름과 일치시킨다.
- Atlas 관리자는 로드하지 않은 Atlas 조회, 빈 Sprite 이름과 누락된 Sprite를 조용히 허용하지 않는다.
- 여러 화면에서 같은 Atlas를 공유한다면 단일 소유자와 실제 해제 시점을 프로젝트 문서에 명시한다.

## 비프리팹 리소스 규칙

기능 전용 Font, Material, PhysicsMaterial과 AudioClip도 리소스 관리자를 통해 로드한다. 프로젝트가 TMP 기본 폰트를 전역 설정으로 확정했다면 그 기본 폰트는 UI별 동적 로드 대상에서 제외한다.

- 경로 문자열은 호출부에 반복하지 않고 프로젝트 경로 상수에서 관리한다.
- 로드 직후 null을 검증한다.
- 풀링된 UI나 View는 매 `Init`에서 필요한 리소스를 다시 연결할 수 있어야 한다.
- `Dispose`에서 Renderer, TMP와 Collider의 외부 참조를 제거한다.
- 같은 리소스를 여러 인스턴스가 공유한다면 개별 인스턴스가 임의로 해제하지 않는다. 화면 단위 관리자나 프로젝트 리소스 소유자가 참조 생명주기를 맡는다.

## Spine 캐릭터 규칙

- Spine Editor export 메이저·마이너 버전과 `spine-csharp`, `spine-unity` 런타임 버전을 일치시킨다.
- JSON, atlas와 texture는 디자이너가 전달한 파일명과 atlas region 계약을 그대로 유지한다.
- 캐릭터는 `SkeletonAnimation`과 로컬 `MeshRenderer`를 포함한 전용 프리팹으로 만들고 `[PrefabPath]`를 선언한다.
- World는 `SkeletonDataAsset`이나 Spine texture를 직접 직렬화하지 않는다. 캐릭터 프리팹을 동적으로 로드해 풀에서 생성한다.
- 풀 회수 전 animation track과 런타임 상태를 정리하며, 다시 꺼낸 인스턴스는 `Init`에서 기본 skin과 초기 animation을 복구한다.
- 캐릭터 애니메이션 이름은 기획·디자인 계약으로 검증한다. 누락된 이름을 다른 animation으로 추측하거나 정적 Sprite로 대체하지 않는다.
- Spine 리소스 통합에는 Spine 라이선스가 필요하므로 프로젝트 소유자는 사용 권한을 확인한다.

## 프로젝트별로 매핑할 값

- `[PROJECT-SPECIFIC: Resources 루트와 경로 상수 타입]`
- `[PROJECT-SPECIFIC: 프리팹 분류 경로]`
- `[PROJECT-SPECIFIC: Atlas 종류 enum과 Atlas 파일 경로]`
- `[PROJECT-SPECIFIC: 풀 생성 확장 메서드 또는 직접 API]`
- `[PROJECT-SPECIFIC: Scene·World·UI 종료 순서]`
- `[PROJECT-SPECIFIC: 공유 에셋의 소유자와 해제 시점]`
- `[PROJECT-SPECIFIC: Editor 프리팹·Atlas 생성 도구]`
- `[PROJECT-SPECIFIC: Spine export·runtime 버전과 애니메이션 이름]`
- `[PROJECT-SPECIFIC: Unity Editor 또는 Player 검증 절차]`

이 값이 정해지지 않았다면 경로, Atlas 범위 또는 공유 리소스 해제 책임을 구현 코드에서 추측하지 않는다.

## 검증

- Scene YAML에 동적 생성 대상 프리팹 인스턴스와 외부 Sprite·Material·Font 직접 참조가 남지 않았는지 확인한다.
- 프리팹 YAML의 외부 참조를 검사해 허용된 같은 프리팹 내부 컴포넌트와 의도한 리소스만 남았는지 확인한다.
- `[PrefabPath]`와 Resources 실제 경로 및 프리팹 파일명이 일치하는지 확인한다.
- Atlas에 필요한 Sprite가 정확히 한 번씩 포함되고 런타임 이름 조회가 성공하는지 확인한다.
- 풀에서 회수한 인스턴스를 다시 꺼내 `Init`했을 때 이전 Sprite, 이벤트, Coroutine과 상태가 남지 않는지 확인한다.
- Spine 캐릭터는 JSON 버전, atlas texture 참조, SkeletonDataAsset scale, skin과 필수 animation 이름을 확인한다.
- Scene 종료 시 인스턴스 회수 뒤 Atlas와 비프리팹 리소스가 해제되는지 확인한다.
- Unity C# 프로젝트 빌드, Prefab·Scene 직렬화 검사와 Unity Editor Play Mode 왕복 진입을 실행한다.

## 다른 프로젝트 세션에 전달할 문장

```text
Unity 리소스와 반복 생성 오브젝트는 `UnityDynamicResourcePooling.md`를 적용한다. Scene에 외부 Sprite, Material, Font 또는 동적 생성 대상 프리팹을 직접 연결하지 않는다. `[PrefabPath]`와 프로젝트 리소스 관리자로 프리팹을 동적 로드하고 프로젝트 표준 풀에서 생성·회수한다. Sprite는 화면 생명주기의 Atlas에서 조회하며, 종료 시 외부 에셋 참조 제거 → 직접 로드한 에셋 해제 → 프리팹 회수 → Atlas 해제 순서를 지킨다. 구현 전 문서의 `[PROJECT-SPECIFIC]` 항목을 대상 프로젝트 구조 문서에 매핑한다.
```
