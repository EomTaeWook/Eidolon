# Eidolon

Avalonia로 만든 프롬프트 중심 데스크톱 이미지 생성·LoRA 학습 프로그램입니다. 기본 모델은 SDXL Base 1.0이며 기본 생성 크기는 1024×1024입니다. 초기 실행 환경과 기본 모델을 준비한 다음, 생성 화면에서 원하는 이미지를 자연어로 설명하면 됩니다.

## 실행

.NET 10 SDK가 있는 Windows에서 다음 명령으로 실행합니다.

```powershell
dotnet run --project Eidolon.App
```

1. **엔진**에서 설치 폴더를 선택합니다. 예: `D:\AI`.
2. NVIDIA GPU를 사용한다면 기본 설치 옵션을 유지합니다. GPU가 없으면 CPU 옵션을 선택합니다. CPU에서는 이미지 생성만 지원합니다.
3. **선택한 경로에 설치 · 복구**를 누릅니다. 선택한 폴더 아래 `EidolonRuntime`에 Python, 생성·학습 환경을 설치합니다. 기본 SDXL 모델 다운로드 옵션을 켜면 모델도 준비합니다.
4. **이미지 생성**에서 프롬프트를 입력하고 **대기열에 추가**를 누릅니다. 생성 결과는 자동 보관되며 **이미지 저장**으로 다른 위치에도 내보낼 수 있습니다.

생성·학습 요청은 추가한 순서대로 하나씩 실행합니다. 실행 중에도 다음 요청을 작성하고 추가할 수 있습니다. 하단에서 현재 작업과 진행률, 대기 건수·목록을 확인합니다. **작업 취소**는 실행 중인 요청만 취소하며, **대기 작업 비우기**는 아직 시작하지 않은 요청을 제거합니다. 앱 종료 시 현재 작업과 대기 요청을 취소하며 대기열은 재실행 시 복원하지 않습니다.

설치에는 인터넷 연결이 필요하고 Python 라이브러리와 모델을 여러 GB 다운로드합니다. 기본 SDXL 모델은 약 6.94 GB입니다. 중단한 다운로드는 다시 설치할 때 이어받습니다. GPU 설치에는 CUDA 12.8을 지원하는 NVIDIA 드라이버가 필요하며 별도 CUDA Toolkit 설치를 요구하지 않습니다. CPU·GPU 설치 옵션을 바꾼 경우 설치·복구를 다시 실행합니다.

**엔진**에는 ComfyUI **서버 주소** 하나만 입력합니다. 기본값은 `http://127.0.0.1:8189`이며 다른 프로그램의 로컬 서버나 원격 서버 주소도 입력할 수 있습니다. 실행 중인 서버가 있으면 자동으로 연결하고, 서버가 없는 로컬 주소에는 설치된 엔진을 실행합니다. 주소 변경은 자동 적용하며 작업 중에는 대기열이 끝난 뒤 연결을 바꿉니다. 연결 전에는 **엔진 켜기** 또는 **서버 연결**, 연결 후에는 **엔진 정지** 또는 **연결 끊기**를 표시합니다. C#이 Python 프로세스와 준비 상태를 관리하고 앱이 시작한 서버는 앱 종료·비정상 종료 시 종료합니다. 기존 서버는 유지합니다.

## 기능

- 프롬프트 입력 → 자동 모델 로드·샘플링·PNG 생성
- 설정의 공용 긍정·부정 프롬프트 적용
- 결과 미리보기, 생성 기록, 프롬프트 재사용과 이미지 내보내기
- SD 1.5·SDXL 체크포인트와 LoRA의 safetensors 가져오기·계열 확인·트리거 관리
- 선택한 LoRA를 생성에 적용하고 트리거를 자동 추가
- 사용자 이미지 폴더로 LoRA 학습, 기존 LoRA 이어 학습, 완료 가중치 자동 등록
- 앱 시작 시 ComfyUI 실행, 종료 시 관리 서버 종료, 주소를 입력해 외부 서버 사용
- 작업 취소, 원본 학습 데이터 보존, 설정·이력 JSON 백업
- 생성·학습 공용 FIFO 대기열, 실행 중 다음 요청 추가, 현재 작업 취소와 대기 요청 비우기
- 설정에서 라이트·블랙 테마와 한국어·영어 선택, 즉시 적용과 저장
- 브랜드와 같은 줄의 아이콘 탐색, 사용법 탭, 하단 작업 상태·진행률
- DignusLog 파일 로그와 종료 시 로그 정리

엔진 준비 후 모델 폴더의 `.safetensors` 파일을 자동으로 읽습니다. **모델 목록 새로고침**으로 다시 읽을 수 있습니다. 실제 설치된 모델과 계열만 기본 목록에 표시하고 파일이 없는 등록은 제외합니다. 자산의 계열을 자동 확인할 수 없으면 모델 화면에서 지정합니다. 기본 모델을 지정하지 않았으면 SDXL을 먼저 선택하며 같은 계열의 LoRA만 생성 화면에 표시됩니다. 등록 해제는 라이브러리 정보만 제거하며 가중치 파일은 보존합니다.

LoRA 학습에서는 기반 모델, 이미지 폴더, 이름, 트리거 단어를 지정합니다. PNG·JPG·BMP와 하위 폴더를 지원합니다. 동일 이름의 `.txt` 캡션이 있으면 사용하며, 없으면 입력한 공통 설명을 사용합니다. 이미지와 캡션은 작업 폴더로 복사하므로 원본은 수정하지 않습니다. 완료한 LoRA는 생성 화면에서 선택됩니다. 첫 학습에는 텍스트 인코더의 토크나이저 등을 추가로 다운로드할 수 있습니다. **기존 LoRA 이어 학습**을 켜고 같은 계열의 로컬 LoRA를 선택하면 가중치·rank부터 추가 학습합니다. optimizer와 스케줄은 새로 시작하며 결과는 새 LoRA로 저장합니다. 학습에는 로컬 실행 환경과 기반 모델이 필요합니다.

ComfyUI 노드 그래프와 생성 파라미터는 제품 내부에서 구성합니다. 학습 프리셋은 입문용 고정값이며 데이터별 품질은 별도 조정·평가가 필요합니다. 현재 SD3·FLUX·inpainting 전용·refiner 전용 모델은 지원하지 않습니다.

## 구조와 저장 위치

| 위치 | 내용 |
|---|---|
| `Eidolon.Core` | Domain 계약, Application 유스케이스, Infrastructure의 설치·ComfyUI·학습·저장 구현 |
| `Eidolon.App` | Avalonia 화면, ViewModel·Presenter, 파일 대화상자, 다국어·템플릿 로딩, DI와 앱 수명 |
| `DataContainer` | Excel 변환기가 생성하는 템플릿·컨테이너·로더 |
| `<선택한 폴더>\EidolonRuntime` | 실행 도구·Python·엔진·모델·다운로드 캐시·설치 로그 |
| `%LOCALAPPDATA%\Eidolon` | 사용자 설정, 자산 목록, 생성·학습 작업과 결과 |

파일 구성과 Core/App 경계는 [작업 규칙](Docs/WorkingRules.md)을 따릅니다. 적용 규칙과 책임 경계는 [EidolonArchitecture](Docs/EidolonArchitecture.md)에 정리했습니다. 다국어 문구는 [Excel/String.xlsx](Excel/String.xlsx)에서 관리하며, 변환 방법은 [문자열 데이터 변환](Docs/StringData.md)을 참고하세요.

설정 파일이 손상되면 앱은 덮어쓰지 않고 오류를 표시합니다. 사용자 데이터 폴더의 해당 `.bak` 파일을 확인해 복원할 수 있습니다. 비정상 종료한 작업은 다음 실행에 중단 상태로 표시됩니다. 앱 로그는 `%LOCALAPPDATA%\Eidolon\Logs\Eidolon.log`, 설치 실패 로그는 `EidolonRuntime\Logs\Install.log`, 생성 엔진 로그는 같은 폴더의 `Engine.log`, 학습 로그는 선택한 작업 폴더의 `Training.log`에 있습니다. 설정과 사용법에서 앱 로그 폴더를 열 수 있습니다.

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
