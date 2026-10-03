# Deterministic Domain State Machine

> 카탈로그: [재사용 아키텍처](README.md)
> 문서 종류: 아키텍처 패키지

동기식 Tick 또는 명시적인 실행 호출로 진행하며 저장·복원이 필요한 도메인 객체의 State Pattern을 위한 프로젝트 독립 문서다.

## 전달 단위

- `DomainStateMachine.md`: 적용 조건, 적용 순서와 변경 지점
- `DomainStateMachineArchitecture.md`: Owner·StateMachine·State 책임, 복원과 이벤트 규칙

두 파일을 함께 전달한다. 실제 상태 이름, 전환 조건, 저장 모델과 이벤트는 대상 프로젝트 문서에서 정의한다.

## 적용 대상

- 객체가 여러 행동 상태를 가지며 상태마다 실행 규칙과 자체 Tick이 다르다.
- 같은 입력으로 같은 결과를 내는 동기식 실행이 필요하다.
- 상태별 경과·남은 Tick을 저장하고 복원해야 한다.
- 상태 변경 결과를 의미별 이벤트로 외부에 투영한다.

네트워크 세션 Actor처럼 비동기 메일박스와 Handler 라우팅이 중심이면 [`ActorState`](ActorState.md)를 사용한다.

## 적용 순서

1. 도메인 Owner가 소유할 실제 데이터와 상태별 데이터부터 분리한다.
2. 상태 목록, 전환 원인, 상태별 실행과 종료 조건을 표로 만든다.
3. 상태 객체 생성자에 필요한 허용 Tick과 복원 Tick을 전달한다.
4. 공통 StateMachine에는 등록과 Enter·Exit 전환만 둔다.
5. Owner가 전환 조건을 판정하거나 상태가 Owner의 내부 전환 경계를 호출하도록 정한다.
6. 상태는 Owner의 의미 있는 변경 메서드를 호출하고 Owner가 이벤트를 발생시킨다.
7. 신규 시작과 복원 시작이 같은 `Enter` 경계를 사용하도록 검증한다.
8. 저장 왕복, 경계 Tick, 같은 상태 중복 전환과 완료 후 추가 입력을 테스트한다.

## 프로젝트별로 바꿀 것

- 상태 enum과 상태 클래스
- 전환 조건과 우선순위
- 상태별 허용·경과·남은 Tick
- Owner가 소유할 데이터와 상태가 소유할 데이터
- 의미별 이벤트와 저장 모델
- 상태 재사용 여부와 완료 표현

## 그대로 유지할 것

- 생성 완료 시 실행 가능한 상태 객체
- 복원 Tick의 생성자 검증
- `Enter`에서 복원값을 무조건 초기화하지 않는 규칙
- StateMachine의 최소 책임
- 상태가 외부 프레임워크·저장 DTO·이벤트 처리기를 모르는 경계
- Owner가 의미 있는 변경 완료 뒤 이벤트를 발생시키는 규칙

상세 규칙은 [`DomainStateMachineArchitecture.md`](DomainStateMachineArchitecture.md)를 따른다.
