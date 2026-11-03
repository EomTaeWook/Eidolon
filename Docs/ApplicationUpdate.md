# 앱 업데이트

시작 화면 초기화가 끝나면 정식 업데이트 조회를 앱 실행당 한 번 시작하고 조회 완료를 기다리지 않는다. 조회 상태와 오류는 기존 앱 업데이트 화면에 표시하며 시작 화면이나 생성·학습 작업을 막지 않는다. 설정 맨 위의 앱 업데이트에서 현재 버전과 조회 결과를 확인하고 **업데이트 확인**으로 다시 조회할 수 있다. 새 정식 버전이 있으면 사용자가 **업데이트 후 재시작**을 눌러 다운로드·검증·설치를 진행한다. 진행 중에는 취소할 수 있고 릴리스 내용을 브라우저에서 열 수 있다. 다운로드·설치는 시작 시 자동 실행하지 않으며 베타 버전을 포함하는 설정은 제공하지 않는다. 업데이트 후 재시작 결과를 표시하는 실행에서는 이 결과를 보존하기 위해 자동 조회를 생략한다.

Bough의 정식 릴리스 조회, 검증한 임시 다운로드, 정상 종료 후 단일 실행 파일 교체 흐름을 참고한다. Bough의 Git 작업·설정·저장소 상태를 가져오지 않고 Eidolon의 기존 작업 큐와 종료 경계에 연결한다.

## 릴리스와 배포 계약

- 원본은 `https://api.github.com/repos/EomTaeWook/Eidolon/releases/latest`다. Git 인증·ComfyUI 서버 주소·사용자 설정을 업데이트 원본에 사용하지 않는다.
- `draft`·`prerelease`를 제외하고 `v1.2.3` 또는 `1.2.3` 형식의 정식 태그를 사용한다. 현재 버전보다 높은 버전만 설치한다. 같은 숫자의 사전 공개 버전에서 정식 버전으로 바꾸는 것은 허용한다.
- Windows x64 asset 이름은 `Eidolon-{tag}-win-x64.exe` 또는 `Eidolon.exe`이며 일치하는 파일이 하나여야 한다. 크기·업로드 상태·공식 저장소의 다운로드 경로를 확인한다.
- macOS asset 이름은 `Eidolon-{tag}-osx-arm64.app.zip` 또는 `Eidolon-{tag}-osx-x64.app.zip`이다. 실행 중인 프로세스의 아키텍처에 맞는 파일 하나만 선택하며 네이티브 아키텍처 실행에서 자동 설치를 제공한다.
- asset의 `digest`에 `sha256:<해시>`가 있어야 자동 설치할 수 있다. 전체 크기와 SHA-256이 일치하지 않거나 digest가 없으면 교체하지 않고 이유와 릴리스 열기를 제공한다.
- 현재 버전은 App의 `AssemblyInformationalVersion`을 읽는다. 릴리스 게시 시 App의 `Version`과 태그의 버전을 맞춘다. 조회·다운로드에는 앱 소유 HTTP 클라이언트를 사용하며 사용자 인증 토큰을 가져오지 않는다.

`WindowsX64.pubxml`·`MacOsArm64.pubxml`·`MacOsX64.pubxml`의 단일 파일·자체 포함 배포를 사용한다. macOS는 실행 파일·Info.plist·아이콘·서명을 담은 `Eidolon.app`을 ZIP으로 제공한다. `PublishSingleFile=true`일 때 `EidolonPublishedSingleFile` 어셈블리 메타데이터를 생성한다. 설치는 이 표시와 지원 운영체제·아키텍처·실행 경로·디버거·일반 개발 출력 파일 여부를 확인한다. `dotnet.exe`나 IDE의 개발 출력은 교체하지 않는다. 정식 릴리스가 없는 404, 조회 실패, 최신 버전 상태를 구분한다.

## 다운로드와 종료

시작 시 조회와 수동 조회는 기존 `AppUpdateViewModel.CheckAsync`·`_checking`·취소 토큰을 공유한다. 초기화가 끝난 `StudioViewModel`은 `CheckOnStartup`으로 조회를 시작하고 완료를 기다리지 않는다. `AppUpdateViewModel`이 실행당 자동 조회 한 번을 소유하고 종료 시 기존 `CloseAsync`에서 취소·대기한다. 설치는 생성·학습·대기 작업과 예약 작업이 없을 때 기존 `StudioWorkPresenter`의 유지 관리 요청으로 추가한다. 별도 큐를 만들지 않는다. 업데이트 카드에는 버전·상태·기존 실행 버튼만 두며 진행 막대를 표시하지 않는다. 업데이트 확인·다운로드 진행률과 활동 문구는 MainWindow의 하단 공통 상태 영역에 표시한다. 실행 중인 공용 큐 작업이 있으면 기존 작업 진행률을 우선하고, 다운로드 중에는 기존 StudioSession 진행률 연결을 사용한다. 업데이트 확인은 기존 Updates.IsBusy·Percentage·IsIndeterminate 바인딩으로 하단에 표시하며 시작 화면을 막지 않는다. 요청에 고정한 릴리스 정보만 사용하며 설치 중에는 새 생성 요청을 받지 않는다.

파일은 `%TEMP%/Eidolon/application-update/<GUID>`에서 준비한다. 설치 담당은 검증한 EXE 또는 ZIP을 별도 요청 폴더에 복사하고 현재 실행 파일을 Windows의 `helper.exe` 또는 macOS의 `helper`로 복사한다. macOS는 허용한 `Eidolon.app` 하위의 일반 파일·디렉터리만 제한된 크기로 추출하며 절대 경로·상위 이동·링크·중복 파일을 거부한다. 실행 권한과 bundle identifier·버전·Mach-O 아키텍처를 확인하고 현재·후보 번들의 전체 파일 해시를 종료 전후에 비교한다. 준비·취소·정리는 해당 요청의 고정 파일과 폴더에만 적용하며 사용자 설정·이미지·학습 데이터·엔진 설치는 건드리지 않는다.

준비가 끝나고 큐가 비면 App의 재시작 Presenter가 기존 MainWindow 닫기를 요청한다. `StudioViewModel.DisposeAsync`가 MCP·진행 중인 읽기·작업 큐·앱 소유 엔진을 정상 정리한다. UI 수명이 반환된 뒤에만 `Program`이 보조 프로세스의 설치를 승인한다. 예외로 앱이 종료되거나 승인 없이 원래 프로세스가 종료되면 실행 파일을 교체하지 않는다.

보조 프로세스는 전용 인수를 `Program.Main`에서 먼저 처리한다. 단일 실행 Mutex·Avalonia·템플릿·사용자 설정·로그를 초기화하지 않는다. 앱 종료 뒤에도 동작해야 하므로 엔진용 부모 프로세스 종료 경계에 넣지 않는다. 원래 PID·시작 시각·실행 경로·기존 해시·승인 nonce를 확인하고 실제 종료를 기다린다. 프로세스를 강제로 종료하거나 권한을 높이거나 재부팅에 교체를 예약하지 않는다.

## 교체와 재실행

보조 프로세스는 교체 직전에 고정 경로·링크 여부·기존 파일과 후보의 SHA-256을 다시 확인한다. 후보를 설치 폴더의 고유 임시 파일에 복사한 뒤 `File.Replace`로 교체하고 원본을 백업한다. 새 프로세스를 시작하면 원본 백업을 정리한다. `Process.Start`가 실패하면 원본을 복원해 같은 경로에서 다시 실행한다. 복원 실패 시 백업을 보존한다. 이 처리는 새 앱의 모든 초기화·기능 실행을 확인하는 건강 상태 검증은 제공하지 않는다.

macOS 설치 담당은 실행 중인 `Eidolon.app`이 있는 쓰기 가능한 폴더에서 고유 후보 번들과 백업을 준비한다. 정상 종료와 승인 뒤 기존 번들을 백업으로, 후보를 기존 경로로 차례대로 이동하고 같은 앱의 실행 파일을 재시작한다. 이동 또는 재실행에 실패하면 원본 번들을 복원하고 실패 상태로 재실행한다. 복원 실패 시 원본 백업을 보존한다. 다른 볼륨으로 직접 이동하거나 관리자 권한을 요청하지 않으며 설정·이미지·엔진 경로는 교체 대상이 아니다.

재실행된 앱은 보조 프로세스가 종료된 뒤 해당 임시 요청 폴더를 정리하고 설정의 앱 업데이트 화면으로 돌아온다. 교체·새 프로세스 시작 실패로 원본이 재실행되면 업데이트 카드에서 실패 상태를 유지한다.

## 책임과 파일

| 위치 | 책임 |
|---|---|
| App `Models/Updates` | 배포용 버전·환경·릴리스·조회·진행·검증 파일·설치 계획·manifest 모델과 업데이트 메시지 코드 |
| App `Services/Updates/ApplicationUpdateService` | 정식 릴리스 선택, 버전 비교, 제한한 HTTPS 다운로드와 검증 |
| App `Services/Updates/ApplicationUpdateFiles` | 요청 임시 경로, 고정 파일 정리, 링크 확인과 해시 계산. App이 주입하며 보조 진입점은 독립 인스턴스를 구성 |
| App `Services/Updates/ApplicationUpdateInstaller` | 공통 설치 준비, 보조 프로세스 시작·취소와 정상 종료 후 승인 |
| App `Services/Updates/WindowsApplicationUpdateInstaller`, `MacOsApplicationUpdateInstaller` | DI로 선택하는 OS별 asset·배포 환경 확인·교체·복원·재실행 |
| App `Services/Updates/ApplicationUpdateHelper` | 전용 보조 진입점, 기존 프로세스 인증·종료·승인 대기, 설치 담당 호출과 재실행 후 정리 |
| App `Services/Updates/ApplicationUpdateException`, `Localization/ApplicationUpdateMessageTemplates` | 독립 업데이트 오류 계약과 기존 문자열 템플릿 대응 |
| App `Presenters/ApplicationUpdateRestartPresenter` | MainWindow 닫기 요청과 정상 UI 종료 후 설치 인계 |
| App `ViewModels/Updates/AppUpdateViewModel`, `Views/AppUpdateView` | 조회·설치 명령, 큐 연결, 진행·취소·실패 표시와 언어 변경 |
| App `App`, `Program` | HTTP·DI·배포 메타데이터 조립, 전용 인수와 앱 수명 연결 |

앱 버전·업데이트는 App에만 존재한다. 오류는 `ApplicationUpdateException`의 `ApplicationUpdateMessageCode`·인자로 전달하고 `ApplicationUpdateMessageTemplates`·`StringHelper`가 번역한다. Core의 `StudioException`·`StudioMessageCode`·`WorkProgress`에 업데이트 항목을 추가하지 않는다. 다운로드 진행은 `ApplicationUpdateProgress`로 전달하며 ViewModel이 기존 `StudioSession`의 상태·진행률을 갱신한다. 문구 원본은 기존 `Excel/String.xlsx`이며 JSON·템플릿은 [문자열 생성 절차](StringData.md)를 따른다. 별도 업데이트 설정 파일·번역 사전·영구 updater 배포물을 만들지 않는다.

## macOS 배포 범위

`Scripts/Package-MacOs.ps1`은 macOS에서 기존 PNG 아이콘을 ICNS로 만들고 자체 포함 단일 실행 파일을 앱 번들에 넣은 뒤 JIT entitlement를 부여한 ad-hoc 서명과 ZIP 포장을 수행한다. `.github/workflows/release.yml`은 지정 태그의 Windows·macOS ARM64·Intel 배포 파일을 만들고 Actions artifact로 제공한다. 공식 릴리스 게시와 자동 업데이트 대상 버전은 총괄이 같은 태그·커밋·파일 해시로 확정한다.

Apple Developer ID 서명과 공증은 현재 배포 설정에 없으므로 첫 실행은 macOS 보안 설정에서 사용자의 허용이 필요할 수 있다. ZIP을 풀어 `Eidolon.app`을 쓰기 가능한 Applications 폴더로 옮긴 뒤 Finder에서 실행한다. 앱 안의 업데이트 버튼은 다운로드·검증·정상 종료·교체·재시작을 자동으로 이어간다. 시작 시에는 조회만 자동 실행하며 다운로드·설치까지 자동 시작하지 않는 기존 정책을 유지한다.

맥 생성 경로는 설치한 Codex 또는 외부 ComfyUI 서버 연결이다. Codex 실행 파일은 직접 지정하거나 PATH·Homebrew의 기본 실행 경로에서 찾는다. 로컬 ComfyUI·LoRA 학습·프롬프트 변환기 설치는 기존 Windows x64/NVIDIA 런타임 범위를 유지한다. 이번 변경은 이 런타임을 macOS용으로 이식하지 않는다. 실제 맥 UI·생성·업데이트 교체 실행 확인은 배포 파일 생성과 구분한다.
