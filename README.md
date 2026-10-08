# Eidolon

Avalonia로 만든 프롬프트 중심 데스크톱 이미지 생성·편집·LoRA 학습 프로그램입니다. 생성 환경에서 ComfyUI 또는 설치된 Codex를 선택합니다. ComfyUI의 기본 모델은 SDXL Base 1.0이며 기본 생성 크기는 1024×1024입니다. Codex는 ComfyUI 설치 없이 기존 Codex 로그인으로 내장 이미지 생성을 사용합니다.

## 실행

.NET 10 SDK가 있는 Windows에서 다음 명령으로 실행합니다.

```powershell
dotnet run --project Eidolon.App
```

1. **생성 환경 → 생성 방식·지침**에서 ComfyUI 또는 Codex를 선택하고 공통 지침·제외할 요소·프리셋을 설정합니다.
2. Codex는 설치된 Codex에서 먼저 로그인합니다. 실행 모델은 자동으로 가져온 목록에서 선택하고 **생성 환경 저장**을 누릅니다. 기본 모델 항목은 Codex 기본값을 사용합니다. 실행 파일을 찾지 못하면 `codex.exe`를 선택하고 목록을 새로고침합니다.
3. ComfyUI는 **생성 환경 → 엔진 설치**에서 실행 환경을 준비합니다. NVIDIA GPU가 없으면 CPU 옵션으로 이미지 생성만 지원합니다. **모델·LoRA**에서 모델을 설치하거나 **모델 폴더**·**LoRA 폴더**를 열어 `.safetensors`를 넣고 새로고침합니다.
4. **생성 환경 저장**으로 방식과 공통 지침을 적용합니다. **설정**에서는 이미지 저장 폴더·언어·테마를 저장합니다. 두 저장은 각각의 입력만 적용하며 테마·언어는 라디오 버튼으로 선택합니다.
5. **이미지 생성**에서 설명과 선택적 참고 이미지를 입력해 생성 요청을 추가합니다. **이미지 편집**은 원본 이미지와 수정 설명을 입력합니다. 결과는 새 이미지로 저장하고 원본을 보존합니다.

생성·편집·학습 요청은 추가한 순서대로 하나씩 실행합니다. 실행 중에도 다음 요청을 작성하고 추가할 수 있습니다. 하단은 상태·진행률·예상 남은 시간·대기 건수만 표시하며 버튼을 두지 않습니다. 학습 취소는 학습 실행 영역에서 제공하고, 각 요청 화면의 대기열 비우기는 아직 시작하지 않은 요청을 제거합니다. 앱 종료 시 현재 작업과 대기 요청을 정리하며 대기열은 재실행 시 복원하지 않습니다. 저장한 변경은 이후 요청에 적용하고 대기 요청은 추가할 때의 설정을 유지합니다.

설치에는 인터넷 연결이 필요하고 Python 라이브러리와 모델을 여러 GB 다운로드합니다. 기본 SDXL 모델은 약 6.94 GB입니다. 중단한 다운로드는 다시 설치할 때 이어받습니다. GPU 설치에는 CUDA 12.8을 지원하는 NVIDIA 드라이버가 필요하며 별도 CUDA Toolkit 설치를 요구하지 않습니다. CPU·GPU 설치 옵션을 바꾼 경우 설치·복구를 다시 실행합니다.

**생성 환경 → 엔진 설치**에서 ComfyUI 서버 주소를 입력합니다. 기본값은 `http://127.0.0.1:8189`이며 다른 프로그램의 로컬 서버나 원격 서버도 연결합니다. 서버가 없는 로컬 주소에서는 설치된 엔진을 실행합니다. 작업 중인 주소 변경은 대기열이 끝난 뒤 적용합니다. 앱이 시작한 서버는 앱 종료·비정상 종료 시 종료하고 기존 서버는 유지합니다.

Codex 실행 모델은 이미지 요청을 처리하는 모델이며 내장 이미지 생성 모델과 구분합니다. 목록은 설치된 CLI의 카탈로그를 읽고 별도 파일로 저장하지 않습니다. 카탈로그에 표시되는 것이 계정의 추론 권한을 보장하지는 않습니다. Codex 생성·편집에는 로컬 체크포인트·LoRA·시드·변경 강도를 적용하지 않습니다. 두 방식 모두 생성 환경의 공통 지침과 제외할 요소를 사용합니다. **배경 투명하게**는 ComfyUI에서 배경 제거를, Codex에서 투명 PNG 생성을 요청합니다. [OpenAI Docs의 모델 카탈로그 조회](https://learn.chatgpt.com/docs/developer-commands#codex-debug-models), [이미지 생성 안내](https://learn.chatgpt.com/docs/image-generation).

## 기능

- ComfyUI 또는 Codex 이미지 생성과 참고 이미지·원본 편집
- 생성 환경의 기본 생성 지침·제외할 요소 적용
- 최종 PNG 갤러리, 적용 프롬프트 조회·재사용과 이미지 내보내기
- SD 1.5·SDXL 체크포인트와 LoRA의 safetensors 폴더 조회·계열 확인·트리거 관리
- 선택한 LoRA를 생성에 적용하고 트리거를 자동 추가
- 사용자 이미지 폴더로 LoRA 학습, 기존 LoRA 이어 학습, 완료 가중치 자동 등록
- 앱 시작 시 ComfyUI 실행, 종료 시 관리 서버 종료, 주소를 입력해 외부 서버 사용
- 학습 취소·남은 시간, 원본 학습 데이터 보존, 설정·작업 JSON 백업
- 생성·편집·학습 공용 FIFO 대기열, 실행 중 다음 요청 추가와 대기 요청 비우기
- 설정에서 라이트·블랙 테마와 한국어·영어 선택, 즉시 적용과 저장
- 브랜드와 같은 줄의 아이콘 탐색, 사용법 탭, 하단 작업 상태·진행률
- DignusLog 파일 로그와 종료 시 로그 정리

모델·LoRA 관리 화면은 보유 목록, 선택 정보·삭제, 새 모델 설치를 세로로 표시합니다. 엔진 준비 후 모델 폴더의 `.safetensors`를 자동으로 읽으며 새로고침으로 다시 조회합니다. 보유 항목을 누르면 목록 아래에서 계열·LoRA 트리거를 수정하고 저장하거나 **선택 항목 삭제**를 실행합니다. 삭제는 확인한 설치 파일과 자산 등록을 제거하며 실행·대기 작업이 없을 때만 가능합니다. 외부 서버 자산은 삭제하지 않습니다. 기본 모델을 지정하지 않았으면 SDXL을 먼저 선택하고 같은 계열의 LoRA를 생성 화면에 표시합니다.

LoRA 학습은 이미지 선택 → 이름·트리거 → 학습 설정의 세 단계로 진행합니다. 흰색·검정색 목록을 함께 학습해 LoRA 하나를 만듭니다. 첫 이미지로 설명을 자동 채우고 이름·트리거는 기본값을 제공하며 직접 수정할 수 있습니다. 설명은 같은 이름의 `.txt` → 이미지 옆 프롬프트 JSON → 공통 설명 → 직접 붙인 파일명 순서로 사용하고 자동 시각·GUID 이름은 제외합니다. 학습 시 선택한 이미지·배경·설명과 설정을 고정하고 `<엔진 설치 경로>/EidolonRuntime/TrainingData/<작업 ID>`에 복사본을 자동 준비합니다. 원본과 이미 있는 배경은 보존합니다. **엔진 설치 → 학습 데이터**에서 폴더를 열거나 준비 데이터를 비울 수 있으며 완료된 LoRA와 Job 로그는 보존합니다. 이어 학습은 기존 가중치·rank를 불러오고 optimizer·스케줄은 새로 시작하며 결과를 새 파일로 등록합니다.

ComfyUI 노드 그래프와 생성 파라미터는 제품 내부에서 구성합니다. 학습 프리셋은 입문용 고정값이며 데이터별 품질은 별도 조정·평가가 필요합니다. 현재 SD3·FLUX·inpainting 전용·refiner 전용 모델은 지원하지 않습니다.

## 스프라이트·삼면도 제작

**에셋 제작**에서 기준 이미지와 설명을 입력해 스프라이트 프레임 또는 삼면도 초안을 만듭니다. 스프라이트는 반복 재생·순서 변경, 캐릭터와 물체는 시점별 비교를 제공합니다. 항목별 재생성·이미지 교체·미완성 이어 만들기를 지원하고 개별 PNG·시트·좌표·기준점·FPS JSON과 원본 사본을 내보냅니다. 현재 참고 생성 기반 초안이므로 동작·외형·시점의 일관성은 직접 검토해야 합니다. 자세한 계약은 [에셋 제작](Docs/AssetCreation.md)을 참고하세요.

## MCP 연결

**설정 → 일반 → MCP 연결**에서 서버를 켜고 `http://127.0.0.1:8190/mcp`를 MCP 클라이언트에 등록합니다. 이미지 생성·그림체 편집·모델 및 결과 조회를 제공하며 생성 지침은 요청별로 지정할 수 있습니다. 요청은 UI와 같은 대기열을 사용하고 저장 설정은 유지합니다. 지원 도구, 호출 예시와 Windows 수신 권한 안내는 [MCP 사용법](Docs/Mcp.md)에 있습니다.

## 구조와 저장 위치

| 위치 | 내용 |
|---|---|
| `Eidolon.Core` | Domain 계약, Application 유스케이스, Infrastructure의 설치·ComfyUI·Codex·학습·저장 구현 |
| `Eidolon.App` | Avalonia 화면, ViewModel·Presenter, 파일 대화상자, 다국어·템플릿 로딩, DI와 앱 수명 |
| `Eidolon.Mcp` | .NET 기본 라이브러리만 사용하는 HttpListener MCP 서버와 메서드별 컨트롤러 |
| `DataContainer` | Excel 변환기가 생성하는 템플릿·컨테이너·로더 |
| `<선택한 폴더>\EidolonRuntime` | 실행 도구·Python·엔진·모델·다운로드 캐시·설치 로그 |
| `%LOCALAPPDATA%\Eidolon` | 사용자 설정, 자산 목록, 생성·학습 작업과 결과 |
| `<설치 경로>/EidolonRuntime/TrainingData/<작업 ID>` | 자동 준비한 학습 이미지·설명. 엔진 설치에서 열기·비우기 |
| 설정의 이미지 저장 폴더 | 최종 PNG와 같은 이름의 프롬프트 JSON. 기본은 `%LOCALAPPDATA%\Eidolon\Images` |
| `%LOCALAPPDATA%\Eidolon\Jobs\<작업 ID>\Originals` | 생성 원본·Codex에서 복사한 원본 PNG |
| Codex 홈의 `generated_images\<세션 ID>` | Codex 내장 도구가 처음 생성한 PNG |

파일 구성과 Core/App 경계는 [작업 규칙](Docs/WorkingRules.md)을 따릅니다. 적용 규칙과 책임 경계는 [EidolonArchitecture](Docs/EidolonArchitecture.md)에 정리했습니다. 다국어 문구는 [Excel/String.xlsx](Excel/String.xlsx)에서 관리하며, 변환 방법은 [문자열 데이터 변환](Docs/StringData.md)을 참고하세요.

설정 파일이 손상되면 앱은 덮어쓰지 않고 오류를 표시합니다. 사용자 데이터 폴더의 해당 `.bak` 파일을 확인해 복원할 수 있습니다. 비정상 종료한 작업은 다음 실행에 중단 상태로 표시됩니다. 앱 로그는 `%LOCALAPPDATA%\Eidolon\Logs\Eidolon.log`, 설치 실패 로그는 `EidolonRuntime\Logs\Install.log`, 생성 엔진 로그는 같은 폴더의 `Engine.log`, 학습 로그는 선택한 작업 폴더의 `Training.log`에 있습니다. 설정과 사용법에서 앱 로그 폴더를 열 수 있습니다.

Codex 홈의 기본값은 `%USERPROFILE%\.codex`입니다. Codex 원본은 그대로 두고 앱이 Job 폴더에 복사한 뒤 최종 결과를 설정의 이미지 폴더에 게시합니다. 생성 결과와 설정에서 이미지 폴더를 열 수 있으며 Codex 요청·결과·실행 로그는 `Jobs\<작업 ID>\Originals\Codex`에, 모델 목록 조회 로그는 사용자 데이터의 `Logs\CodexModels.log`에 남습니다.

## 개발과 배포

현재 사용자 지시에 따라 빌드·테스트·검증을 실행하지 않습니다. 아래 명령은 추후 요청 시 사용할 개발·배포 절차입니다.

```powershell
dotnet build Eidolon.slnx -m:1 /p:UseSharedCompilation=false
```

Windows x64 자체 포함 단일 EXE의 배포 프로필:

```powershell
dotnet publish Eidolon.App -p:PublishProfile=WindowsX64 -o artifacts\win-x64
```

앱의 런타임과 UI 라이브러리는 EXE에 포함합니다. 이미지 생성·학습용 Python과 모델은 사용자가 선택한 경로에 설치합니다. 앱의 기본 배포 프로필과 개발 빌드 설정은 분리되어 있습니다.

## 외부 구성요소

UI는 [Avalonia](https://github.com/AvaloniaUI/Avalonia) 12.1.3, 생성은 [ComfyUI](https://github.com/Comfy-Org/ComfyUI/releases/tag/v0.38.0), 학습은 [sd-scripts](https://github.com/kohya-ss/sd-scripts/releases/tag/v0.12.0)를 사용합니다. 자동 설치는 [uv](https://github.com/astral-sh/uv/releases/tag/0.12.22) 0.12.22, Python 3.11.17, PyTorch 2.10.0·torchvision 0.25.0의 CPU 또는 CUDA 12.8 패키지를 사용합니다. uv 실행 파일과 기본 모델 다운로드는 공개 SHA-256으로 확인합니다.

기본 모델은 [Stability AI의 SDXL Base 1.0](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0)에서 내려받으며 모델 원본의 [CreativeML Open RAIL++-M 고지](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/462165984030d82259a11f4367a4eed129e94a7b/LICENSE.md)를 설치 폴더에 저장합니다. 엔진 원본의 라이선스는 설치한 패키지 폴더에 보존합니다. 추가 가중치의 사용 조건은 해당 배포자의 조건을 따릅니다. [외부 소프트웨어 고지](Docs/ThirdPartyNotices.md)도 참고하세요.
