# Replay Validation & Sync Architecture

> 카탈로그: [재사용 아키텍처](README.md)
> 상태: 프로젝트 독립 이식 기준
> 적용 범위: 클라이언트 시뮬레이션, 서버 검증·보상, 온라인·오프라인 동기화
> 프로젝트별 표기: `[PROJECT-SPECIFIC]` 적용 프로젝트에서 확정해야 하는 항목

---

## 1. 목적

이 문서는 클라이언트에서 즉시 진행되는 게임을 서버가 재현해 검증하고, 같은 구조로 온라인과 오프라인 플레이를 지원하기 위한 이식 가능한 구현 기준이다.

목표는 다음과 같다.

- 클라이언트 입력 반응은 네트워크 왕복 없이 즉시 처리한다.
- 서버는 클라이언트가 제출한 최종 재화와 보상을 신뢰하지 않는다.
- 서버는 모든 화면 이벤트가 아니라 판정에 필요한 입력과 최소 결과만 검증한다.
- 온라인과 오프라인은 서로 다른 게임 규칙을 만들지 않고 같은 기록과 정산 경계를 사용한다.
- 끊김, 재전송과 앱 종료가 있어도 한 정산 단위를 정확히 한 번만 승인한다.
- View 연출과 서버 권위 데이터를 분리해 콘텐츠 로직을 다른 엔진이나 서버에서도 실행할 수 있게 한다.

이 구조는 클라이언트를 신뢰할 수 있는 실행 환경으로 만들지 않는다. 특히 무제한 오프라인에서는 사용자가 로컬 저장과 메모리를 조작하거나 미래 Seed를 분석할 수 있다. 서버 재현은 허용되지 않은 결과 변경, 순번 건너뛰기와 중복 지급을 차단하지만 온라인 서버 직접 실행과 같은 수준의 비밀성은 제공하지 않는다.

---

## 2. 핵심 불변식

다른 프로젝트로 옮길 때 다음 원칙은 유지한다.

1. 콘텐츠 상태는 콘텐츠 객체만 변경한다.
2. UI 입력은 명령 객체로 만들어 큐에 넣고, 콘텐츠 실행 경계에서 소비한다.
3. 서버용 사용자 행동은 UI가 발생한 시점이 아니라 콘텐츠가 실제 소비한 시점에 기록한다.
4. 화면 투영 이벤트, 서버 재현 기록과 서버 정산 응답을 서로 다른 개념으로 둔다.
5. 서버가 비교할 결과 이벤트는 조작 탐지에 필요한 최소 집합만 선택한다.
6. 보상과 영구 진행은 서버가 계산하고 하나의 트랜잭션으로 저장한다.
7. 클라이언트와 서버는 같은 콘텐츠 버전, Tick 규칙, 난수 알고리즘, 후보 순서와 난수 호출 순서를 사용한다.
8. 서버 승인 전 로컬 결과는 `Pending`이며 승인된 서버 상태보다 높은 권위를 갖지 않는다.
9. 각 정산 단위는 단조 증가하는 ID와 멱등 키를 가진다.
10. 다른 기기 로그인은 새 오프라인 세대를 만들고 이전 기기의 미승인 진행을 폐기한다.

---

## 3. 전체 계층과 의존성

| 계층 | 범용 이름 | 책임 |
|---|---|---|
| Content Domain | Cycle, Simulation, 도메인 객체 | 규칙 실행, 상태 변경, 요청 소비, 콘텐츠 이벤트 발생 |
| Content Data | 실행·복원 데이터 | 한 실행 단위를 재현하는 최소 상태 |
| Connector | Controller, Adapter, Record Handler | 엔진 시간 변환, 요청 전달, 이벤트 투영, 기록 저장, DTO 변환 |
| Presentation | View Model, UI, World View | 표시와 입력 수집, 로컬 연출 |
| Local Persistence | 승인 스냅샷, 미승인 Journal | 앱 종료 복원, 재전송 가능한 기록 보존 |
| Network | Request/Response Client | 인증된 패킷 송수신과 재시도 |
| Server Replay | 서버용 콘텐츠 실행기 | 기록된 입력 재적용, 검증 이벤트 생성·비교 |
| Settlement | 서버 정산 서비스 | 보상, 도감, 재화와 진행의 원자적 반영 |

```text
View 입력
   │
   ▼
Connector ── IUserRequest ──> Cycle ──> Simulation / PostContent
   ▲                                │
   │                                ├─ View Event ──────────────> 화면 투영
   │                                ├─ Replay Record ───────────> Pending Journal
   │                                └─ Validation Event ────────> Pending Journal
   │                                                               │
   │                                                               ▼
   └──── Server Settlement Response <── Network <── Server Replay + Settlement
```

Content Domain은 UI 프레임워크, 저장 DTO, 네트워크 패킷과 DB 모델을 참조하지 않는다. Connector가 콘텐츠 타입과 외부 타입을 변환한다. 서버도 같은 콘텐츠 코드를 직접 참조하거나 동일 규칙을 가진 독립 모듈을 사용하되, 클라이언트와 서버 결과를 비교하는 기준 테스트를 반드시 공유한다.

권장 모듈 배치는 다음과 같다. 실제 폴더와 프로젝트 이름은 `[PROJECT-SPECIFIC]`이다.

```text
Game.Core                 순수 콘텐츠, 요청, View Event, Replay 계약
Game.Data                 버전이 고정된 생성 데이터와 템플릿
Client.Connector          엔진 시간, 저장·프로토콜 변환, Record Handler
Client.Presentation       View Model, UI, 연출
Shared.Protocol           네트워크 DTO와 프로토콜 ID
Server.Replay             서버 콘텐츠 생성과 기록 재현
Server.Settlement         검증 결과, 보상과 DB 트랜잭션
Tests.Determinism         Seed·난수·재현 기준 벡터
```

`Game.Core`는 `Client.Connector`, `Client.Presentation`, `Shared.Protocol`과 서버 저장 모듈을 참조하지 않는다. `Shared.Protocol` DTO를 콘텐츠 입력 타입으로 직접 사용하지 않고 양쪽 연결 계층에서 변환한다.

---

## 4. 실행 단위와 상태 소유권

### 4.1 Simulation과 Cycle

`Simulation`은 전투, 퍼즐, 러닝 한 판처럼 Tick으로 재현할 수 있는 핵심 실행 단위다. `Cycle`은 Simulation 종료 뒤 서버 보상 선택, 수집 확인 또는 다음 단계 전환까지 포함하는 더 큰 정산 단위다.

```text
Cycle
├─ Playing: Simulation 실행
├─ PostSimulation: 선택·수집·확인 같은 후속 콘텐츠 [선택]
└─ Completed: 서버에 제출 가능한 논리적 정산 단위
```

후속 콘텐츠가 없다면 `Simulation.Ended`가 곧 Cycle 종료점이다. 후속 사용자 행동이 보상에 영향을 주면 Simulation 종료에서 기록을 잘라 버리지 않고 후속 행동까지 같은 Cycle에 포함한다.

### 4.2 상태 소유권

- 전체 게임 Tick은 Simulation 하나가 소유한다.
- 상태별 경과 Tick은 해당 상태 객체가 소유한다.
- 도메인 데이터는 그 값을 변경하는 도메인 객체가 소유한다.
- 저장 객체와 런타임 객체는 참조를 공유하지 않는다. 생성 시 복사하고 저장 시 새 스냅샷을 만든다.
- 실제 날짜와 서버 시각은 동기화 경계에서만 사용하며 게임 Tick으로 사용하지 않는다.
- Controller와 View는 상태 조회를 반복해 화면을 구성하지 않는다. 최초 상태 이벤트와 이후 변경 이벤트를 투영한다.

상태별 실행 분기를 외부 Controller에 두지 않는다. Cycle의 현재 상태가 `Execute`, 요청 전달, 저장 스냅샷 생성과 종료 판정을 직접 구현하게 한다.

---

## 5. 입력, 이벤트, 기록과 응답의 분리

같은 객체를 모두 “이벤트”라고 부르면 저장과 권위 경계가 무너지므로 다음 역할을 분리한다.

| 종류 | 방향 | 목적 | 서버 전송 |
|---|---|---|---|
| User Request | View → Content | 사용자의 의도 전달 | 지원하는 구체 요청만 기록 |
| View Event | Content → View | 최초 화면과 변경 결과 투영 | 원칙적으로 전송하지 않음 |
| Replay Record | Content → Connector | 서버가 같은 입력을 Simulation에 재적용 | 전송 |
| Validation Event | Content → Connector | 서버 재현 결과와 비교할 최소 체크포인트 | 전송 |
| Post Action Record | PostContent → Connector | Simulation 종료 후 수집·선택 같은 Cycle 행동 증명 | 전송 |
| Settlement Response | Server → Connector | 영구 진행·재화·보상 확정 | Content에 재주입하지 않음 |

### 5.1 User Request

사용자 행동은 구체 명령 객체로 표현한다.

```csharp
public interface IUserRequest
{
}

public interface IReplayRecord
{
}

public class RequestPrimaryAction : IUserRequest, IReplayRecord
{
}

public class RequestPurchase : IUserRequest, IReplayRecord
{
    public int ProductTemplateId { get; set; }
}
```

`IUserRequest` 전체가 기록 인터페이스를 상속하지 않는다. Simulation 재현에 필요한 구체 요청만 `IReplayRecord`를 구현한다. 화면 열기나 로컬 카메라 조작처럼 콘텐츠 결과에 영향을 주지 않는 요청은 기록하지 않는다. Simulation 종료 후 Cycle 단계에서 소비되는 요청은 Simulation Replay에 합치지 않고 별도 Post Action Record 경계를 사용한다.

### 5.2 View Event

View 이벤트는 화면이 콘텐츠 객체를 다시 조회하지 않아도 되도록 변경 후 값 또는 명시적인 증감값을 가진다.

```csharp
public interface IContentEvent
{
    long GameTick { get; }
}

public class ResourceChanged : IContentEvent
{
    public long GameTick { get; set; }
    public int ResourceAmount { get; set; }
}
```

모든 이벤트를 범용 enum과 `TargetId`, `Value`에 합치지 않는다. 의미가 다른 사건은 구체 타입으로 분리한다.

### 5.3 Validation Event

서버가 직접 비교해야 하는 결과 이벤트만 별도 표식 인터페이스를 구현한다.

```csharp
public interface IValidationEvent : IContentEvent, IReplayRecord
{
}
```

검증 이벤트는 다음 기준으로 선택한다.

- 서버가 입력을 재현해 동일 결과를 만들 수 있다.
- 조작 시 보상 또는 핵심 진행 결과가 달라진다.
- 더 적은 이벤트로 같은 조작을 탐지할 수 없다.
- UI 연출 순서나 중간 표시 값만 확인하기 위한 이벤트가 아니다.

예를 들어 HP 변경 전체와 최종 종료 Tick만 비교해 충분하다면 매 Tick, 배경 전환, 사운드와 파티클 이벤트는 기록하지 않는다.

### 5.4 Post Action Record

Simulation 종료 후 별도 콘텐츠가 소비한 수집·선택 요청은 Cycle-level 기록으로 전달한다.

```csharp
public class RequestCollection : IUserRequest
{
}

public interface IPostActionRecordHandler
{
    void Process(long gameTick, RequestCollection record);
}
```

Post Action은 Simulation을 다시 실행하기 위한 입력이 아니다. 서버가 재현한 Cycle 상태에서 현재 활성 대상과 사용자 완료 의사를 검증하고 보상을 정산하기 위한 기록이다. Simulation Replay Record와 저장 목록 또는 처리 단계를 분리해도 되지만 같은 Cycle ID와 순서를 유지한다.

### 5.5 Settlement Response

서버가 반환한 보상, 신규 여부, 수집 횟수와 최종 재화는 장기 진행 및 Presentation 데이터다. 이를 Simulation 결과 이벤트로 다시 만들거나 이미 끝난 콘텐츠 객체에 주입하지 않는다. 이후 새 Cycle을 만들 때는 서버 승인으로 갱신된 장기 진행을 새 콘텐츠 입력으로 변환한다.

---

## 6. 요청 큐와 소비 시점 기록

View는 요청을 큐에 넣을 뿐 콘텐츠 상태를 즉시 바꾸지 않는다. Simulation은 Tick 처리 중 큐를 순서대로 소비한다.

```csharp
private void ProcessUserRequests()
{
    while (_userRequestQueue.TryRead(out var request) == true)
    {
        if (request is IReplayRecord replayRecord)
        {
            _recordHandler.Process(_gameTick, replayRecord);
        }

        if (request is RequestPrimaryAction)
        {
            ApplyPrimaryAction();
            continue;
        }

        if (request is RequestPurchase requestPurchase)
        {
            Purchase(requestPurchase.ProductTemplateId);
            continue;
        }

        throw new InvalidOperationException($"Unsupported user request. requestType:{request.GetType().FullName}");
    }
}
```

기록은 상태 변경 전에 연결 계층의 재전송 가능한 Journal로 인계한다. 인계에 실패하면 예외로 처리하고 상태 변경과 Cycle 완료를 진행하지 않는다.

지원하는 요청은 실제 효과가 없거나 도메인 검증에 실패해도 소비했다면 기록한다. 서버가 같은 상태에서 같은 요청을 소비해야 이후 입력 순서와 결과를 정확히 재현할 수 있기 때문이다. 반대로 현재 상태에서 지원하지 않아 큐에 넣지 않은 입력은 기록하지 않는다.

같은 Tick에 여러 요청이 있으면 목록 순서를 보존한다. Tick만 같다고 정렬하거나 요청을 합치면 재현 결과가 달라질 수 있다.

---

## 7. 최초 상태와 View 투영

Controller가 콘텐츠 객체의 Getter를 조합해 최초 화면을 만들면 복원 경계와 View 상태가 쉽게 어긋난다. 콘텐츠 시작 시 최초 화면에 필요한 값을 하나의 시작 이벤트에 담는다.

```text
Content.Start 또는 Load
→ Started Event 1회
→ Connector가 Presentation Model 구성
→ 이후 의미별 변경 이벤트만 적용
```

시작 이벤트에는 현재 Tick, 실행 규칙을 결정하는 템플릿 ID, 현재 자원, 진행 상태와 화면에 필요한 초기 플래그를 포함한다. 실행 중 View에 필요한 값이 빠졌다면 공개 Getter를 추가하지 않고 해당 변경 이벤트의 payload를 보강한다.

View Event는 서버 권위의 증거가 아니다. 화면 투영에 필요하다는 이유만으로 Replay Record나 Validation Event에 포함하지 않는다.

---

## 8. 서버 재현과 최소 검증

### 8.1 제출 단위

클라이언트는 Cycle별로 다음 데이터를 제출한다.

| 필드 | 내용 |
|---|---|
| `DeviceId` | 기록을 만든 활성 기기 |
| `Generation` | 오프라인 세대 또는 세션 세대 |
| `CycleId` | 단조 증가하는 정산 순번이자 멱등 키 |
| `BaseRevision` | 기록이 시작된 서버 승인 상태 |
| `ContentVersion` | 재현에 사용할 규칙과 템플릿 버전 |
| `ReplayRecords` | 소비 Tick과 순서를 보존한 Simulation 사용자 요청·검증 이벤트 |
| `PostActions` | Simulation 종료 후 소비된 Cycle-level 수집·선택 요청 |
| `FinalStateHash` | `[PROJECT-SPECIFIC]` 보조 진단용이며 단독 승인 근거로 사용하지 않음 |

### 8.2 검증 절차

```text
인증·활성 기기 확인
→ Generation, BaseRevision, CycleId 순번 확인
→ ContentVersion에 맞는 서버 시작 스냅샷 로드
→ 목적별 결정론 난수원 생성
→ 기록된 Tick과 순서대로 User Request 재입력
→ 서버 Validation Event 수집
→ 클라이언트 Validation Event와 개수·순서·필드 비교
→ 종료 조건과 종료 Tick 확인
→ 서버가 재현한 Cycle 상태에서 Post Action 검증
→ 서버가 보상과 영구 진행 계산
→ DB 트랜잭션 커밋
→ 다음 Revision과 정산 결과 응답
```

검증은 “클라이언트 결과가 가능한 범위인가”만 확인하지 않는다. 같은 시작 상태와 입력으로 정확히 같은 결과가 발생하는지 비교한다.

비교 대상마다 다음을 확인한다.

- 이벤트 개수
- 이벤트 순서
- 이벤트 Tick
- 대상 템플릿 또는 엔티티 ID
- 변경 후 핵심 상태
- 최종 종료 이벤트의 정확한 1회 발생 여부

서버는 클라이언트가 제출한 최종 재화, 보상량, 추첨 후보와 신규 여부를 승인 근거로 사용하지 않는다.

---

## 9. 보상 권위와 Simulation 이후 상호작용

보상은 서버만 확정한다. 클라이언트 콘텐츠는 보상 계산에 필요한 행동과 최소 결과를 기록하고 로컬에서는 예상 결과를 `Pending`으로 표시할 수 있다.

Simulation 종료 뒤 사용자의 한 번의 선택이나 수집이 있어야 보상이 결정되는 콘텐츠는 다음 구조를 사용한다.

```text
Simulation Ended
→ Cycle이 PostSimulation 상태로 전환
→ Appeared Event로 View 구성
→ 사용자 입력을 RequestCollection 또는 RequestSelection으로 전달
→ 콘텐츠가 요청 소비 사실을 기록
→ 필요한 경우 의미별 Result Event를 순서대로 발생
→ 콘텐츠 내부 완료 상태 변경
→ 서버가 기록과 활성 서버 상태로 보상 확정
→ Settlement Response로 UI·영구 진행 갱신
```

이 패턴의 핵심은 다음과 같다.

- View가 이미 아는 입력 수신 사실만 확인하는 이벤트는 만들지 않는다.
- 요청에는 서버가 이미 소유한 대상 ID, 로컬 연출 index와 클라이언트 계산 보상을 넣지 않는다.
- 요청 하나가 여러 의미 있는 결과를 만들거나 완료 순서를 View에 알려야 하면 `ItemCollected`, `Completed` 같은 구체 Result Event를 발생시킨다. 이는 입력 확인이 아니라 콘텐츠가 결정한 결과 투영이다.
- Result Event가 필요하지 않은 단순 Post Action은 콘텐츠의 내부 완료 상태만 변경할 수 있다.
- Post Action Record Handler는 소비 Tick과 같은 요청 객체를 저장·전송 경계로 넘긴다.
- 로컬 전용 애니메이션은 View 입력으로 즉시 시작할 수 있다. 콘텐츠가 결과 순서나 index를 소유하면 해당 Result Event를 기준으로 연출한다.
- 신규·중복, 수집 횟수, 보상과 최종 재화는 서버 응답으로 갱신한다.
- 재전송 시 서버는 Cycle ID와 활성 대상 상태로 중복 지급을 막는다.

사용자의 선택값이 실제 보상 후보를 결정하는 게임이라면 선택 식별자는 요청에 포함한다. 단순히 여러 시각 요소 중 하나를 밝히는 연출 index라면 포함하지 않는다. 요청 필드는 서버 판정에 실제로 소비되는 값만 가진다.

---

## 10. 결정론 난수

### 10.1 난수 재현 조건

같은 Seed만으로는 충분하지 않다. 다음 항목이 모두 고정돼야 한다.

- 난수 알고리즘
- Seed 범위와 바이트 표현
- 후보 목록 정렬 순서
- 확률 누적 방식
- 난수 호출 횟수와 호출 순서
- 정수 범위의 포함·제외 규칙
- 콘텐츠 데이터와 시뮬레이션 코드 버전

게임 도중 재현 대상 난수에 런타임 기본 `Random`을 직접 사용하지 않는다. 고정 구현의 `IRandomSource`를 주입한다.

### 10.2 목적별 Seed 분리

한 Cycle 안에서도 게임 생성, 보상 추첨과 후속 수집 대상 추첨은 서로 다른 난수 흐름을 사용한다.

```text
PurposeSeed = StableHash(
    RootSeed,
    Generation,
    CycleId,
    RandomPurpose)
```

한 기능이 조건부로 실행돼도 다른 기능의 난수 수열이 밀리지 않도록 `RandomPurpose`를 분리한다. 입력 직렬화의 바이트 순서와 해시 결과를 정수 Seed로 바꾸는 규칙도 버전으로 고정한다.

온라인에서는 서버 내부 암호학적 난수로 직접 추첨할 수 있다. 오프라인 결과를 클라이언트와 서버가 함께 재현해야 한다면 서버가 발급한 Root Seed와 고정 파생 규칙을 사용한다. 클라이언트가 임의로 보낸 Root Seed나 추첨 결과는 신뢰하지 않는다.

### 10.3 기준 벡터

다른 언어나 런타임으로 이식할 때 다음 기준 벡터를 테스트 자산으로 둔다.

- Seed 파생 입력과 예상 Seed
- 고정 Seed에서 최초 N개 난수 값
- 후보 정렬 뒤 예상 선택 ID
- 경계값 `Next(1)`, 최대 후보 수와 확률 합계
- 목적별 Seed가 서로 다른지 확인하는 테스트

---

## 11. 온라인과 오프라인의 공통 기록 파이프라인

온라인과 오프라인은 기록 생성 방식이 같다. 차이는 기록을 서버에 보내는 시점뿐이다.

### 11.1 공통 로컬 저장

```text
마지막 승인 ServerSnapshot
+ Pending Cycle Journal 0..N
+ 현재 실행 중 Cycle Snapshot
```

기록 Handler는 콘텐츠 상태 변경 전에 요청을 Journal에 안전하게 인계한다. 앱이 종료돼도 서버에 제출할 원본 순서를 복원할 수 있어야 한다. 승인 전 기록을 최신 스냅샷 하나로 압축하거나 제거하지 않는다.

### 11.2 온라인

1. 기록을 로컬 Pending Journal에 추가한다.
2. 가장 오래된 미승인 Cycle부터 즉시 전송한다.
3. 서버 승인 응답의 Revision과 정산 결과를 적용한다.
4. 승인된 앞부분만 Journal에서 제거한다.
5. 응답이 유실되면 같은 Cycle ID와 Generation으로 재전송한다.

### 11.3 오프라인

1. 마지막 승인 스냅샷에서 계속 실행한다.
2. Cycle별 입력, 검증 이벤트, Post Action과 임시 추첨 결과를 순서대로 보관한다.
3. 로컬 재화와 수집 결과는 `Pending`으로 표시한다.
4. 네트워크 복구 뒤 온라인 신규 진행보다 미승인 Cycle을 먼저 제출한다.
5. 서버는 유효한 연속 앞부분만 승인하고 최초 실패에서 중단한다.
6. 실패 Cycle 이후의 의존 진행도 함께 거부하고 마지막 승인 스냅샷으로 복구한다.

오프라인 횟수를 제한하지 않으면 업로드를 여러 패킷으로 나눌 수 있어야 한다. 분할해도 Cycle ID 순서와 Cycle 단위 원자성을 유지한다.

---

## 12. 계정, 기기와 오프라인 세대

서버는 계정마다 하나의 활성 기기와 하나의 활성 `Generation`만 허용한다.

```text
A 기기: Generation 7에서 오프라인 진행
→ B 기기 로그인
→ 서버가 B를 활성 기기로 지정하고 Generation 8 발급
→ B는 최신 승인 서버 상태에서 시작
→ A의 Generation 7 미승인 기록은 모두 거부
```

기기 충돌 시 서로 다른 기록을 병합하지 않는다. 뒤의 Cycle은 앞의 미승인 결과에 의존하므로 일부를 임의로 골라 승인하지 않는다.

필수 서버 상태는 다음과 같다.

- 활성 `DeviceId`
- 현재 `Generation`
- 다음 승인 `CycleId`
- 마지막 승인 `Revision`
- 재현용 Root Seed
- 콘텐츠 버전
- 최근 정산 결과 또는 멱등 응답을 복구할 수 있는 정보

계정 ID가 클라이언트에서 생성되더라도 재현용 Root Seed는 별도 값으로 다룬다. 계정 식별자의 일반 해시를 보안 Seed로 사용하면 예측 가능성과 런타임 종속 문제가 생긴다.

---

## 13. 기록 모델과 직렬화

범용 기록 Envelope는 다음 정보를 가진다.

```csharp
public class ReplayRecordData
{
    public long GameTick { get; set; }
    public string RecordType { get; set; }
    public IReplayRecord Record { get; set; }
}
```

- `RecordType`은 구체 타입의 안정된 식별자다.
- `Record`를 먼저 JSON 문자열로 만들지 않고 Envelope 전체를 한 번만 직렬화한다.
- 역직렬화는 서버와 클라이언트에 등록된 허용 타입 목록만 사용한다.
- 타입 이름을 리팩터링하면 과거 기록 호환 전략이나 별도 안정 ID가 필요하다.
- 외부 패킷 DTO는 Domain Record 객체와 타입을 공유하지 않아도 된다. Connector가 필드별로 변환한다.
- 기록 스키마, 콘텐츠 데이터와 실행 코드는 함께 버전 관리한다.

Cycle 기록은 append-only 순서를 유지한다. 서버 DB에 모든 원본 기록을 영구 보존할 필요는 없지만, 승인 트랜잭션이 끝날 때까지 서버 요청 메모리와 클라이언트 Pending Journal에는 원본이 있어야 한다.

Post Action은 Simulation Replay Envelope에 포함하거나 별도 목록으로 직렬화할 수 있다. 별도 목록을 권장하며 각 항목은 소비 Tick, 안정된 Action Type과 서버 판정에 실제 필요한 요청 필드만 가진다. 두 형식 중 무엇을 선택해도 같은 Cycle ID 안에서 Simulation 종료 뒤의 순서를 잃지 않아야 한다.

---

## 14. 멱등성과 트랜잭션

서버 정산의 멱등 키는 `(AccountId, Generation, CycleId)`처럼 한 계정의 한 실행 단위를 유일하게 식별해야 한다.

- 기대한 다음 Cycle ID보다 작으면 이미 처리됐는지 확인하고 이전 정산 결과 또는 현재 서버 상태를 반환한다.
- 기대한 다음 Cycle ID보다 크면 중간 기록이 빠졌으므로 거부한다.
- 같은 ID의 재전송은 보상과 수집 횟수를 다시 증가시키지 않는다.
- 검증, 영구 진행, 재화, 도감, 보상 지급, 다음 Cycle ID와 Revision 갱신을 하나의 DB 트랜잭션으로 처리한다.
- 응답 생성에 필요한 최근 정산 결과는 재전송 정책에 맞는 기간 또는 개수만 보관한다.

DB 커밋에 성공하고 응답만 유실될 수 있으므로 “응답을 받지 못했다”는 사실을 “서버가 처리하지 않았다”로 해석하지 않는다.

---

## 15. 프로젝트별 확정 항목

다른 프로젝트에 적용하기 전에 다음을 명시적으로 결정한다.

| 항목 | 결정 내용 |
|---|---|
| Cycle 경계 | 어디서 시작하고 어떤 후속 콘텐츠까지 포함하는가 |
| Tick 규칙 | Tick 길이, 누적과 일시 정지 기준 |
| 기록할 요청 | 서버 재현 결과에 영향을 주는 구체 요청 목록 |
| 후속 행동 | Simulation 종료 뒤 별도로 기록할 Post Action 목록과 필드 |
| 검증 이벤트 | 최소 비교 이벤트와 필드 |
| 종료 조건 | 종료 이벤트, 발생 횟수와 종료 Tick |
| 보상 권위 | 서버가 다시 계산할 후보, 확률, 보상 테이블 |
| 난수 규칙 | 알고리즘, Seed 파생, 목적 enum, 후보 순서 |
| 콘텐츠 버전 | 과거 기록을 재현할 코드·데이터 보존 방식 |
| 오프라인 정책 | 허용 기간·횟수, Pending 사용 범위 |
| 기기 정책 | 활성 기기 수와 Generation 무효화 규칙 |
| 멱등 정책 | Cycle ID, 최근 결과 보존과 재전송 응답 |
| 복구 UX | 검증 실패, 기기 충돌과 미승인 진행 폐기 안내 |
| 패킷 제한 | Cycle당 최대 기록 수, 패킷 크기와 분할 방식 |

이 표에서 결정되지 않은 항목을 코드의 기본값이나 암묵적 관례로 대신하지 않는다.

---

## 16. 권장 구현 순서

1. Content Domain을 엔진과 네트워크 의존성에서 분리한다.
2. 모든 진행 시간과 상태 타이머를 Tick으로 통일한다.
3. 외부 상태 변경 API를 User Request 큐로 교체한다.
4. 최초 상태와 변경 결과를 구체 View Event로 투영한다.
5. 서버 재현에 필요한 구체 요청만 Replay Record로 지정한다.
6. 서버가 비교할 최소 Validation Event를 선정한다.
7. Simulation 이후 사용자 행동이 있으면 별도 Post Action Record 경계를 만든다.
8. Connector에 View Event Queue와 Replay Journal을 분리한다.
9. 로컬 저장을 승인 스냅샷, 현재 Cycle과 Pending Journal로 나눈다.
10. 고정 난수 알고리즘, 목적별 Seed와 기준 벡터를 구현한다.
11. 서버 Replay Runner로 동일 기록의 결과 일치를 검증한다.
12. 보상과 영구 진행을 서버 트랜잭션으로 이동한다.
13. Cycle ID 기반 멱등 재전송을 구현한다.
14. Generation과 활성 기기 충돌 정책을 구현한다.
15. 마지막으로 오프라인 연속 업로드, 부분 승인과 복구 UX를 연결한다.

각 단계는 입력 생산부터 실제 소비와 테스트까지 한 번에 끝낸다. 후속 기능을 예상한 비어 있는 이벤트, 상태 필드와 범용 추상화를 미리 만들지 않는다.

---

## 17. 필수 테스트

### 콘텐츠 단위 테스트

- 같은 시작 상태와 요청 순서가 같은 View Event와 Validation Event를 만든다.
- 지원 요청은 효과 성공 여부와 관계없이 소비 순서대로 기록된다.
- 지원하지 않는 상태의 요청은 기록되지 않는다.
- 같은 Tick의 여러 요청 순서가 보존된다.
- 시작 이벤트만으로 최초 View 상태를 구성할 수 있다.
- 완료 뒤 중복 요청이 상태와 기록을 다시 만들지 않는다.
- Simulation Replay Record와 Post Action Record가 서로 다른 처리 경계로 전달된다.

### 결정론 테스트

- 클라이언트와 서버의 Seed 파생 기준 벡터가 같다.
- 같은 Seed의 최초 N개 난수 수열이 같다.
- 후보 입력 순서가 달라도 고정 정렬 뒤 선택 결과가 같다.
- 목적별 Seed가 서로 다른 난수 흐름을 만든다.
- 콘텐츠 버전이 다른 기록을 잘못된 구현으로 재현하지 않는다.

### 서버 검증 테스트

- 요청 누락, 추가, 순서 변경과 Tick 변경을 거부한다.
- Validation Event 누락, 추가, 순서와 필드 변경을 거부한다.
- 종료 이벤트가 없거나 두 번 발생하면 거부한다.
- 클라이언트가 보상, 신규 여부와 최종 재화를 조작해도 서버 계산값만 반영한다.
- 같은 Cycle을 반복 제출해도 보상이 한 번만 지급된다.
- Cycle 순번을 건너뛰거나 재사용하면 정책대로 거부 또는 멱등 응답한다.

### 오프라인 테스트

- 여러 Cycle을 오프라인으로 진행한 뒤 순서대로 승인한다.
- 중간 Cycle 실패 시 그 이후 Cycle을 승인하지 않는다.
- 업로드 응답 유실 뒤 같은 ID 재전송이 중복 지급되지 않는다.
- 다른 기기 로그인 뒤 이전 Generation 기록을 모두 거부한다.
- 앱 종료 시 현재 Cycle과 Pending Journal을 손실 없이 복원한다.

---

## 18. 금지할 패턴

- Controller나 UI가 Domain 객체의 상태를 직접 변경한다.
- 화면을 그리기 위해 매 프레임 Domain Getter를 조합한다.
- UI 입력 시각을 서버 행동 Tick으로 기록한다.
- 모든 View Event를 서버에 보내거나 모든 Tick 상태를 저장한다.
- 최종 상태 해시 하나만 비교하고 중간 핵심 결과를 검증하지 않는다.
- 클라이언트가 보낸 보상량, 추첨 결과와 신규 여부를 그대로 저장한다.
- 런타임 기본 문자열 해시나 플랫폼별 `Random`을 재현 Seed에 사용한다.
- 하나의 난수원을 기능 전체가 공유해 조건부 호출이 다른 결과를 밀어낸다.
- 승인 전 기록을 최신 스냅샷으로 덮어써 원본 순서를 잃는다.
- 응답 유실 재시도에서 새 Cycle ID를 발급한다.
- 서로 다른 기기의 미승인 진행을 병합한다.
- View가 이미 아는 자신의 입력을 확인하기 위한 `Accepted`나 내용 없는 완료 이벤트를 추가한다.
- 로컬 연출 index를 서버 보상 입력으로 전송한다.
