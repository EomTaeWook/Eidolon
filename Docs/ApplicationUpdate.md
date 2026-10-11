# 앱 업데이트

설정 → 일반의 앱 업데이트에서 현재 버전을 확인하고 **업데이트 확인**을 누른다. 새 정식 버전이 있으면 **업데이트 후 재시작**으로 다운로드·검증·설치를 진행한다. 진행 중에는 취소할 수 있고 릴리스 내용을 브라우저에서 열 수 있다. 시작 시 자동 조회·설치하거나 베타 버전을 포함하는 설정은 제공하지 않는다.

Bough의 정식 릴리스 조회, 검증한 임시 다운로드, 정상 종료 후 단일 실행 파일 교체 흐름을 참고한다. Bough의 Git 작업·설정·저장소 상태를 가져오지 않고 Eidolon의 기존 작업 큐와 종료 경계에 연결한다.

## 릴리스와 배포 계약

- 원본은 `https://api.github.com/repos/EomTaeWook/Eidolon/releases/latest`다. Git 인증·ComfyUI 서버 주소·사용자 설정을 업데이트 원본에 사용하지 않는다.
- `draft`·`prerelease`를 제외하고 `v1.2.3` 또는 `1.2.3` 형식의 정식 태그를 사용한다. 현재 버전보다 높은 버전만 설치한다. 같은 숫자의 사전 공개 버전에서 정식 버전으로 바꾸는 것은 허용한다.
- Windows x64 asset 이름은 `Eidolon-{tag}-win-x64.exe` 또는 `Eidolon.exe`이며 일치하는 파일이 하나여야 한다. 크기·업로드 상태·공식 저장소의 다운로드 경로를 확인한다.
- asset의 `digest`에 `sha256:<해시>`가 있어야 자동 설치할 수 있다. 전체 크기와 SHA-256이 일치하지 않거나 digest가 없으면 교체하지 않고 이유와 릴리스 열기를 제공한다.
- 현재 버전은 App의 `AssemblyInformationalVersion`을 읽는다. 릴리스 게시 시 App의 `Version`과 태그의 버전을 맞춘다. 조회·다운로드에는 앱 소유 HTTP 클라이언트를 사용하며 사용자 인증 토큰을 가져오지 않는다.

`WindowsX64.pubxml`의 단일 파일·자체 포함 배포를 유지한다. `PublishSingleFile=true`일 때 `EidolonPublishedSingleFile` 어셈블리 메타데이터를 생성한다. 설치는 이 표시와 Windows x64·실행 경로·디버거·일반 개발 출력 파일 여부를 확인한다. `dotnet.exe`나 IDE의 개발 출력은 교체하지 않는다. 정식 릴리스가 없는 404, 조회 실패, 최신 버전 상태를 구분한다.

## 다운로드와 종료

조회는 화면의 취소 토큰과 앱 수명을 사용한다. 설치는 생성·학습·대기 작업과 예약 작업이 없을 때 기존 `StudioWorkPresenter`의 유지 관리 요청으로 추가한다. 별도 큐를 만들지 않는다. 다운로드 진행과 검증 상태를 업데이트 카드와 기존 진행 영역에 표시한다. 요청에 고정한 릴리스 정보만 사용하며 설치 중에는 새 생성 요청을 받지 않는다.

파일은 `%TEMP%/Eidolon/application-update/<GUID>`에서 준비한다. 설치 담당은 검증된 실행 파일을 별도 요청 폴더에 복사하고 현재 실행 파일을 `helper.exe`로 복사한다. 준비·취소·정리는 해당 요청의 고정 파일과 폴더에만 적용하며 사용자 설정·이미지·학습 데이터·엔진 설치는 건드리지 않는다.

준비가 끝나고 큐가 비면 App의 재시작 Presenter가 기존 MainWindow 닫기를 요청한다. `StudioViewModel.DisposeAsync`가 MCP·진행 중인 읽기·작업 큐·앱 소유 엔진을 정상 정리한다. UI 수명이 반환된 뒤에만 `Program`이 보조 프로세스의 설치를 승인한다. 예외로 앱이 종료되거나 승인 없이 원래 프로세스가 종료되면 실행 파일을 교체하지 않는다.

보조 프로세스는 전용 인수를 `Program.Main`에서 먼저 처리한다. 단일 실행 Mutex·Avalonia·템플릿·사용자 설정·로그를 초기화하지 않는다. 앱 종료 뒤에도 동작해야 하므로 엔진용 부모 프로세스 종료 경계에 넣지 않는다. 원래 PID·시작 시각·실행 경로·기존 해시·승인 nonce를 확인하고 실제 종료를 기다린다. 프로세스를 강제로 종료하거나 권한을 높이거나 재부팅에 교체를 예약하지 않는다.

## 교체와 재실행

보조 프로세스는 교체 직전에 고정 경로·링크 여부·기존 파일과 후보의 SHA-256을 다시 확인한다. 후보를 설치 폴더의 고유 임시 파일에 복사한 뒤 `File.Replace`로 교체하고 원본을 백업한다. 새 프로세스를 시작하면 원본 백업을 정리한다. `Process.Start`가 실패하면 원본을 복원해 같은 경로에서 다시 실행한다. 복원 실패 시 백업을 보존한다. 이 처리는 새 앱의 모든 초기화·기능 실행을 확인하는 건강 상태 검증은 제공하지 않는다.

재실행된 앱은 보조 프로세스가 종료된 뒤 해당 임시 요청 폴더를 정리하고 일반 설정 화면으로 돌아온다. 교체·새 프로세스 시작 실패로 원본이 재실행되면 업데이트 카드에서 실패 상태를 유지한다.

## 책임과 파일

| 위치 | 책임 |
|---|---|
| App `Models/Updates` | 배포용 버전·환경·릴리스·조회·진행·검증 파일·설치 계획·manifest 모델과 업데이트 메시지 코드 |
| App `Services/Updates/ApplicationUpdateService` | 정식 릴리스 선택, 버전 비교, 제한한 HTTPS 다운로드와 검증 |
| App `Services/Updates/ApplicationUpdateFiles` | 요청 임시 경로, 고정 파일 정리, 링크 확인과 해시 계산. App이 주입하며 보조 진입점은 독립 인스턴스를 구성 |
| App `Services/Updates/ApplicationUpdateInstaller` | 설치 준비, 보조 프로세스 시작·취소와 정상 종료 후 승인 |
| App `Services/Updates/ApplicationUpdateHelper` | 전용 보조 진입점, 기존 프로세스 종료 대기, 교체·복원·재실행과 재실행 후 정리 |
| App `Services/Updates/ApplicationUpdateException`, `Localization/ApplicationUpdateMessageTemplates` | 독립 업데이트 오류 계약과 기존 문자열 템플릿 대응 |
| App `Presenters/ApplicationUpdateRestartPresenter` | MainWindow 닫기 요청과 정상 UI 종료 후 설치 인계 |
| App `ViewModels/Updates/AppUpdateViewModel`, `Views/AppUpdateView` | 조회·설치 명령, 큐 연결, 진행·취소·실패 표시와 언어 변경 |
| App `App`, `Program` | HTTP·DI·배포 메타데이터 조립, 전용 인수와 앱 수명 연결 |

앱 버전·업데이트는 App에만 존재한다. 오류는 `ApplicationUpdateException`의 `ApplicationUpdateMessageCode`·인자로 전달하고 `ApplicationUpdateMessageTemplates`·`StringHelper`가 번역한다. Core의 `StudioException`·`StudioMessageCode`·`WorkProgress`에 업데이트 항목을 추가하지 않는다. 다운로드 진행은 `ApplicationUpdateProgress`로 전달하며 ViewModel이 기존 `StudioSession`의 상태·진행률을 갱신한다. 문구 원본은 기존 `Excel/String.xlsx`이며 JSON·템플릿은 [문자열 생성 절차](StringData.md)를 따른다. 별도 업데이트 설정 파일·번역 사전·영구 updater 배포물을 만들지 않는다.
