# Eidolon.Mcp

`HttpListener`로 MCP 도구 서버를 제공하는 .NET 10 클래스 라이브러리다. 컨트롤러 생성에는 `Dignus` 1.3.0의 DI를 사용한다. 외부 패키지 제한에서 Dignus는 허용하며 ASP.NET Core, Avalonia와 Eidolon Core/App에 의존하지 않는다. 프로젝트별 기능은 `McpTool`의 실행 핸들러로 등록한다.

## 사용

```csharp
using Eidolon.Mcp;
using System.Text.Json.Nodes;

McpTool tool = new McpTool(new McpToolDefinition
{
    Name = "get_status",
    Description = "Read the application status.",
    ReadOnly = true,
    Idempotent = true,
    OpenWorld = false
}, (arguments, token) =>
{
    token.ThrowIfCancellationRequested();
    return Task.FromResult(new McpToolResult(new JsonObject
    {
        ["status"] = "ready"
    }));
});

await using McpHttpServer server = new McpHttpServer(new McpServerOptions
{
    Endpoint = new Uri("http://127.0.0.1:8190/mcp"),
    Name = "Example",
    Version = "1.0.0"
}, new[] { tool });

await server.StartAsync();
// Keep the application alive, then await server.StopAsync() or DisposeAsync().
```

`McpServerOptions`는 서버 생성 시 복사한다. 도구 정의와 스키마도 등록 시 복사하며 실행 중 목록은 고정한다. 같은 서버 인스턴스를 정지 후 다시 시작할 수 있고 해제 뒤에는 시작할 수 없다. 호출자는 서버의 수명과 오류 로그 콜백을 소유한다. `StateChanged`는 호출 스레드에서 발생하므로 UI 연결은 해당 UI 스레드로 전달한다.

시작·정지·해제는 호출자가 순서대로 호출하며 서버는 이 동작을 세마포어로 직렬화하지 않는다. 실행 중 재시작 요청은 별도 중복 시작 처리 없이 `HttpListener.Start()`로 전달하며 바인딩 실패는 원래 예외로 반환한다. 실패한 새 listener만 닫고 기존 실행 listener는 유지한다. 동시 HTTP 요청 수 제한은 별도의 요청 슬롯 세마포어가 담당한다.

## 구성

`Controllers`에 JSON-RPC 메서드마다 하나의 컨트롤러를 둔다. 초기화·핑·조회·호출·취소는 각각 `InitializeController`, `PingController`, `ServerDiscoverController`, `ToolsListController`, `ToolsCallController`, `NotificationsCancelledController`가 소유한다. `Protocol/McpDispatcher`는 컨트롤러 등록·라우팅과 공통 응답을 조합하며 도구의 업무 처리는 호스트가 등록한 핸들러로 전달한다.

`McpDispatcher`는 서버 구성 시 Dignus `ServiceContainer`에 모든 컨트롤러를 `LifeScope.Transient`로 등록하고 provider를 한 번 구성한다. 라우팅에는 각 컨트롤러의 `MethodName`과 타입을 저장하며 요청·알림을 처리할 때 해당 타입을 resolve해 새 컨트롤러를 실행한다. 등록 과정에서 컨트롤러를 생성하지 않는다. 생성자는 Dignus의 생성자 주입을 위해 public으로 선언하고 컨트롤러 타입은 internal을 유지한다. 서버 옵션·읽기 전용 도구 목록·오류 및 취소 콜백은 기존 인스턴스로 등록해 공유한다. 생성자 델리게이트의 표현식 트리 준비는 Dignus DI가 소유한다.

Eidolon App은 `Mcp/McpService.cs`의 `CreateTools()`에서 `new McpTool(new McpToolDefinition(name, description, schema), handler)`로 도구를 등록한다. 처리 메서드는 같은 클래스의 private 메서드로 모은다. `tools/call → ToolsCallController → McpService의 처리 메서드` 순서로 실행한다.

## 지원 범위

- Streamable HTTP의 JSON 응답 경로를 제공한다. SSE 스트림, stdio와 구 HTTP+SSE 전송은 제공하지 않는다.
- `2026-07-28`: `server/discover`, `ping`, `tools/list`, `tools/call`. 요청의 버전·기능 메타데이터와 HTTP 헤더를 검증하고 결과에 `resultType`·서버 식별 메타데이터를 붙인다.
- `2025-11-25`, `2025-06-18`, `2025-03-26`: 세션 ID를 발급하지 않는 기존 HTTP 초기화 호환 경로를 제공한다. `initialize`와 `notifications/initialized`를 받으며 초기화에서 최신 2025 계열을 협상한다. GET·DELETE는 405, 받은 알림은 202로 응답한다.
- 도구 목록은 이름순으로 전체 반환한다. 페이지 커서, 목록 변경 알림, resources, prompts, sampling, elicitation과 tasks는 지원한다고 광고하지 않는다.
- 도구 결과는 텍스트와 선택적 `structuredContent`다. 이미지 바이트·리소스 링크 전송은 현재 범위에 없다.

스키마는 JSON Schema 2020-12의 제한된 부분집합을 사용한다. 단일 `type`, `properties`, `required`, Boolean `additionalProperties`, `items`, `enum`, `minimum`·`maximum`, 문자열 길이·배열 길이 제한을 지원한다. `title`, `description`, `default`, `examples`는 메타데이터다. `$ref`, 조합 스키마, 정규식, 헤더 파라미터 확장 등 지원하지 않는 키워드는 등록 시 거부한다. 파일·모델 호환성 같은 업무 검증은 실행 핸들러가 소유한다.

필수 HTTP 헤더와 클라이언트 요청 예시는 [Eidolon MCP 사용법](../Docs/Mcp.md)을 참고한다. 전송·버전 계약의 원본은 [MCP HTTP 규격](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http), [버전 협상](https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning)이다.

## 수명과 입력 경계

로컬 루프백 주소만 바인딩하고 Host·Origin을 검증한다. 선택적 `AccessToken`은 Bearer 헤더로 검증하며 라이브러리는 인증 값을 저장하지 않는다. 요청 크기·동시 요청 수·읽기와 실행 대기 시간을 제한한다. 기본값은 1 MiB, 동시 8건, 요청당 2분이다. 요청과 도구 핸들러는 스레드풀에서 실행하므로 호스트 UI 상태는 UI 스레드로 전달해야 한다. 도구 실행 핸들러는 취소 토큰을 준수하고 시작한 하위 작업을 자신의 실행 경계에서 정리해야 한다. 서버는 핸들러가 시작한 외부 작업을 강제 종료하지 않는다.

정지는 HTTP 수락과 진행 중인 요청을 중단하고 요청 처리 수명을 기다린다. 클라이언트 연결 종료만으로 이미 수락한 업무를 취소하지 않는다. 도구 핸들러의 큐 추가·업무 수명은 호스트가 소유한다. 오류 콜백에 원본 예외를 전달하고 예상하지 못한 도구 예외의 스택·내부 정보는 클라이언트에 보내지 않는다.

2025 계열의 `notifications/cancelled`는 `requestId`가 같은 진행 중인 HTTP 호출의 취소 토큰에 전달한다. 완료한 호출과 이미 호스트 큐에 접수된 업무는 취소하지 않는다. 세션을 발급하지 않으므로 동시 클라이언트는 진행 중인 호출 ID가 서로 겹치지 않게 사용한다. 2026 HTTP에서는 취소 알림을 실행 경로로 사용하지 않는다.

Windows에서는 HTTP URL 예약 권한이 필요할 수 있다. 이 라이브러리는 권한 상승이나 시스템 URL 예약을 자동 변경하지 않는다. 현재 빌드·실행 검증은 수행하지 않았다.
