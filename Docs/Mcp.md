# Eidolon MCP

Eidolon 앱 초기화가 끝나면 MCP 서버가 자동으로 시작된다. MCP 클라이언트에 `http://127.0.0.1:8190/mcp`를 등록한다. 설정 왼쪽의 MCP 연결에서 서버 상태·주소를 확인하고 **서버 끄기**·**서버 켜기**로 수동 정지·재시작할 수 있다. **서버 실행 중**은 요청을 받을 수 있다는 뜻이며 AI 클라이언트의 연결 여부를 표시하지 않는다. 앱 종료 시 서버를 정리한다. 현재 앱 호스트는 같은 PC의 연결만 받고 브라우저의 다른 Origin은 거부한다.

## 호출자 모델과 유지 특징 왕복

같은 HTTP 서버와 생성·편집 도구에서 공식 MCP의 두 세대를 함께 지원한다. 이는 [공식 SDK dual-era 예제](https://github.com/modelcontextprotocol/typescript-sdk/tree/main/examples/dual-era)처럼 `2025` 초기화 경로와 `2026-07-28` 요청별 경로를 함께 처리하는 방식이다. 전송·프로토콜 세대의 선택은 `MCP.Core`가 맡고 App의 유지 특징·생성 처리 메서드는 공유한다.

`2026-07-28`은 [MRTR](https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr)을 사용한다. 추가 입력을 요구하면 `result.resultType`이 `input_required`이고 최종 응답은 `complete`다. 해당 요청의 `params._meta["io.modelcontextprotocol/clientCapabilities"].sampling`을 객체로 선언한 클라이언트에만 요청하며 초기화나 이전 요청의 선언을 재사용하지 않는다. `sampling/createMessage`는 이 세대에서 비권장이지만 현재 공식 규격에 남아 있는 기능이다.

`2025`는 [공식 Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)의 초기화·세션과 POST 응답 SSE를 사용한다. 초기화에서 sampling을 선언한 유효한 세션에만 같은 특징 요청을 보낸다. WebSocket이나 별도 커스텀 전송은 추가하지 않는다.

`eidolon_generate_image`와 `eidolon_edit_image`의 `preservation_prompt`는 호출자가 확정한 원본 유지 특징이다. 참고 이미지가 있을 때 직접 전달하면 그 값을 권위로 사용하고 추가 분석 없이 요청 사본에 고정한다. ComfyUI에는 대상 모델의 `prompt_guidance`에 맞는 간결한 영어 특징을 제공하고, 변경 요청과 충돌하는 항목은 호출자가 정리한다. 빈 문자열은 보존 지침을 명시적으로 생략한다. 원본 없는 일반 생성은 기존대로 동작하며, 원본 없이 이 필드만 전달하는 것은 거절한다.

참고 이미지가 있고 이 필드를 생략한 경우 `2026-07-28` 지원 클라이언트에는 다음 흐름을 사용한다.

1. 앱은 현재 옵션·선택 모델·LoRA·시드와 원본을 읽지만 생성 큐에는 아직 넣지 않는다.
2. `inputRequests.reference_features`에 `sampling/createMessage`를 넣어 원본 이미지와 변경 요청을 전달한다. 고정 특징 추출 지침은 UI와 같은 App 리소스를 사용한다. 다른 도구나 추가 서버 문맥은 요청하지 않으며, 사용할 모델과 샘플링 승인 여부는 클라이언트가 결정한다. 서버의 로컬 Codex나 태그 변환 모델을 대신 실행하지 않는다.
3. 호출자는 자신의 모델로 유지할 특징을 정리·확인·수정하고, 새 JSON-RPC `id`로 같은 `tools/call`을 재시도한다. 원래 `name`·`arguments`를 유지하고 `requestState`와 `inputResponses`를 **`params` 바로 아래**에 넣는다. 도구 `arguments` 안에 넣지 않는다.
4. 앱은 상태·원본·응답을 확인한 뒤에만 기존 공용 FIFO에 생성을 접수한다. `accepted=true`는 접수이며 완성 이미지나 정확히 한 번 실행을 의미하지 않는다. 수락된 요청은 재전송하지 말고 기존 상태·이미지 조회 도구로 확인한다.

클라이언트 응답은 표준 sampling 결과의 `role=assistant`, 모델 정보와 text content를 사용하며, text는 `{"features":"...","error":""}` 형태의 추출 결과다. `features`를 수정해 확정할 수 있다. 앱은 확정 특징을 다시 분석하거나 자동으로 덮어쓰지 않는다. 빈 값·거부·중단/잘림·잘못된 응답은 생성을 접수하지 않는다. 필요한 응답이 빠지면 지원 클라이언트에 같은 왕복 요청을 다시 제시하고, 관계없는 응답 키는 사용하지 않는다.

### 2026 요청 예시

초기 요청의 `params`는 다음과 같다. HTTP 헤더의 `MCP-Protocol-Version`, `Mcp-Method`, `Mcp-Name`도 기존 현대 규격 검증에 맞춰 보낸다.

```json
{
  "name": "eidolon_edit_image",
  "arguments": {
    "prompt": "Change only the hat to red",
    "reference_path": "D:/Images/character.png"
  },
  "_meta": {
    "io.modelcontextprotocol/protocolVersion": "2026-07-28",
    "io.modelcontextprotocol/clientCapabilities": { "sampling": {} }
  }
}
```

앱이 `input_required`와 opaque `requestState`를 반환하면, 새 RPC ID로 같은 요청의 `params`에 다음 두 필드를 추가한다. 매번 필요한 `_meta`와 HTTP 헤더도 유지한다.

```json
{
  "requestState": "<서버가 준 문자열을 그대로 반환>",
  "inputResponses": {
    "reference_features": {
      "role": "assistant",
      "model": "<클라이언트가 선택한 모델>",
      "content": {
        "type": "text",
        "text": "{\"features\":\"round face, short silhouette, blue coat, one staff in screen-right hand\",\"error\":\"\"}"
      },
      "stopReason": "endTurn"
    }
  }
}
```

### 2025 특징 요청

초기화에서 `capabilities.sampling`을 선언하고 `notifications/initialized`를 마친 유효 세션의 참고 생성·편집 요청도 같은 처리 메서드와 특징 입력을 사용한다. `MCP.Core`는 내부 입력 요구 결과를 해당 `tools/call`의 POST 응답 SSE에 공식 `sampling/createMessage` JSON-RPC 요청으로 보낸다. 클라이언트는 그 서버 요청 ID에 대응하는 JSON-RPC 결과나 오류를 같은 MCP 주소에 별도 POST하고 서버는 수락한 응답에 빈 HTTP 202를 반환한다. 응답은 해당 세션·인증·서버 요청 ID에만 연결하며 한 번만 수락한다.

라이브러리는 수락한 특징 응답을 원래 RPC ID와 서버 전용 내부 재진입 표식으로 같은 도구 처리 메서드에 연결한다. App이 생성 큐에 넣는 시점은 특징 응답과 재개 검증이 끝난 뒤이며 원래 도구 호출은 SSE의 최종 도구 응답으로 마친다. `2025` 클라이언트가 MRTR의 `inputResponses`·`requestState`를 직접 보내거나 `tools/call`을 재시도하는 방식은 사용하지 않는다. 호출 취소·시간 초과·서버 종료는 대기 중인 sampling 응답을 정리하며, 거부·오류·취소된 응답으로 생성 큐에 진입하지 않는다. 이미 접수한 생성의 취소는 기존 업무 취소 도구를 사용한다.

sampling을 선언하지 않았거나 해당 세대의 왕복을 지원하지 않는 클라이언트는 참고 생성·편집에 `preservation_prompt`를 직접 제공한다. 생략하면 필요한 입력을 안내하는 도구 오류를 받고 생성은 접수되지 않는다. 보존을 원하지 않으면 `preservation_prompt: ""`를 명시한다. 직접 제공하는 새 요청에는 이전 왕복의 `requestState`·`inputResponses`를 붙이지 않는다. 다면도 도구의 입력과 기존 일반 생성·학습·삭제·취소 범위는 이 기능으로 바꾸지 않는다.

### 상태와 책임 경계

`requestState`는 기존 `McpService` 인스턴스 수명의 임시 키로 무결성과 기밀성을 보호하고 5분 만료 시간을 둔다. `2026` 호출자는 내용을 해석하거나 고치지 않고 그대로 돌려주며, `2025`에서는 라이브러리의 같은 호출 내부 재진입에만 사용한다. 서버는 검증 전에 그 안의 경로·설정을 사용하지 않으며 프로토콜 세대·도구·인증 범위·2025 세션·원래 인자·RPC ID·원본 이미지 해시·실행 환경을 확인한다. `2026` 재시도에는 새 RPC ID가 필요하고, `2025` 내부 재진입은 라이브러리의 신뢰 표식과 원래 RPC ID 일치를 함께 확인한다. 원본이나 관련 설정이 바뀌었거나 앱 재시작/만료로 상태가 무효이면 처음부터 요청한다. 이미지 bytes는 상태에 넣지 않고 재진입에서 같은 원본을 다시 읽어 확인한다. App에 대기 요청 저장소·재개 세션·추가 큐·일회용 토큰 저장소를 만들지 않는다. 이 상태는 별도 HTTP 호출의 정확히 한 번 접수나 재전송 중복 제거를 제공하지 않는다.

`MCP.Core`는 요청별 도구 호출 문맥, 공식 `InputRequiredResult`의 직렬화와 `2025` 프로토콜 세션·POST SSE 왕복을 소유한다. App이 특징 판단 내용·이미지 검증·요청 옵션과 재개 상태를 소유하고, Core의 기존 `GenerationReferenceInput.PreservationPrompt`와 실제 `PositivePrompt` 합성 경로로 전달한다. 최종 접수 응답은 특징과 그 출처를 돌려주며 실제 적용 프롬프트는 완료 이미지의 기존 메타데이터에서 확인한다. UI의 직접 Codex 특징 읽기는 [원본 특징 유지](ImageEditing.md#원본-특징-유지)의 기존 흐름을 유지한다.

이번 연결은 소스 편집만 진행했으며 빌드·테스트·실제 MCP 클라이언트 sampling·취소/재시도·이미지 생성으로 확인하지 않았다. 클라이언트가 HTTP MCP를 지원한다는 이유만으로 해당 세대의 sampling 왕복도 지원한다고 판단하지 않는다.

## 도구

| 도구 | 기능 |
|---|---|
| `eidolon_get_status` | 현재 작업·취소 가능 여부, 대기 작업 제목·건수, 엔진 상태, 진행률과 오류 조회 |
| `eidolon_list_models` | 사용할 체크포인트·LoRA의 ID, 이름, 계열과 트리거 조회 |
| `eidolon_delete_model` | 명시적 승인 후 관리 로컬 체크포인트·LoRA의 실제 파일 삭제 요청 등록 |
| `eidolon_get_instructions` | 저장된 공통 생성 지침과 제외할 요소 조회 |
| `eidolon_train_lora` | 흰색·검정색 배경별 이미지·폴더 경로로 신규·기존 LoRA 추가 학습 요청 등록 |
| `eidolon_cancel_training` | 현재 실행 중인 LoRA 학습의 취소 신호를 즉시 요청 |
| `eidolon_cancel_work` | 생성·편집·학습·다면도·모델 관리 등 공용 큐의 현재 작업 취소 신호 요청 |
| `eidolon_clear_pending` | 현재 작업은 유지하고 공용 큐에서 기다리는 요청 전체 비우기 |
| `eidolon_generate_image` | 설명과 선택적 참고 이미지로 생성 요청 추가 |
| `eidolon_edit_image` | 참고 이미지와 설명으로 그림체 편집 요청 추가 |
| `eidolon_list_images` | 현재 출력 폴더의 PNG 경로와 저장된 생성 조건을 페이지로 조회 |
| `eidolon_create_views` | GPT(Codex CLI)로 정면·좌측·우측·후면 4방향 다면도 초안 제작 요청 추가 |
| `eidolon_get_asset_collection` | 묶음 상태와 항목별 이미지·설명·시드·오류 조회 |
| `eidolon_list_asset_collections` | 최근 제작 묶음 20개의 ID·상태·완료 수 조회 |
| `eidolon_resume_asset_collection` | 미완성 묶음의 남은 항목만 다시 요청 |
| `eidolon_regenerate_asset_frame` | 선택 항목의 설명·시드로 그 항목만 다시 요청 |
| `eidolon_export_asset_collection` | 개별 PNG·시트·좌표 메타데이터·원본 사본 내보내기 |

이미지·학습 요청은 UI와 같은 FIFO 큐에 추가한다. 반환되는 `accepted`·`seed`는 수락 사실과 고정한 생성 시드이며 완료 결과가 아니다. 상태를 조회하고 결과 목록에서 이미지 경로·프롬프트·시드를 확인한다. HTTP 서버를 끄거나 클라이언트 연결을 닫아도 이미 추가한 생성·학습 요청을 지우지 않는다. 대기 요청은 UI의 기존 대기열에서 관리한다. 생성 결과 목록은 파일 경로로 조회한다. 특징 판단 sampling 요청에서는 호출자의 모델이 원본을 볼 수 있도록 검증·정규화한 참고 PNG를 표준 이미지 content로 전달한다. 설치 도구는 현재 제공하지 않는다.

`model_id`를 생략하면 UI에서 선택한 모델을 사용한다. `lora_ids`를 생략하면 선택 모델과 같은 계열의 현재 선택 LoRA를 사용하며 빈 배열을 주면 해당 요청에서 LoRA를 제외한다. 다른 모델·LoRA를 지정해도 UI의 선택은 변경하지 않는다.

`positive_prompt`·`negative_prompt`를 제공하면 해당 요청의 지침만 대체한다. 빈 문자열은 해당 지침을 비운다. 생략한 지침은 저장된 설정을 사용한다. 저장 설정·프리셋·생성 및 편집 입력칸은 변경하지 않는다. LoRA 트리거 → 사용자 설명 → 요청 지침의 합성 순서는 기존 생성 서비스를 따른다.

일반·참고 생성과 그림체 편집의 `prompt`는 태그·구문·문장 또는 혼합형으로 전달할 수 있다. ComfyUI에서 로컬 변환기가 설치되어 있으면 앱이 요청에 고정된 체크포인트의 안내로 영어 형식을 정리한 뒤 같은 FIFO에서 생성한다. 입력이 태그여도 문장형 모델의 출력까지 태그로 고정하지 않으며, 문장 입력도 태그 중심 모델의 안내에 맞춰 정리한다. Codex 방식은 로컬 변환 없이 원문을 기존 Codex 이미지 생성·편집으로 바로 전달한다. UI의 변환문 확인 단계는 MCP에 추가하지 않는다. 원문은 `UserPrompt`, 실제 합성 결과는 `PositivePrompt`에 남는다. `accepted=true` 응답에는 접수한 원문·지침을 반환하며 최종 적용문은 완료 후 이미지 메타데이터에서 확인한다. ComfyUI 변환기 미설치 상태에서는 입력을 그대로 사용하므로 클라이언트가 대상 모델에 적합한 영어 프롬프트를 작성해야 한다.

요청별 `positive_prompt`·`negative_prompt`와 저장 지침·LoRA 트리거는 로컬 변환기가 다시 작성하지 않는다. 클라이언트는 요청별 지침을 대상 모델의 안내에 맞는 영어 태그·구문·문장으로 작성하며 제외 조건은 `negative_prompt`에 둔다. 다면도·선택 재생성과 학습 캡션은 기존 영어 작성 계약을 유지하며 로컬 자동 변환 대상에 포함하지 않는다. 요청에 필요한 대상·개수·포즈·관계·시점·스타일·제외 조건을 보존하고 요청하지 않은 품질·수위·출처 태그를 임의로 추가하지 않는다. 선택 입력의 생략·지침을 비우는 빈 문자열과 기존 저장 지침은 유지한다.

생성·에셋 제작 전에는 `eidolon_get_instructions`로 저장된 공통 지침을 읽는다. 일반 이미지 생성은 응답의 `generation_backend`·`selected_model_id`·`prompt_guidance`를 참고한다. ComfyUI이면 `eidolon_list_models`의 대상 체크포인트 항목에서 `engine_name`·`prompt_guidance`를 확인한다. `model_id`를 생략할 때는 응답의 `selected_model_id`에 해당하는 안내를 사용한다. 다른 체크포인트를 명시할 때는 그 항목의 안내를 사용하며 저장된 공통 지침과 충돌하는 태그는 요청별 `positive_prompt`·`negative_prompt` 사본으로 조정한다. 모델 목록 응답에도 현재 `generation_backend`를 표시한다. 신규 다면도는 GPT로 고정하므로 기본 생성 방식과 로컬 체크포인트 안내를 적용하지 않는다. 프레임 재생성은 `eidolon_get_asset_collection`의 저장된 생성 방식·체크포인트를 기준으로 하며 현재 UI 선택을 대신 사용하지 않는다.

기본 제공 체크포인트 안내는 파일명 전체와 모델 계열을 함께 비교한다. 표시 이름·파일명 일부·SDXL 호환성만으로 학습 태그를 추정하지 않는다. 파일명 일치는 가중치 해시 검증이나 임의로 이름을 바꾼 모델의 신원 확인을 뜻하지 않으며 응답에도 이 한계를 안내한다. 다른 버전·커스텀 파일은 해당 체크포인트의 모델 카드 확인을 요구하고, 확인되지 않은 품질·수위·출처 태그를 필수값으로 만들어 붙이지 않는다.

| 체크포인트 | MCP 프롬프트 안내와 근거 |
|---|---|
| Illustrious XL 0.1 | Danbooru 형식의 영어 태그를 중심으로 관계 설명에 짧은 자연어를 사용한다. 선택적 품질 태그는 `best quality`·`masterpiece`, 제외 예시는 `worst quality`·`bad quality`·`lowres`로 안내하며 구도 충돌을 피한다. `score_9`·`rating_safe`·`source_anime`를 공통 기본값으로 간주하지 않는다. [공식 모델 카드](https://huggingface.co/OnomaAIResearch/Illustrious-xl-early-release-v0) |
| RealVisXL 4.0 | 공식 카드의 실사 지향을 바탕으로 사진 요청에 대상·동작·구도·재질·조명·배경을 짧은 영어 구문으로 구체화하도록 권한다. 이는 앱의 작성 권고이며 모든 항목에 반드시 실사 스타일을 강제하지 않는다. 인물 제외 태그는 해당 요청에 맞을 때만 사용한다. [공식 모델 카드](https://huggingface.co/SG161222/RealVisXL_V4.0) |
| SDXL Base 1.0 | 공식 카드의 자연어 예시를 바탕으로 영어 설명·문장에 대상과 공간 관계를 명확히 표현하도록 안내한다. SDXL 계열이라는 이유로 Danbooru·점수·수위·출처 태그를 필수로 넣지 않는다. [공식 모델 카드](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0) |
| Stable Diffusion 1.5 base | 공식 카드의 영어 캡션·자연어 예시를 바탕으로 주요 대상·동작과 관련 구도·스타일·조명·배경을 정리하도록 권한다. 다른 애니메이션 파생 모델의 특수 태그를 그대로 가져오지 않는다. [배포 모델 카드](https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5) |

태그 순서는 읽기 쉬운 구성 권고이며 앞쪽일수록 반드시 더 높은 가중치가 적용된다고 보장하지 않는다. 캐릭터·작가·스타일은 사용자 요청에 맞는 경우에만 추가한다. LoRA 항목은 대상 체크포인트 안내를 따르며 등록된 호출 단어의 자동 적용을 유지한다. 모델 안내는 App의 AssetsViewModel.GetModelPromptGuidance가 소유하며 MCP 응답과 로컬 프롬프트 변환에서 같은 내용을 사용한다. 안내 조회 자체는 UI 입력과 저장 설정을 변경하지 않는다. 별도 태그 저장소·모델 등록부·실행 시 모델 카드 다운로드는 추가하지 않는다.

`reference_path`는 읽을 수 있는 절대 파일 경로다. 입력 파일은 요청 추가 전에 바이트 사본으로 읽고 원본은 유지한다. 최대 입력은 기존 32 MiB·4천만 픽셀 경계를 사용하며 실제 디코딩은 기존 생성 서비스가 담당한다. 생성의 참고 강도는 기본 0.65, 편집은 0.35이며 0.05~0.95를 허용한다. 편집에서는 참고 이미지가 필수다.

ComfyUI 방식의 `eidolon_edit_image`는 UI 편집과 같은 Canny 외곽선·ControlNet 조건을 자동 적용한다. 기존 `reference_path`·`change_strength`를 사용하며 새 옵션은 요구하지 않는다. 관리 로컬 엔진은 첫 편집 때 필요한 SDXL 또는 SD 1.5 Canny 가중치를 준비하고, 외부 서버에 필요한 파일·노드가 없으면 작업 오류로 알린다. `eidolon_get_status`로 준비·작업 오류를 확인하고 `eidolon_list_images`의 `metadata.OutlineControlModel`·`metadata.OutlineControlStrength`로 실제 적용 조건을 확인한다. 이 조건은 원본 포즈·구도의 유지를 돕는 편집용이며, 참고 생성과 다면도에는 적용하지 않는다. Codex 편집은 기존 내장 이미지 편집을 사용한다.

## 모델·LoRA 삭제

`eidolon_delete_model`은 등록된 관리 로컬 체크포인트 또는 LoRA의 실제 파일을 삭제한다. 복구할 수 없는 파일 삭제이므로 사용자가 해당 대상의 실제 파일 삭제를 명시적으로 승인한 경우에만 `confirm=true`로 호출한다.

필수 인자는 `eidolon_list_models`에서 조회한 실제 ID인 `asset_id`(문자열)와 `confirm`(불리언, `true`만 허용)이며 추가 속성은 허용하지 않는다. 임의 경로나 외부 서버 자산은 대상이 아니다.

현재 작업·대기 요청이 있거나 초기화 미완료·종료 중 등 기존 유지 관리 조건을 만족하지 않으면 거절한다. UI와 같은 기존 유지 관리 FIFO에서 `AssetLibrary` 삭제, 기본 모델 설정 정리와 목록 새로고침을 수행하며 화면의 선택을 다른 대상으로 바꾸지 않는다.

해당 대상의 실제 파일 삭제를 승인받은 뒤 사용하는 `tools/call`의 `params` 예시다. `<asset ID>`는 조회한 체크포인트 또는 LoRA의 실제 ID로 바꾼다.

```json
{
  "name": "eidolon_delete_model",
  "arguments": {
    "asset_id": "<asset ID>",
    "confirm": true
  }
}
```

응답의 `accepted=true`와 `asset_id`는 접수 사실이며 완료를 뜻하지 않는다. `eidolon_get_status`로 진행·오류를 확인하고, 완료 후 `eidolon_list_models`에서 대상이 제거되었는지 확인한다.

## LoRA 학습 호출

`eidolon_train_lora`는 `name`·`trigger_word`와 `white_images`·`black_images` 중 한 곳 이상의 이미지가 필요하다. 각 목록은 절대 이미지 경로 또는 폴더 경로를 받으며 폴더는 하위 폴더까지 PNG·JPG·JPEG·BMP를 가져온다. 같은 목록의 중복 파일은 제외하고 같은 파일을 두 배경 목록에 넣는 것은 허용한다. 두 목록은 한 데이터셋으로 준비해 새 LoRA 하나를 만든다. 투명·반투명 영역만 목록의 배경색으로 채우며 기존 배경과 원본 파일·설명은 보존한다. 폴더 선택 창이나 UI 조작을 요구하지 않는다.

`model_id`는 `eidolon_list_models`의 로컬 체크포인트 ID이며 생략하면 UI에서 선택한 모델을 사용한다. `steps`는 기본 1000, 허용 범위는 기존 학습 프리셋의 1~100000이다. `trigger_word`에는 공백·쉼표를 넣지 않는다. 선택적 `description`은 영어 태그로 작성한 공통 설명이며 같은 이름의 `.txt` → Eidolon 이미지 메타 정보 → 공통 설명 → 직접 붙인 파일명 순서로 사용한다. 설명 파일의 트리거 추가와 학습 데이터 준비는 기존 `LoraTrainer`가 학습 시작 시 처리한다.

선택적 `resume_lora_id`는 `eidolon_list_models`에 표시된 기존 로컬 LoRA의 ID다. 체크포인트와 같은 모델 계열·설치 환경인지 접수 전에 확인하고, 요청 사본의 기존 `TrainingInput.ResumeLora`에 연결한다. 지정하면 기존 가중치와 rank를 불러와 이번에 전달한 이미지로 추가 학습한다. 이전 학습 이미지는 자동으로 합치지 않으므로 함께 학습할 이미지는 `white_images`·`black_images`에 다시 포함한다. optimizer와 학습 스케줄은 새로 시작하며 `steps`는 이번에 추가할 단계 수다. 원본 LoRA를 보존하고 결과를 새 LoRA로 등록한다. 생략하면 UI의 이어 학습 선택과 관계없이 신규 학습을 요청한다. 접수 응답에는 지정한 `resume_lora_id`를 반환하며 완료 여부는 기존 상태·모델 조회로 확인한다.

이미지 생성 방식을 Codex로 설정했어도 LoRA 학습은 로컬 ComfyUI 학습 환경·기반 모델·NVIDIA GPU를 사용한다. 요청은 화면의 학습 입력·선택 모델·설정을 변경하지 않고 입력 사본을 기존 큐에 넣는다. 실제 파일 내용은 학습 시작 시 복사한다. 접수 전에 경로·지원 확장자·이름·트리거·모델·단계 수를 확인하며 실행 환경 확인과 학습 오류는 기존 학습 서비스가 처리한다.

예시 `tools/call`의 `params`:

```json
{
  "name": "eidolon_train_lora",
  "arguments": {
    "name": "나의 수채화",
    "trigger_word": "eidolon_watercolor",
    "steps": 1000,
    "description": "single object, centered composition, clear silhouette, plain background",
    "white_images": ["D:\\LoRA\\White"],
    "black_images": ["D:\\LoRA\\Black\\sword.png"]
  }
}
```

기존 LoRA에 추가 이미지를 학습하는 `params` 예시다. ID는 `eidolon_list_models`에서 조회한 실제 값으로 바꾼다.

```json
{
  "name": "eidolon_train_lora",
  "arguments": {
    "name": "나의 수채화 추가 학습",
    "trigger_word": "eidolon_watercolor",
    "model_id": "<checkpoint ID>",
    "resume_lora_id": "<existing LoRA ID>",
    "steps": 300,
    "white_images": ["D:\\LoRA\\Additional"]
  }
}
```

응답은 `accepted`·`name`·`trigger_word`·`model_id`·`steps`·`image_count`이며 완료 결과가 아니다. `eidolon_get_status`에서 현재 작업·대기열·진행률·오류를 확인하고 완료 뒤 `eidolon_list_models`에서 새 LoRA와 트리거를 조회한다. 학습 상태·남은 시간·취소는 기존 학습 화면에서도 사용한다. 엔진 정지·학습·등록·복구는 기존 `StudioService.TrainAsync`가 소유한다.

## 작업 취소

MCP 취소는 HTTP 호출, 접수 후 실행 중인 업무, 아직 대기 중인 업무를 구분한다. 완료한 작업과 이미 끝난 파일 쓰기·삭제를 되돌리지는 않는다.

| 대상 | 취소 경로 |
|---|---|
| 응답을 기다리는 조회·입력 준비·내보내기 등 MCP HTTP 호출 | JSON-RPC 요청 ID를 지정한 `notifications/cancelled` |
| 접수 후 실행 중인 생성·참고 생성·편집·프롬프트 변환 | `eidolon_cancel_work` |
| 접수 후 실행 중인 신규·이어 학습 | `eidolon_cancel_work` 또는 `eidolon_cancel_training` |
| 다면도 생성·이어 만들기·선택 재생성 | `eidolon_cancel_work` |
| 접수된 모델 삭제 등 큐의 유지 관리 작업 | 실행 전에는 `eidolon_clear_pending`, 실행 중에는 `eidolon_cancel_work`. 이미 완료한 삭제는 복원하지 않음 |
| 공용 큐에서 기다리는 요청 전체 | `eidolon_clear_pending` |

### 실행 중인 업무 취소

1. `eidolon_get_status`에서 `can_cancel_work=true`와 `active_request`를 확인한다.
2. `active_request` 문자열 전체를 그대로 `eidolon_cancel_work`에 전달한다. 편집 가능한 화면 입력이나 파일명으로 대신 구성하지 않는다.
3. 응답의 `accepted=true`·`cancellation_requested=true`는 취소 신호 접수다. 프로세스 종료·파일 처리·엔진 복구 완료를 뜻하지 않으므로 상태·오류를 계속 조회한다.

```json
{
  "name": "eidolon_cancel_work",
  "arguments": {
    "active_request": "<가장 최근 eidolon_get_status 응답의 active_request 전체>"
  }
}
```

취소 도구는 FIFO 뒤에 추가하지 않고 UI 스레드에서 바로 기존 큐 취소를 호출한다. 큐 소유자도 같은 제목의 작업인지 다시 확인하며, 작업이 바뀌거나 유휴 상태이면 거절한다. `active_request`는 제목이며 고유 작업 ID가 아니므로 같은 제목의 연속 작업을 구분하는 식별자로 사용하지 않는다. UI와 다른 MCP 클라이언트도 같은 업무 큐를 사용한다.

학습은 기존 `TrainingViewModel.CancelTrainingAsync`를 거쳐 취소 중 표시·프로세스 정리·엔진 복구를 유지하고 완성 LoRA 등록 중에는 취소를 거절한다. `eidolon_cancel_training`은 같은 `active_request` 인자로 학습만 취소하는 도구로 유지하며 `can_cancel_training=true`일 때 사용한다. 일반 생성·편집·다면도는 기존 토큰과 개별 엔진 작업 취소를 사용하며 외부 서버의 전체 작업을 중단하지 않는다. 완료 이미지·기존 LoRA·원본 학습 자료는 취소를 이유로 삭제하지 않는다.

### 대기 업무 비우기

`eidolon_get_status.pending_requests`에서 제목 목록을 확인한 뒤 다음 도구를 호출한다.

```json
{
  "name": "eidolon_clear_pending",
  "arguments": {}
}
```

현재 대기 중인 요청 전체를 기존 `ClearPending` 경로로 비우며 UI·다른 MCP 클라이언트의 대기 요청도 포함한다. 실행 중인 작업·완료 파일·연결 설정의 별도 적용 예약은 유지한다. 준비된 다면도 묶음은 기존 폐기 콜백으로 취소 상태를 남기고, 시작 전 유지 관리 요청을 비웠을 때도 입력 제한을 해제한다. 응답은 `accepted`와 남은 `pending_count`다.

현재 작업과 대기 작업을 모두 멈추려면 대기를 먼저 비우고, 상태를 다시 읽은 뒤 현재 작업을 취소한다. 현재 작업만 취소하면 정리 뒤 다음 대기 작업이 실행될 수 있다.

### 진행 중인 HTTP 호출 취소

응답이 아직 끝나지 않은 조회·내보내기 등의 호출에는 업무 제목 대신 해당 JSON-RPC 호출 ID를 사용한다. 다음 예시는 ID가 `42`인 호출을 취소한다. HTTP 요청 헤더와 버전 메타데이터는 원래 호출과 같은 프로토콜을 따른다.

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/cancelled",
  "params": {
    "requestId": 42
  }
}
```

2025 호환 경로와 `2026-07-28` 경로 모두 요청 ID를 취소에 연결한다. 알림은 요청 액터 뒤에서 기다리지 않고 처리하며, 완료된 호출·`initialize`·이미 업무 큐에 접수된 작업에는 영향을 주지 않는다. 동시 클라이언트는 진행 중인 요청 ID를 서로 겹치지 않게 사용한다. 호출 취소 토큰은 실제 처리 메서드로 전달하지만 이미 완료한 파일이나 내보내기 중 작성한 일부 파일의 롤백을 보장하지 않는다.

이 취소 기능 변경은 빌드·테스트·앱 재실행·실제 작업 취소 호출로 검증하지 않았다. 실행 중인 이전 버전에는 소스 변경만으로 새 도구가 추가되지 않는다.

## 에셋 제작 호출

`eidolon_create_views`에는 `reference_path`만 필수이며 `prompt`는 선택 입력이다. 대상 설명이 없으면 기준 이미지의 같은 대상을 요청한다. 캐릭터·물체 구분 없이 정면·좌측·우측·후면 순서로 4항목을 만들며 방향 종류나 장수를 입력하지 않는다. 우측·좌측은 대상 자신의 면을 뜻하며 각각의 실제 방향을 요청한다. 스프라이트 생성 도구와 `action`·`frame_count`·`fps`·`frame_descriptions` 인자는 제공하지 않는다.

새 다면도는 저장된 이미지 생성 방식과 관계없이 GPT(Codex CLI의 기존 이미지 생성 경로)를 사용한다. 저장한 Codex 실행 경로·모델과 공통 지침을 사용하며, 필요하면 요청의 `positive_prompt`·`negative_prompt`로 지침 사본을 바꿀 수 있다. `model_id`·`lora_ids`·`seed`·`change_strength`는 스키마에 없으며 지정하면 접수 전에 인자 오류를 반환한다. 배경 투명화 기본값은 true이며 내장 이미지 생성에 알파 출력을 요청한다. `frame_size`는 내보낼 정사각형 크기(기본 1024, 16~2048), `columns`는 시트 열 수(기본 5, 1~32), `pixel_art`는 픽셀 아트 설명과 nearest 보간을 지정한다. 이 호출은 참고 생성 초안이며 대상 유지·기하학적으로 정확한 다면도를 보장하지 않는다.

응답에는 `accepted`·`collection_id`·`generation_backend`(`Codex`)·`codex_model`이 포함되며 완료를 뜻하지 않는다. `eidolon_get_asset_collection`에 `collection_id`를 주어 완료를 조회한다. 조회 결과는 묶음 DTO의 `Settings`·`State`·`Frames`와 항목의 `Number`·`ImagePath`·`SourceJobId`·`Seed`(요청 시드)·`ImageSeed`(현재 이미지의 실제 시드)·`ErrorCode`·`ErrorArguments`를 포함한다. Codex의 DTO 시드 필드는 0이며 적용된 시드를 뜻하지 않는다. 이전 ComfyUI 묶음의 저장 시드는 그대로 조회할 수 있다. 묶음 조건은 요청 당시의 사본이며 나중의 설정 변경에 영향받지 않는다.

이어 만들기는 `collection_id`, 선택 재생성은 추가로 `frame_number`(1부터)·`prompt`를 받는다. 기존 묶음은 저장된 `frame_number`와 방향을 유지한다. 선택적 `seed`는 ComfyUI로 저장한 묶음에서만 허용한다. 현재 생성 환경과 관계없이 묶음의 저장된 생성 방식으로 처리한다. 둘 다 큐가 비었을 때 요청한다. 완료한 항목과 실패 전 이미지를 보존한다. 내보내기는 `collection_id`·`directory`(절대 폴더 경로)를 받아 새 하위 폴더를 만들고 `directory`·`sheet`·`metadata` 경로를 반환한다. 모든 항목에 이미지가 필요하다. 정렬·원본·좌표·실패 시 파일 계약은 [에셋 제작](AssetCreation.md)을 따른다.

## HTTP 메시지

JSON-RPC 메시지 하나를 UTF-8 JSON으로 POST한다. 클라이언트의 Accept는 JSON과 event-stream을 함께 허용한다. `2026-07-28`은 JSON 응답으로 MRTR을 처리하며, `2025`의 sampling 왕복은 원래 POST 응답을 SSE로 연다. sampling 응답도 같은 MCP 주소에 새 HTTP POST로 보낸다.

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
      "prompt": "single golden sword, straight double-edged blade, engraved gold crossguard, blue gemstone in the hilt, entire sword visible, centered composition, front view, watercolor illustration, textured brushwork, warm gold tones, soft diffuse lighting, plain white background",
      "positive_prompt": "isolated subject, balanced proportions, crisp silhouette, detailed metal texture, uncluttered composition",
      "negative_prompt": "multiple swords, cropped blade, deformed hilt, blurry details, text, watermark, cluttered background",
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

기존 `2025` 계열 클라이언트는 `initialize` → `notifications/initialized` 뒤 `tools/list`·`tools/call`을 사용한다. 초기화 응답의 버전을 이후 `MCP-Protocol-Version`에 넣고, 발급받은 `Mcp-Session-Id`를 이후 모든 요청·알림·sampling 응답 POST에 전달한다. 프로토콜 세션은 발급 시점부터 30분 유효하며 인증 범위·협상 버전·초기화 상태에 연결된다. 세션이 만료되거나 서버가 재시작했으면 새 `initialize`로 시작한다.

`2026-07-28`은 초기화·세션 없이 요청별 `_meta`로 처리하고 `2025` 세션의 기능 선언을 가져오지 않는다. `GET`·`DELETE`에는 HTTP 405와 `Allow: POST`를 반환한다. 서버의 SSE는 `2025` 원래 POST 응답에서만 열며 독립 GET 스트림·재연결·이벤트 재전송은 제공하지 않는다. 구 `2024-11-05` HTTP+SSE 전송이나 stdio 전용 연결은 지원하지 않는다.

`notifications/cancelled`는 2025 계열과 `2026-07-28` 경로에서 진행 중인 HTTP 호출에만 적용한다. 여러 클라이언트를 동시에 연결할 때 호출 ID를 서로 겹치지 않게 사용한다. 취소 알림으로 이미 대기열에 추가한 이미지·학습 요청을 취소하지는 않는다. 접수 후 실행 중인 업무는 `eidolon_cancel_work`, 대기 업무는 `eidolon_clear_pending`을 사용한다.

## Windows HTTP URL 예약

서버 시작에서 수신 권한 오류가 나면 Windows의 HTTP URL 예약이 필요하다. 관리자 PowerShell에서 실행 계정에 아래 주소만 예약한 뒤 일반 사용자로 앱을 다시 실행한다. 앱이 실행 중 자동으로 권한을 바꾸거나 관리자 재실행을 요청하지는 않는다.

```powershell
netsh http add urlacl url=http://127.0.0.1:8190/ user="$env:USERDOMAIN\$env:USERNAME"
```

URL 예약 명령의 계약은 [Microsoft netsh http 문서](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http)를 따른다.

다른 프로그램이 포트를 사용한다면 서버 시작이 실패한다. ComfyUI의 기본 8189 포트와 MCP 8190 포트는 서로 다른 용도다.

## 책임 경계

`MCP.Core`는 Dignus DI를 사용하고 App/Core 참조 없이 `HttpListener`, JSON-RPC, MCP 전송과 도구 등록을 소유한다. Dignus는 [작업 규칙](WorkingRules.md)의 외부 패키지 제한에서 허용한다. 모델은 `Models/Server`, `Models/Protocol`, `Models/Tools`로 묶으며 통신·프로토콜·도구 실행 타입은 기능 폴더에서 타입별 파일로 나눈다. 라이브러리 상세 계약은 [라이브러리 사용법](../Mcp.Core/README.md)이 소유한다.

독립 MCP.Core 솔루션은 https://github.com/EomTaeWook/Mcp.Core.git 저장소를 Mcp.Core 서브모듈로 연결하고 Mcp.Core/MCP.Core.csproj를 참조한다. 프로젝트 설정·사용법·MIT 라이선스도 독립 저장소가 소유한다. 서버 이름은 호스트가 지정하며 Eidolon App의 `McpViewModel`이 `Name="Eidolon"`을 전달한다. 앱 도구·설정·번역·큐는 이 라이브러리에 포함하지 않는다.

JSON-RPC 메서드는 `Mcp.Core/Controllers`의 `InitializeController`, `PingController`, `ServerDiscoverController`, `ToolsListController`, `ToolsCallController`, `NotificationsCancelledController`가 각각 처리한다. 컨트롤러는 `McpControllerBase`를 상속하고 `[Controller("initialize")]` 같은 특성의 이름으로 검색한다. `MethodName` 상수와 수동 등록 목록은 사용하지 않는다. `McpDispatcher`가 발견한 타입을 항상 `LifeScope.Transient`로 등록하고 요청·알림마다 새 인스턴스를 resolve한 뒤 공통 응답 메타데이터를 붙인다. 서버 옵션·도구 목록·콜백과 App의 `McpService`는 공유한다.

App의 `Mcp/McpService.cs`는 `CreateTools()`에서 도구 정의와 private 처리 메서드를 등록하고 기존 Session·자산 선택·생성 및 학습 ViewModel·JobStore에 연결한다. 학습 요청은 `TrainingViewModel.EnqueueTraining`을 통해 UI와 같은 `TrainAsync` 실행 경로에 연결하며 별도 폴더 선택·학습 큐·데이터셋 저장소를 만들지 않는다. 호출 흐름은 `tools/call → ToolsCallController → McpService의 처리 메서드`다. UI 상태는 Avalonia UI 스레드에서 읽고 요청 사본을 만든다. `ViewModels/Mcp/McpViewModel`이 서버의 시작·정지·화면 상태를 소유한다. 루트는 초기화 완료 후 기존 `StartCommand`를 실행하고 종료를 조합한다. 초기화 실패·종료 중·이미 실행 중이면 기존 명령 조건으로 시작하지 않으며 시작 오류는 기존 명령 오류 처리로 표시한다. Core는 MCP와 UI를 참조하지 않는다. 별도 설정 저장소, 생성 큐, 작업 이력이나 결과 인덱스를 만들지 않는다.

현재 변경은 빌드·테스트·클라이언트 접속 검증을 수행하지 않았다.

생성·편집·에셋 제작 도구는 저장된 생성 방식을 사용합니다. Codex 방식에서는 체크포인트·LoRA·시드·변경 강도 인자를 지정하면 오류를 반환하며, 응답에는 생성 방식과 Codex 실행 모델을 제공합니다. 공통 지침의 요청별 사본과 참고 이미지는 기존 Codex 생성 경계로 전달합니다.

## 3D 모델링 도구

### `eidolon_model_3d`

모델링 UI의 `ModelingInput`과 같은 실행 연결로 공용 FIFO에 요청한다. UI 입력·선택이나 저장 설정을 임시 변경하지 않는다. 저장된 네이티브 GPT(Codex CLI) 실행 경로·모델과 Blender를 확인하며, Claude·WSL에서 모델링을 시작하지 않는다. 초기화·종료·유지 관리·예약 작업 때문에 접수할 수 없거나 설치·로그인을 확인하지 못하면 `accepted=true`를 반환하지 않는다.

| 인자 | 계약 |
| --- | --- |
| `instructions` | 최대 16,000자. 원하는 형상·재질만 설명한다. Blender 파일 저장·GLB 내보내기·미리보기 렌더는 Eidolon이 자동 수행하므로 지시문에서 제외한다. 새 모델은 이미지가 있으면 생략 가능. `.blend` 수정은 공백이 아닌 설명 필수 |
| `reference_path` | 단일 이미지의 절대 로컬 경로. `views`와 동시 지정 불가 |
| `views` | `front`, `left`, `right`, `back` 중 한 방향 이상의 이미지 경로 객체. 지정한 방향만 사용 |
| `source_blend_path` | 선택적인 기존 `.blend`의 절대 로컬 경로. 이 원본을 덮어쓰지 않고 새 GUID 작업으로 수정 |
| `maximum_triangle_count` | 정수 `0`(자동) 또는 `1`~`1,000,000`, 기본 `0` |

이미지는 기존 PNG·JPG·JPEG·WebP 파일이며 32MB·4천만 픽셀 제한과 App의 기존 디코딩 검사를 적용한다. Windows의 UNC·디바이스 경로는 슬래시 표기를 정규화한 뒤 접두사로 거절하며, 파일 존재 확인·읽기·디코딩 전에 검사한다. 참조 경로는 접수 시 고정하고 실제 바이트는 큐 실행 시 기존 엔진이 복사하므로 시작 전 해당 파일을 유지해야 한다. 밝은 배경 합성이나 방향 반전은 하지 않는다. 실행 파일, 실행 모델 덮어쓰기, 에이전트, 작업·출력 폴더, 스크립트, 임의 Python 및 그 밖의 인자는 허용하지 않는다. Blender 창은 기본 꺼짐이다.

```json
{
  "instructions": "Keep the shape and shorten the handle slightly",
  "views": {
    "front": "D:/References/front.png",
    "left": "D:/References/left.png",
    "right": "D:/References/right.png",
    "back": "D:/References/back.png"
  },
  "maximum_triangle_count": 20000
}
```

응답은 `accepted=true`, `request_id`, `result_tool`, `status_tool`이다. `request_id`는 기존 관리 작업 폴더의 32자리 GUID이며 완료 ID나 별도 저장소를 새로 만들지 않는다. 공용 작업 제목은 `3D [<request_id>]`다. 접수 뒤 모델 호출·렌더가 실패하거나 취소될 수 있으므로 같은 요청을 재전송하지 말고 조회한다.

### `eidolon_get_modeling_result`

인자는 `request_id` GUID 하나뿐이다. 하이픈 있는 GUID도 관리 폴더 이름인 32자리 형식으로 정규화하며 임의 경로는 받지 않는다. 결과는 `request_id`, `state`, `completed`이며 완료일 때만 `blend_path`, `glb_path`, `triangle_count`가 추가된다. 실제 작업을 완료한 기존 `Control/Draft.json`의 오류 없는 상태·삼각형 수와 비어 있지 않은 `Draft/Model.blend`·`Draft/Model.glb`를 확인한다. 삼각형 수는 키트가 실제 GLB를 내보내며 계산한 값이다. 로그·오류 본문·스크립트·임의 파일 내용은 반환하지 않고 링크를 경유한 관리 결과도 거절한다.

| `state` | 확인 근거 |
| --- | --- |
| `queued` | 해당 GUID의 공용 대기 제목이 현재 큐에 있음 |
| `running` | 해당 공용 현재 제목 또는 현재 `ModelingInput`의 GUID와 일치 |
| `cancelled` | 최근 모델링 요청의 GUID와 실제 취소 처리 상태가 일치 |
| `failed` | 최근 실패 상태 또는 관리 요청의 기존 `Failure.txt`/실패 완료 표식이 있음 |
| `completed` | 완료 표식과 `.blend`·`.glb` 및 실제 삼각형 수가 모두 확인됨 |
| `unknown` | 폴더는 있으나 상태·완료 증거가 불충분하거나 옛 `Refined` 결과의 최종 선택을 복원할 수 없음 |
| `not_found` | 현재 큐·최근 실행 상태·관리 폴더에서 요청을 찾지 못함. 오류 응답의 JSON 텍스트로 제공 |

`completed=false`는 완료가 아니다. 대기·실행·취소·실패 응답은 `status_tool`, `cancel_tool`, `clear_pending_tool`로 기존 도구를 안내한다. `eidolon_get_status`의 정확한 `active_request`를 `eidolon_cancel_work`에 주고, 대기 요청 전체를 버릴 때는 기존 `eidolon_clear_pending`을 사용한다. 시작 전 비운 요청, 과거 취소, 앱 재시작 이후의 비영속 상태는 남은 증거 없이 취소나 실패로 단정하지 않는다. 취소 상태와 부분 산출물은 별개이며, 새 결과 이력 저장소나 영속 취소 표식은 추가하지 않는다.

이번 연결은 App `McpService`·`ModelingViewModel`과 기존 루트 주입에 한정한다. MCP.Core 전송·서브모듈, 모델링 엔진·Python·생성 UI·다국어 원본은 변경하지 않았다. 도구 등록·실행·렌더·실제 취소·빌드·테스트 검증은 수행하지 않았다. 모델링 실행과 원본 보호는 [3D 모델링](Modeling.md#모델링-mcp)을 따른다.
