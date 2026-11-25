# 문자열 데이터 변환

원본은 [Excel/String.xlsx](../Excel/String.xlsx)다. Data·Define 시트와 Id, Name, Kor, Eng 필드를 사용한다. 기존 원본 행을 보존하며 Eidolon 문구는 별도 ID·이름으로 관리한다.

원본을 수정한 뒤 ExportTools/ExcelToJson에서 ExcelToJson.exe --no-pause를 실행하고 ExportTools/JsonToCSharp에서 JsonToCSharp.exe --no-pause를 실행한다. 생성 JSON·C#은 직접 수정하지 않는다. 변환기는 데이터 산출물 생성 단계이며, 빌드·실행 검증은 작업별 명시적 사용자 요청 범위를 따른다.

| 산출물 | 소비 계약 |
|---|---|
| Datas/String.json | ExcelToJson 생성. App 어셈블리에 내장하며 Debug 출력 폴더에도 복사 |
| ExportTools/Exported/ClassDefine/String.json, Ref/enumRef.json | JsonToCSharp의 스키마·참조 |
| DataContainer/Generated/*.cs | 생성 템플릿·컨테이너·로더 |

Program에서 App의 TemplateDataLoader를 실행하여 생성 TemplateLoader.Load와 MakeRefTemplate을 호출한다. TemplateDeserializer가 JSON을 생성 타입으로 읽고 TemplateContainer<StringTemplate>에 로드한다. Debug에서는 출력 폴더의 Datas를 먼저 읽고 배포 시 App의 내장 리소스를 사용한다.

App의 공용 StringHelper가 ID·이름·템플릿으로 현재 언어의 문자열을 조회한다. 별도 번역 데이터 사전이나 병렬 원본은 만들지 않는다. Core는 문자열 서비스·UI 리소스 키·Excel 키 번호를 참조하지 않는다. App의 StudioMessageTemplates가 Core 오류·진행 코드와 Excel 키의 대응을 소유한다. LanguageService가 언어 변경을 화면 리소스에 반영하고 App의 SettingsStore가 선택 언어를 저장한다.

한국어·영어의 필수 값과 포맷 인자를 일치시킨다. 사용자가 입력한 프롬프트·모델 이름·캡션과 외부 도구의 원문은 번역하지 않는다. 새 작업 오류는 코드·인자로 저장해 표시할 때 번역하고 이전 이력의 원문 오류도 읽는다. UI 리소스 키·코드 대응 변경 시 App과 Excel을 함께 수정한다.

## 변환기 소스와 셀 읽기

ExcelToJson의 유지보수 소스는 별도 [Dignus.Tool 저장소](https://github.com/EomTaeWook/Dignus.Tool)의 `ExcelToJson/ExcelToJson.csproj`와 `ExcelToJson/ExcelReader.cs`다. 이 PC의 경로는 `D:\Source\Dignus\Dignus.Tool\ExcelToJson`이다. Eidolon에는 빌드된 변환기를 `ExportTools/ExcelToJson`에 배치한다.

교체 전 배포 EXE의 제품 버전과 PDB Source Link는 이전 Kosher 저장소의 `9439c7060a27c4fb5ade22af2f61d1949f584e5d` 커밋을 가리킨다. 해당 커밋의 `Tools/ExcelToJson/ExcelReader.cs`와 수정 전 Dignus.Tool 소스의 Git blob은 일치했다.

변환기의 공통 NuGet 의존성은 Dignus 1.4.3과 Dignus.Log 3.0.0이다. 두 패키지는 공식 피드의 최신 안정 버전으로 갱신했고, 두 변환기의 로그 초기화를 새 API에 맞췄다. ExcelToJson·JsonToCSharp 빌드는 경고·오류 없이 성공했다.

변환기는 .NET 10의 ZIP·XML 기능으로 `.xlsx` 내부의 시트 관계와 셀 주소를 읽는다. ACE OLE DB의 열 타입 추론이나 PC별 레지스트리 설정을 사용하지 않는다. 공유 문자열·인라인 문자열·서식 있는 문자열의 본문, 공백, 줄바꿈을 보존하고 빈 셀은 기존 변환 단계에 DBNull로 전달한다. 수식은 저장된 계산 결과를 읽으며 다시 계산하지 않는다. Define·Data 매핑과 JSON 직렬화는 기존 변환 경로를 유지한다.

## 2026-11-25 재변환 확인

수정한 변환기를 Release·win-x64 단일 실행 파일로 빌드해 배치하고, 실제 `Excel/String.xlsx`에서 `Datas/String.json`을 다시 생성했다. JSON은 수동 편집하지 않았다.

원본 1,308행과 JSON 1,308행의 Name·Id·Kor·Eng를 대조해 모두 일치했다. 기존 JSON과 비교하면 아래 영문 7개만 255자 절단에서 복구됐으며, 나머지 영문·모든 한글·행 순서는 그대로다. 줄바꿈이 있는 한글·영문 각 14개도 원본과 일치했다.

| Name | 재생성된 Eng 길이 |
| --- | ---: |
| EidolonText189 | 269 |
| EidolonText192 | 335 |
| EidolonText195 | 262 |
| EidolonText569 | 316 |
| EidolonText662 | 266 |
| EidolonText663 | 272 |
| EidolonText665 | 470 |

Excel 원본 SHA-256은 `356BAA4E93351C8055AC1CE9DF6DF2C4B31CD4FCE6E640A52BC3C2E85232140E`로 변경되지 않았다. 재생성된 JSON SHA-256은 `BBFAD52CB15DC0D748FD0F95AA4F20880B69783C68C801B972CD1F340F6500D4`다. ClassDefine과 enumRef의 해시도 변경되지 않아 JsonToCSharp 재생성은 수행하지 않았다. 앱 실행이나 릴리스 검증은 수행하지 않았다.
