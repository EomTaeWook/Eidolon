# Mailbox Actor State Architecture

> 카탈로그: [재사용 아키텍처](README.md)
> 상태: 프로젝트 독립 이식 기준

---

## 1. 목적과 전제

이 패턴은 Actor의 현재 상태에 따라 허용 입력과 수명 자원이 달라질 때 업무 로직과 상태 전환을 분리한다. 다음 전제를 요구한다.

- Actor 메일박스가 메시지를 한 번에 하나씩 처리한다.
- 외부 비동기 작업 뒤 Actor 문맥으로 복귀하는 API가 있다.
- 상태 전환 메시지와 같은 Actor의 자기 게시 순서를 설명할 수 있다.

메일박스가 없는 일반 객체나 Tick 기반 도메인 객체에 이 패턴을 그대로 적용하지 않는다.

## 2. 역할

| 구성 요소 | 담당 책임 | 담당하지 않는 책임 |
|---|---|---|
| Owner Actor | StateController와 Context 소유, 실행 문맥 제공, 전환 메시지 처리 | 요청별 DB 조회와 응답 조립 |
| StateController | State 등록, 최초 상태, 현재 상태, 전환과 입력 디스패치 | 외부 I/O, 검증과 세션 업무 |
| State | 현재 상태의 허용 입력 판별, Handler와 내부 메시지 라우팅, 상태 수명 처리 | 요청 업무와 StateController 직접 조작 |
| Handler | 요청 검증, Service 호출, Context 갱신, 응답과 전환 요청 | 상태 저장과 StateController 직접 조작 |
| Service | DB, 다른 Actor, HTTP와 외부 시스템 처리 | 현재 State 결정 |
| Session Context | 연결·사용자·인증 데이터 보관 | State 등록과 전환 |

Owner만 StateController를 소유한다. State와 Handler에 StateController 접근 경로를 제공하지 않는다.

## 3. 최초 상태

Owner는 생성 중 필수 구성 요소와 StateController를 만든 뒤 최초 상태를 한 번 설정한다.

```csharp
public SessionActor(IServiceProvider serviceProvider)
{
    _stateController = new StateController(this, serviceProvider);
    _stateController.ChangeState(SessionStateType.Initial);
}
```

- 일반 메시지마다 초기화 여부를 검사하지 않는다.
- `_isStateInitialized`, `EnsureStateInitialized` 같은 지연 초기화 장치를 두지 않는다.
- 최초 `Enter`가 Actor 시작 이후 문맥을 요구한다면 명시적 초기화 메시지를 한 번 게시한다.

## 4. StateController

허용하는 책임은 다음과 같다.

- 상태 컬렉션과 현재 상태 보관
- 상태 등록
- 최초 상태 설정과 상태 전환
- 현재 상태로 메시지·패킷 전달

기능 이름이 붙은 요청 메서드, DB·HTTP 호출, 요청 검증, 응답 생성과 세션 데이터는 넣지 않는다.

등록되지 않은 상태 전환은 프로그래밍 오류다. 조용히 무시하면 호출자는 전환이 성공했다고 오인하므로 구체적인 오류로 드러낸다. 같은 상태 재진입이 필요하면 무조건 Exit·Enter하지 말고 프로젝트 상태 표에서 재진입 의미를 먼저 정의한다.

## 5. 런타임 전환

Owner의 실제 `ChangeState`는 private으로 두고 State와 Handler에는 전환 메시지를 게시하는 경계만 제공한다.

```csharp
internal readonly record struct ChangeStateMessage(SessionStateType StateType)
    : IActorMessage;

internal void PostStateChange(SessionStateType stateType)
{
    VerifyContext();
    Self.Post(new ChangeStateMessage(stateType), Self);
}

private void ChangeState(SessionStateType stateType)
{
    VerifyContext();
    _stateController.ChangeState(stateType);
}
```

Owner는 전환 메시지를 다른 상태별 메시지보다 먼저 판별한다. 생성자 최초 설정, 강제 종료와 Actor 자체 수명 처리는 Owner가 private 전환을 직접 호출할 수 있다.

## 6. State

- `Enter`와 `Exit`에는 해당 상태 수명에 정확히 묶이는 자원 시작·정리만 둔다.
- 외부 요청은 허용 여부를 판별한 뒤 State별 Handler로 전달한다.
- 내부 완료 메시지는 State가 현재 흐름을 확인한 뒤 Handler 또는 Service 경계로 전달한다.
- 요청값 검증, Repository 호출, DB 모델 생성과 응답 조립은 넣지 않는다.
- 처리할 입력이 없는 State에 형식용 빈 Handler를 만들지 않는다.

State가 Handler의 의존성 해석 Context를 보관할 수는 있지만 직접 외부 I/O를 수행할 권한을 의미하지 않는다.

## 7. Handler와 비동기 문맥

Handler의 처리 메서드 하나에서 요청의 실행 순서를 확인할 수 있어야 한다.

```text
입력 검증
→ Service 호출
→ Actor 문맥 복귀
→ 결과 검증과 Session Context 갱신
→ 상태 전환 요청
→ 응답 게시
```

- 외부 시스템 세부 구현은 Service에 위임한다.
- 비동기 작업 뒤에는 Actor 문맥으로 복귀한 다음 Context와 상태를 변경한다.
- 실패 경로마다 상태 유지, 실패 전환 또는 연결 종료를 명시한다.
- 전환 메시지를 게시한 뒤 새 State의 Handler를 직접 호출하지 않는다.

## 8. 전환 후 재라우팅

전환 직후 새 State가 원본 메시지를 처리해야 하면 상태 전환과 원본 메시지를 같은 Owner 메일박스에 연속 게시한다.

```csharp
owner.PostStateChange(SessionStateType.Ready);
owner.Self.Post(message, owner.Self);
```

- 두 게시 사이에 `await`, 외부 호출, 조건 분기와 다른 작업을 넣지 않는다.
- 동일 발신자의 연속 자기 게시가 FIFO인지 확인한다.
- 게시 거부 시 두 단계 모두 성공한 것으로 처리하지 않는다.
- FIFO를 보장하지 않으면 Owner가 제공하는 하나의 직렬 실행 메시지로 모델링한다.
- `PostStateChangeAndMessage` 같은 편의 메서드로 순서를 숨기지 않는다.

## 9. 종료와 취소

- 연결 종료와 Kick처럼 모든 상태에 공통인 제어 메시지는 Owner가 상태 디스패치보다 먼저 처리한다.
- 종료 상태 진입은 상태별 스케줄러와 구독을 먼저 정리한다.
- 외부 I/O 완료 뒤 Actor가 종료됐거나 세션이 교체됐을 수 있으므로 문맥 복귀 뒤 현재 Context를 다시 검증한다.
- 같은 계정의 새 Actor가 기존 Actor를 교체한다면 Registry 교체와 이전 Actor 종료 순서를 프로젝트 문서에 기록한다.

## 10. 프로젝트 적용 문서

대상 프로젝트는 다음 항목을 별도 문서로 유지한다.

- 상태 목록과 전환 표
- State별 허용 입력
- Handler와 Service 매핑
- Session Context 소유 필드
- 최초 상태 설정 위치
- Actor 문맥 복귀 API
- 자기 게시 FIFO 보장
- 게시 거부, 연결 종료, 취소와 재접속 처리
- 범용 규칙과 현재 구현의 차이

## 11. 필수 테스트

- 최초 메시지 전에 최초 State가 설정된다.
- 현재 State에서 허용하지 않는 입력이 업무 Handler에 도달하지 않는다.
- 외부 I/O 뒤 Actor 문맥으로 복귀하기 전에 Context를 변경하지 않는다.
- 전환 메시지가 원본 재라우팅보다 먼저 처리된다.
- 전환 뒤 이전 State의 Handler를 직접 실행하지 않는다.
- 상태 종료 시 상태 수명 자원을 정리한다.
- 등록되지 않은 상태 전환이 명시적으로 실패한다.
- 세션 종료와 새 Actor 교체가 중복 등록을 남기지 않는다.

## 12. 금지 패턴

- StateController가 기능별 Service처럼 확장됨
- State나 Handler가 StateController를 직접 호출함
- State에 Repository 호출과 응답 조립을 작성함
- 비동기 완료 스레드에서 Actor Context를 변경함
- 전환 결과를 임시 State 필드에 저장하고 나중에 적용함
- 전환과 원본 재라우팅 순서를 편의 메서드 안에 숨김
- 등록되지 않은 상태 전환을 조용히 무시함
- 내용 없는 State나 Handler를 형식만 맞추기 위해 추가함
