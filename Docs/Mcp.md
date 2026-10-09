# Eidolon MCP

설정 → 일반 → MCP 연결에서 **서버 켜기**를 누른 뒤 MCP 클라이언트에 `http://127.0.0.1:8190/mcp`를 등록한다. Eidolon이 실행 중이고 초기화를 마쳐야 도구를 사용할 수 있다. 서버는 자동으로 켜지지 않으며 **서버 끄기**와 앱 종료에서 정리한다. 현재 앱 호스트는 같은 PC의 연결만 받고 브라우저의 다른 Origin은 거부한다.

## 도구

| 도구 | 기능 |
|---|---|
| `eidolon_get_status` | 엔진·요청 가능 상태, 현재 작업, 대기 건수, 진행률과 오류 조회 |
| `eidolon_list_models` | 사용할 체크포인트·LoRA의 ID, 이름, 계열과 트리거 조회 |
| `eidolon_get_instructions` | 저장된 공통 생성 지침과 제외할 요소 조회 |
| `eidolon_generate_image` | 설명과 선택적 참고 이미지로 생성 요청 추가 |
| `eidolon_edit_image` | 참고 이미지와 설명으로 그림체 편집 요청 추가 |
| `eidolon_list_images` | 현재 출력 폴더의 PNG 경로와 저장된 생성 조건을 페이지로 조회 |
| `eidolon_create_sprite_animation` | 기준 이미지·동작 설명으로 스프라이트 프레임 초안 제작 요청 추가 |
| `eidolon_create_views` | 정면·측면·후면·윗면 사면도 초안 제작 요청 추가 |
| `eidolon_get_asset_collection` | 묶음 상태와 항목별 이미지·설명·시드·오류 조회 |
| `eidolon_list_asset_collections` | 최근 제작 묶음 20개의 ID·상태·완료 수 조회 |
| `eidolon_resume_asset_collection` | 미완성 묶음의 남은 항목만 다시 요청 |
| `eidolon_regenerate_asset_frame` | 선택 항목의 설명·시드로 그 항목만 다시 요청 |
| `eidolon_export_asset_collection` | 개별 PNG·시트·좌표 메타데이터·원본 사본 내보내기 |

이미지 요청은 UI와 같은 FIFO 큐에 추가한다. 반환되는 `accepted`·`seed`는 수락 사실과 고정한 시드이며 완료 결과가 아니다. 상태를 조회하고 결과 목록에서 이미지 경로·프롬프트·시드를 확인한다. HTTP 서버를 끄거나 클라이언트 연결을 닫아도 이미 추가한 생성 요청을 지우지 않는다. 대기 요청은 UI의 기존 대기열에서 관리한다. 이미지 전송은 파일 경로 조회이며 MCP 응답에 이미지 바이트를 포함하지 않는다. 학습·설치·파일 삭제 도구는 현재 제공하지 않는다.

`model_id`를 생략하면 UI에서 선택한 모델을 사용한다. `lora_ids`를 생략하면 선택 모델과 같은 계열의 현재 선택 LoRA를 사용하며 빈 배열을 주면 해당 요청에서 LoRA를 제외한다. 다른 모델·LoRA를 지정해도 UI의 선택은 변경하지 않는다.

`positive_prompt`·`negative_prompt`를 제공하면 해당 요청의 지침만 대체한다. 빈 문자열은 해당 지침을 비운다. 생략한 지침은 저장된 설정을 사용한다. 저장 설정·프리셋·생성 및 편집 입력칸은 변경하지 않는다. LoRA 트리거 → 사용자 설명 → 요청 지침의 합성 순서는 기존 생성 서비스를 따른다.

`reference_path`는 읽을 수 있는 절대 파일 경로다. 입력 파일은 요청 추가 전에 바이트 사본으로 읽고 원본은 유지한다. 최대 입력은 기존 32 MiB·4천만 픽셀 경계를 사용하며 실제 디코딩은 기존 생성 서비스가 담당한다. 생성의 참고 강도는 기본 0.65, 편집은 0.35이며 0.05~0.95를 허용한다. 편집에서는 참고 이미지가 필수다.

## 에셋 제작 호출

제작 요청에는 `prompt`·`reference_path`가 필요하다. 스프라이트에는 `action`도 입력한다. `frame_count`는 기본 8, 2~32이며 `fps`는 기본 10, 1~60이다. `frame_descriptions`를 주면 프레임 수와 같은 개수의 포즈 설명을 전달한다. 사면도는 캐릭터·물체 구분 없이 정면·측면·후면·윗면의 4항목을 만들며 별도 시점 종류를 입력하지 않는다.

두 제작 호출은 저장된 ComfyUI 또는 Codex 생성 방식과 지침 사본을 사용한다. ComfyUI는 이미지 생성과 같은 `model_id`·`lora_ids`·`seed`를 사용하며 참고 강도 기본값은 0.45다. Codex는 체크포인트 없이 제작하며 `model_id`·`lora_ids`·`seed`·`change_strength`를 지정하면 오류를 반환한다. 배경 투명화 기본값은 true이며 Codex는 내장 이미지 생성에 알파 출력을 요청한다. `frame_size`는 내보낼 정사각형 크기(스프라이트 기본 256, 사면도 기본 1024, 16~2048), `columns`는 시트 열 수(기본 4, 1~32), `pixel_art`는 픽셀 아트 설명과 nearest 보간을 지정한다. ComfyUI 생성 해상도는 기존 모델 프리셋을 따른다. 이 호출은 참고 생성 초안이며 자연스러운 반복 동작·대상 유지·기하학적으로 정확한 사면도를 보장하지 않는다.

응답의 `accepted`·`collection_id`·`generation_backend`는 요청 접수와 생성 방식이다. ComfyUI 응답에는 `seed`, Codex 응답에는 `codex_model`을 포함한다. `eidolon_get_asset_collection`에 `collection_id`를 주어 완료를 조회한다. 조회 결과는 묶음 DTO의 `Settings`·`State`·`Frames`와 항목의 `Number`·`ImagePath`·`SourceJobId`·`Seed`(요청 시드)·`ImageSeed`(현재 이미지의 실제 시드)·`ErrorCode`·`ErrorArguments`를 포함한다. Codex의 DTO 시드 필드는 0이며 적용된 시드를 뜻하지 않는다. 묶음 조건은 요청 당시의 사본이며 나중의 설정 변경에 영향받지 않는다.

이어 만들기는 `collection_id`, 선택 재생성은 추가로 `frame_number`(1부터)·`prompt`를 받는다. 선택적 `seed`는 ComfyUI로 저장한 묶음에서만 허용한다. 현재 생성 환경과 관계없이 묶음의 저장된 생성 방식으로 처리한다. 둘 다 큐가 비었을 때 요청한다. 완료한 항목과 실패 전 이미지를 보존한다. 내보내기는 `collection_id`·`directory`(절대 폴더 경로)를 받아 새 하위 폴더를 만들고 `directory`·`sheet`·`metadata` 경로를 반환한다. 모든 항목에 이미지가 필요하다. 정렬·원본·좌표·실패 시 파일 계약은 [에셋 제작](AssetCreation.md)을 따른다.

## HTTP 메시지

JSON-RPC 메시지 하나를 UTF-8 JSON으로 POST한다. 클라이언트의 Accept는 JSON과 event-stream을 함께 허용한다. 서버는 JSON 응답을 사용한다.

최신 `2026-07-28` 클라이언트의 예:

```http
POST /mcp HTTP/1.1
Host: 127.0.0.1:8190
Content-Type: application/json
Accept: application/json, text/event-stream
MCP-Protocol-Version: 2026-07-28
Mcp-Method: tools/call
Mcp-Name: eidolon_generate_image

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "eidolon_generate_image",
    "arguments": {
      "prompt": "single golden sword, watercolor illustration",
      "positive_prompt": "centered, plain white background",
      "negative_prompt": "low quality",
      "seed": 1234,
      "lora_ids": []
    },
    "_meta": {
      "io.modelcontextprotocol/protocolVersion": "2026-07-28",
      "io.modelcontextprotocol/clientInfo": { "name": "Example", "version": "1.0.0" },
      "io.modelcontextprotocol/clientCapabilities": {}
    }
  }
}
```

기존 2025 계열 클라이언트는 `initialize` → `notifications/initialized` 뒤 `tools/list`·`tools/call`을 사용한다. 초기화 응답의 버전을 이후 `MCP-Protocol-Version`에 넣는다. 세션 ID는 발급하지 않으므로 세션 헤더나 별도 SSE 연결이 필요하지 않다. 구 HTTP+SSE나 stdio 전용 연결은 지원하지 않는다.

2025 계열의 `notifications/cancelled`는 진행 중인 HTTP 호출에만 적용한다. 여러 클라이언트를 동시에 연결할 때 호출 ID를 서로 겹치지 않게 사용한다. 취소 알림으로 이미 대기열에 추가한 이미지 요청을 취소하지는 않는다.

## Windows HTTP URL 예약

서버 시작에서 수신 권한 오류가 나면 Windows의 HTTP URL 예약이 필요하다. 관리자 PowerShell에서 실행 계정에 아래 주소만 예약한 뒤 일반 사용자로 앱을 다시 실행한다. 앱이 실행 중 자동으로 권한을 바꾸거나 관리자 재실행을 요청하지는 않는다.

```powershell
netsh http add urlacl url=http://127.0.0.1:8190/ user="$env:USERDOMAIN\$env:USERNAME"
```

URL 예약 명령의 계약은 [Microsoft netsh http 문서](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http)를 따른다.

다른 프로그램이 포트를 사용한다면 서버 시작이 실패한다. ComfyUI의 기본 8189 포트와 MCP 8190 포트는 서로 다른 용도다.

## 책임 경계

`Eidolon.Mcp`는 Dignus DI를 사용하고 App/Core 참조 없이 `HttpListener`, JSON-RPC, MCP 전송과 도구 등록을 소유한다. Dignus는 [작업 규칙](WorkingRules.md)의 외부 패키지 제한에서 허용한다. 모델은 `Models/Server`, `Models/Protocol`, `Models/Tools`로 묶으며 통신·프로토콜·도구 실행 타입은 기능 폴더에서 타입별 파일로 나눈다. 라이브러리 상세 계약은 [라이브러리 사용법](../Eidolon.Mcp/README.md)이 소유한다.

JSON-RPC 메서드는 `Eidolon.Mcp/Controllers`의 `InitializeController`, `PingController`, `ServerDiscoverController`, `ToolsListController`, `ToolsCallController`, `NotificationsCancelledController`가 각각 처리한다. 컨트롤러는 transient이며 `McpDispatcher`가 메서드별 타입을 찾아 요청·알림마다 DI에서 새로 resolve한 뒤 공통 응답 메타데이터를 붙인다. 서버 옵션·도구 목록·콜백과 App의 `McpService`는 공유한다.

App의 `Mcp/McpService.cs`는 `CreateTools()`에서 도구 정의와 private 처리 메서드를 등록하고 기존 Session·자산 선택·생성 ViewModel·JobStore에 연결한다. 호출 흐름은 `tools/call → ToolsCallController → McpService의 처리 메서드`다. UI 상태는 Avalonia UI 스레드에서 읽고 요청 사본을 만든다. `ViewModels/Mcp/McpViewModel`이 서버의 시작·정지·화면 상태를 소유하고 루트가 종료를 조합한다. Core는 MCP와 UI를 참조하지 않는다. 별도 설정 저장소, 생성 큐, 작업 이력이나 결과 인덱스를 만들지 않는다.

현재 변경은 빌드·테스트·클라이언트 접속 검증을 수행하지 않았다.

생성·편집·에셋 제작 도구는 저장된 생성 방식을 사용합니다. Codex 방식에서는 체크포인트·LoRA·시드·변경 강도 인자를 지정하면 오류를 반환하며, 응답에는 생성 방식과 Codex 실행 모델을 제공합니다. 공통 지침의 요청별 사본과 참고 이미지는 기존 Codex 생성 경계로 전달합니다.
