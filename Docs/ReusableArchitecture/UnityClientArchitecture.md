# Unity 클라이언트 계층형 MVP 규칙

> 카탈로그: [재사용 아키텍처](README.md)
> 상태: 프로젝트 독립 적용 지침
> 문서 종류: 아키텍처 패키지
> 적용 대상: Domain 로직, Presenter, Presentation Model과 Unity View를 분리하는 클라이언트

## 1. 적용 조건

다음 조건을 만족하는 Unity 프로젝트에서 이 문서를 사용한다.

- 게임 규칙과 화면 연출을 분리해야 한다.
- 입력을 명시적인 요청 경계로 전달하고 확정 결과를 이벤트로 받아 표시한다.
- Scene 전환과 View 수명에 맞춰 구독과 리소스를 해제해야 한다.
- UI가 문자열 테이블과 현재 언어를 사용한다.

프로젝트가 MVVM, ECS 또는 엔진 외부 UI 프레임워크를 이미 표준으로 사용한다면 이름을 억지로 바꾸지 않는다. 아래의 상태 소유권과 의존 방향만 동등한 프로젝트 경계에 매핑한다.

## 2. 계층과 책임

| 계층 | 책임 | 금지 사항 |
|---|---|---|
| Domain Model | 게임 규칙, 상태 전환, 요청 소비, 확정 결과 이벤트 생성 | Unity 타입, View, BindableProperty, 패킷과 저장 DTO 참조 |
| Presentation Model | View가 관찰할 원본 상태와 값 보관 | 게임 규칙, 문자열 지역화, 저장·통신 수행 |
| Presenter | 입력 중계, Domain 이벤트 검증과 원본 상태 투영, Gateway·Storage 조정 | TMP와 GameObject 조작, 화면 문구와 숫자 서식 생성 |
| World Coordinator (선택) | 한 Scene 수명의 로컬 Domain과 월드 표현 객체를 소유하고 확정 이벤트를 UI 모델과 월드 연출에 전달 | 게임 결과 재계산, UI 문자열 생성, 전역 이벤트 버스 사용 |
| View | 모델 바인딩, 문자열 지역화, 단위·서식 적용, 애니메이션과 입력 전달 | Domain·패킷 직접 참조, 게임 결과 계산 |
| Lifecycle Adapter | Scene 진입·종료와 Presenter·View·리소스 연결 | 게임 규칙과 표시 값 계산 |
| Gateway / Storage | 서버와 로컬 저장 경계 | 패킷·저장 DTO를 View에 직접 노출 |

허용하는 기본 흐름은 다음과 같다.

```text
View --UIActions--> Presenter --Request--> Domain Model
View <--binding-- Presentation Model <--event projection-- Presenter
Domain Model --typed result event--> Presenter
Presenter <--> Gateway / Storage
Lifecycle Adapter --> Presenter + View
```

View는 Presenter 구체 타입 대신 의미별 UIActions 경계를 사용한다. Presentation Model은 Domain Model을 소유하지 않고 Domain Model은 Unity와 외부 저장 경계를 알지 못한다.

한 Scene 안에서 로컬 월드의 생성·해제와 월드 표현이 정확히 같은 수명을 가지면 별도 Presenter와 World View 래퍼를 중복으로 만들지 않아도 된다. 이 경우 Scene에 배치한 루트 `MonoBehaviour` 하나를 World Coordinator로 두고, 명시적으로 주입받은 Gateway·Presentation Model과 자신이 만든 Domain을 소유할 수 있다. Domain은 이 객체를 타입이 명확한 event sink로 받아 확정 이벤트를 전달한다. Coordinator는 이벤트 순서대로 원본 값을 Presentation Model에 투영하고, 같은 이벤트의 연출 값만 자식 View에 전달한다. UI 문자열과 게임 결과는 계산하지 않는다.

```text
Scene --Coordinator 참조·수명 연결--> World Coordinator
입력·시간 --명시적 호출--> World Coordinator --Request·Execute--> Domain
Domain --typed result event--> World Coordinator --> Presentation Model --> UI
                                             \--> 자식 월드 View 연출
```

Scene에 배치한 빈 월드 루트는 리소스 프리팹이 아니므로 다시 동적 로드하거나 풀에서 생성하지 않는다. 독립적으로 생성·회수하는 캐릭터 등의 표현 객체만 프로젝트 리소스 관리자와 풀을 사용한다. Scene 루트와 동적 프리팹을 같은 역할로 두 번 생성하지 않는다.

## 3. 입력과 Domain 실행

View는 클릭, 탭, 키 입력을 의미별 UIActions 메서드로 전달한다. Presenter는 입력 가능 상태를 확인한 뒤 프로젝트가 채택한 Request 또는 명시적 Domain 메서드 호출로 변환한다.

Domain은 자신의 실행 경계에서 요청을 소비하고 상태 변경을 끝낸 뒤 결과 이벤트를 발생시킨다. View나 Presenter가 Domain 필드를 직접 변경하거나 성공 결과를 미리 추측해 표시하지 않는다.

프레임 실행이 필요하면 프로젝트가 정한 Scheduler 또는 Coroutine 하나를 사용한다. 같은 책임을 `Update`, Unity Coroutine과 별도 Scheduler에 중복 등록하지 않는다. 실행 중단과 Scene 종료 시 Handle을 명시적으로 해제한다.

## 4. 출력과 이벤트 수명

- Started 이벤트는 최초 화면과 복원에 필요한 값을 제공한다.
- 실행 중 변경은 의미별 결과 이벤트가 변경 후 값 또는 명시적 증감값을 제공한다.
- 표시 값이 부족할 때 Presenter가 Domain Getter로 다시 조회하지 않는다. 이벤트 계약을 보완한다.
- 이벤트에는 `GameObject`, `Transform`, `Animator`, `Vector` 같은 Unity 타입을 넣지 않는다.
- 여러 이벤트가 발생하면 Domain 생성 순서를 Presenter 투영까지 보존한다.
- 이벤트는 한 번만 소비한다. 같은 이벤트를 View별로 다시 drain하거나 재주입하지 않는다.
- 전역 정적 EventBus와 Service Locator를 이벤트 수명 관리 수단으로 사용하지 않는다.
- 구독 또는 event sink는 Presenter와 Domain 실행 단위의 생성·종료 경계에서 등록하고 해제한다.
- 서버 확정 결과는 Gateway 응답으로 장기 진행 상태와 Presentation Model에 반영한다. 끝난 Domain 이벤트로 위장해 다시 주입하지 않는다.

## 5. Presentation Model 규칙

Presentation Model은 View가 표시 결정을 내리는 데 필요한 원본 상태만 가진다.

허용하는 값:

- `AccountEntryMode`, `ConnectionStatus`처럼 의미가 명확한 enum
- 거리, 수량, 회전, 진행도와 시간 같은 원본 수치
- 계정 ID, 닉네임과 코드처럼 기능 계약 자체가 문자열인 값
- 표시 여부, 입력 가능 여부와 요청 진행 여부
- 반복 프리팹에 투영할 구조화된 항목

금지하는 값:

- `CreateButtonText`, `DistanceLabelText`, `StatusText` 같은 완성된 UI 문구
- 문자열 테이블 조회가 끝난 지역화 문자열
- 단위와 숫자 서식이 적용된 문자열
- 오류 코드나 상태 enum을 문장으로 바꾼 값
- UI 컴포넌트 한 개를 갱신하기 위해서만 만든 장식용 모델

`BindableProperty<string>` 자체를 금지하지는 않는다. 닉네임과 코드처럼 원본 계약이 문자열이면 사용할 수 있다. 속성의 값이 UI를 떼어 내도 여전히 기능 상태로 의미가 있는지 판단한다.

기능별 상태와 생명주기가 다르면 하나의 Scene Model에 모두 몰아넣지 않는다. 로컬 월드, 계정 진입, 온라인 방처럼 독립적으로 생성·종료되는 기능은 각각 Presentation Model을 두고 Lifecycle Adapter가 필요한 조합만 연결한다. 필드를 줄이기 위한 이름뿐인 Wrapper는 만들지 않는다.

## 6. 문자열, 지역화와 표시 서식

문자열 키, 문자열 테이블 조회, 현재 언어, 단위와 표시 서식은 View 책임이다. Controller와 Presenter는 최종 화면 문자열을 만들지 않는다.

```text
Domain / Gateway 결과
    -> Presenter가 원본 상태와 값을 모델에 투영
    -> View가 상태 enum과 원본 값을 구독
    -> View가 문자열 테이블, 현재 언어와 표시 서식을 적용
    -> View가 TMP와 UI 컴포넌트를 갱신
```

고정 라벨, 버튼명과 placeholder는 View가 문자열 키를 소유하고 초기화 시 설정한다. 상태에 따라 문구가 달라지면 Presenter는 상태 enum만 바꾸고 View가 enum과 문자열 키를 매핑한다.

```csharp
// Presenter
_model.AccountEntryMode.Value = AccountEntryMode.CreateAccount;

// View
private void SetAccountEntryMode(AccountEntryMode mode)
{
    if (mode == AccountEntryMode.CreateAccount)
    {
        _authenticationButtonText.text = StringTable.Get(CreateAccountLabelStringKey);
        return;
    }
    if (mode == AccountEntryMode.Login)
    {
        _authenticationButtonText.text = StringTable.Get(LoginLabelStringKey);
        return;
    }

    _authenticationButtonText.text = string.Empty;
}
```

숫자는 원본 값과 데이터에서 받은 표시 자릿수를 모델에 보관한다. View가 현재 문화권, 단위 문자열과 서식을 적용한다. 언어가 실행 중 변경될 수 있으면 View가 언어 변경 경계를 구독하고 현재 모델 값으로 다시 표시한다.

다음 구현은 금지한다.

```csharp
private void ProjectAuthenticationButtonText()
{
    _model.AuthenticationButtonText.Value = StringTable.Get(CreateAccountLabelStringKey);
}
```

## 7. View와 연출

View는 TMP, 색상, Transform, Animator, 사운드, 보간과 프리팹 표시 상태를 소유한다. 연출 값은 저장, 서버 검증과 보상 입력에 사용하지 않는다.

콘텐츠가 결정해야 하는 대상, 순서, 보상과 난수를 View에서 만들지 않는다. Presenter가 Domain 결과를 다시 계산하지 않듯 View도 결과를 보정하지 않는다.

UI는 프리팹 우선으로 구성한다. 반복 UI는 프리팹 또는 직렬화 템플릿을 사용한다. 런타임 코드로 UI 계층을 조립해 프리팹 원칙을 우회하지 않는다.

동적 리소스와 풀링을 사용하는 프로젝트는 [`UnityDynamicResourcePooling`](UnityDynamicResourcePooling.md)을 함께 적용한다.

## 8. 생명주기

초기화 순서:

1. Lifecycle Adapter가 필요한 리소스와 View를 준비한다.
2. Presenter와 Presentation Model을 생성한다.
3. View에 Presentation Model과 UIActions를 연결한다.
4. 현재 모델 값으로 최초 화면을 표시한다.
5. ValueChanged, 입력과 Domain 이벤트를 구독한다.
6. 프레임 실행이 필요하면 마지막에 시작한다.

종료 순서:

1. 프레임 실행과 새 입력을 중단한다.
2. View와 Presenter의 구독을 해제한다.
3. Presenter와 Domain 실행 단위를 종료한다.
4. 풀 인스턴스를 초기화하고 회수한다.
5. 인스턴스가 참조한 외부 에셋을 해제한 뒤 로드 Handle을 해제한다.

초기화가 두 번 호출되거나 종료 뒤 이벤트가 도착해도 중복 구독과 다음 Scene 오염이 생기지 않아야 한다.

## 9. 적용 순서

기존 프로젝트를 정리할 때 다음 순서로 진행한다.

1. Controller와 Presenter의 문자열 테이블 조회, 숫자 서식, TMP 접근과 `...Text.Value` 대입을 검색한다.
2. 각 값을 원본 기능 상태, UI 고정 문구, 상태 문구와 서식 문자열로 분류한다.
3. 고정 문구의 키와 컴포넌트 갱신을 View로 이동한다.
4. 상태 문구를 enum으로 바꾸고 View에서 문자열 키와 매핑한다.
5. 숫자 문자열을 원본 수치와 표시 자릿수로 되돌린다.
6. Presentation Model의 불필요한 문자열 속성과 Presenter의 `Project...Text` 메서드를 제거한다.
7. 기능별 생명주기가 다른 상태가 하나의 Scene Model에 섞였으면 독립 Presentation Model로 분리한다.
8. View의 최초 표시, 값 변경 구독과 구독 해제를 연결한다.
9. Controller와 Presenter에 문자열 조회 또는 View 컴포넌트 접근이 남지 않았는지 다시 검색한다.
10. 빌드와 실제 Scene 진입·종료를 확인한다.

## 10. 프로젝트별 매핑

대상 프로젝트 적용 문서에는 다음 값을 기록한다.

- Domain, Scene, Presenter, Presentation Model, View와 UIActions 실제 경로
- 채택한 Request·Event 전달 방식
- 프레임 반복 관리자와 Handle 해제 방식
- 문자열 테이블과 현재 언어 경계
- UI 프리팹 루트와 반복 프리팹 생성 방식
- 동적 리소스 관리자, 풀과 아틀라스 관리자
- 데이터 원본, 생성 도구와 런타임 조회 방식
- 클라이언트 빌드와 Unity Editor 검증 방법

이 문서의 내용을 프로젝트 적용 문서에 복사하지 않는다. 적용 문서는 링크와 실제 매핑, 프로젝트 전용 예외만 기록한다.
