# Replay Validation & Sync Architecture

> 카탈로그: [재사용 아키텍처](README.md)
> 문서 종류: 아키텍처 패키지

이 문서 묶음은 Tick 기반 클라이언트 콘텐츠, 서버 재현 검증, 서버 권위 보상과 온라인·오프라인 동기화 구조를 다른 프로젝트로 옮기기 위한 독립 문서 패키지다.

## 전달 단위

다른 프로젝트에는 다음 두 문서를 함께 전달한다. 대상 저장소의 배치 경로는 프로젝트 문서 구조에 맞게 정한다.

```text
ReusableArchitecture/
├─ ReplayValidationSync.md
└─ ReplayValidationSyncArchitecture.md
```

- `ReplayValidationSync.md`: 전달 단위와 적용 시작점
- `ReplayValidationSyncArchitecture.md`: 계층, 요청·이벤트·기록 경계, 서버 재현, 결정론 난수, 보상, 멱등성과 오프라인 동기화 기준

호스트 프로젝트의 기획 문서, 구체 클래스 이름, 콘텐츠 규칙과 밸런스 데이터는 이 문서 묶음에 포함하지 않는다. 실제 타입과 정책의 대응표는 호스트 프로젝트 문서로 관리한다.

## 포함하지 않는 것

이 패키지는 구현 기준 문서이며 컴파일 가능한 공용 C# 라이브러리가 아니다. 기존 프로젝트의 다음 코드를 전달 대상으로 간주하지 않는다.

- 도메인 객체와 상태 클래스
- 구체 User Request와 View Event
- 저장·프로토콜 DTO와 DB 모델
- 보상 및 콘텐츠 템플릿
- 엔진 Controller와 UI 코드

공용 코드 패키지가 필요하면 계약 인터페이스, Record Envelope, 결정론 난수와 Seed 파생만 별도 라이브러리 후보로 검토한다. 도메인 요청·이벤트와 Cycle 상태는 대상 프로젝트에서 구현한다.

## 적용 순서

1. [`ReplayValidationSyncArchitecture.md`](ReplayValidationSyncArchitecture.md)의 핵심 불변식과 계층 의존성을 먼저 채택한다.
2. `프로젝트별 확정 항목` 표를 대상 프로젝트 규칙으로 작성한다.
3. 범용 이름인 `Cycle`, `Simulation`, `Replay Record`, `Post Action Record`를 대상 프로젝트의 실제 타입에 매핑한다.
4. `권장 구현 순서`대로 입력 생산, 기록, 서버 소비와 테스트를 한 단계씩 연결한다.
5. `필수 테스트`의 결정론 기준 벡터와 멱등·오프라인 시나리오를 대상 프로젝트 테스트에 추가한다.

## 그대로 유지할 것

- 콘텐츠 상태는 콘텐츠 객체만 변경한다.
- 사용자 행동은 콘텐츠가 실제 소비한 시점에 기록한다.
- View Event, Simulation Replay Record, Post Action Record와 서버 응답을 분리한다.
- 서버는 최소 Validation Event만 비교하고 보상과 영구 진행을 직접 계산한다.
- 온라인과 오프라인은 같은 기록 형식을 사용하며 전송 시점만 다르게 한다.
- 결정론 난수의 알고리즘, Seed 직렬화, 후보 순서와 호출 순서를 버전으로 고정한다.
- 정산은 Cycle ID 기반으로 멱등하게 처리한다.

## 프로젝트별로 바꿀 것

- Tick 길이와 Cycle 종료점
- 기록할 사용자 요청과 최소 검증 이벤트
- Simulation 종료 후 Post Action의 존재와 요청 필드
- 보상 테이블, 후보 구성과 난수 목적
- 저장·프로토콜 DTO와 DB 스키마
- 콘텐츠 버전 보존 기간
- 오프라인 허용 범위와 기기 충돌 UX
- 패킷 크기, 업로드 분할과 재시도 정책

범용 문서의 `[PROJECT-SPECIFIC]` 항목은 대상 프로젝트에서 반드시 결정한다. 결정되지 않은 항목을 코드 기본값으로 숨기지 않는다.
