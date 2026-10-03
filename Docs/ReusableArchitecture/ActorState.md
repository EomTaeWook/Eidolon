# Mailbox Actor State Architecture

> 카탈로그: [재사용 아키텍처](README.md)
> 문서 종류: 아키텍처 패키지

메일박스에서 메시지를 직렬 처리하는 서버 Actor에 State Pattern을 적용하기 위한 프로젝트 독립 문서다.

## 전달 단위

- `ActorState.md`: 적용 대상, 적용 순서와 변경 지점
- `ActorStateArchitecture.md`: 역할, 전환, 비동기 경계와 검증 규칙

두 파일을 함께 전달한다. 특정 Actor의 상태 표와 프로토콜 매핑은 대상 프로젝트 문서에 별도로 작성한다.

## 적용 대상

- 연결 또는 사용자 Actor가 수명 동안 여러 허용 입력 상태를 가진다.
- Actor 메일박스가 메시지를 직렬 처리한다.
- 외부 I/O 뒤 Actor 실행 문맥으로 명시적으로 복귀해야 한다.
- 상태 전환과 전환 후 원본 메시지 재처리의 순서가 중요하다.

단일 메서드의 단순 분기, 프레임 Tick 기반 게임 로직과 저장 Tick을 복원하는 도메인 상태에는 사용하지 않는다. 동기식 도메인 상태는 [`DomainStateMachine`](DomainStateMachine.md)을 사용한다.

## 적용 순서

1. Owner Actor, 세션 Context와 상태 목록을 정의한다.
2. State별 허용 외부 입력과 내부 메시지를 표로 만든다.
3. StateController에는 등록·현재 상태·전환·디스패치만 둔다.
4. State는 허용 입력을 판별하고 Handler에 라우팅한다.
5. Handler는 요청 검증, Service 호출, Context 갱신, 응답과 전환 요청 순서를 소유한다.
6. 외부 I/O 뒤 Actor 문맥 복귀 지점을 명시한다.
7. 전환 뒤 원본 메시지를 다시 처리한다면 자기 게시 FIFO 보장을 확인한다.
8. 연결 종료, 게시 거부, 취소와 재접속 경로를 테스트한다.

## 프로젝트별로 바꿀 것

- 상태 enum과 전환 표
- State별 허용 프로토콜·메시지
- Handler·Service 매핑
- 세션 Context 필드와 수명
- Actor 문맥 복귀 API
- 자기 게시 FIFO 보장과 게시 실패 처리

## 그대로 유지할 것

- StateController의 구조 책임 제한
- Owner만 실제 상태를 변경하는 경계
- State와 Handler가 StateController를 참조하지 않는 규칙
- 비동기 작업 뒤 Actor 문맥 복귀
- 전환과 재라우팅 순서를 호출부에 드러내는 원칙
- 등록되지 않은 상태 전환을 프로그래밍 오류로 다루는 원칙

상세 규칙은 [`ActorStateArchitecture.md`](ActorStateArchitecture.md)를 따른다.
