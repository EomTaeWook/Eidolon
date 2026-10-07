# 문자열 데이터 변환

원본은 [Excel/String.xlsx](../Excel/String.xlsx)다. Bough와 같은 Data·Define 시트와 Id, Name, Kor, Eng 필드를 사용한다. 기존 원본 행을 보존하며 Eidolon 문구는 별도 ID·이름으로 관리한다.

원본을 수정한 뒤 ExportTools/ExcelToJson에서 ExcelToJson.exe --no-pause를 실행하고 ExportTools/JsonToCSharp에서 JsonToCSharp.exe --no-pause를 실행한다. 생성 JSON·C#은 직접 수정하지 않는다. 현재 사용자 지시로 빌드·테스트는 실행하지 않으며 변환기는 데이터 산출물 생성 단계다.

| 산출물 | 소비 계약 |
|---|---|
| Datas/String.json | ExcelToJson 생성. App 어셈블리에 내장하며 Debug 출력 폴더에도 복사 |
| ExportTools/Exported/ClassDefine/String.json, Ref/enumRef.json | JsonToCSharp의 스키마·참조 |
| DataContainer/Generated/*.cs | 생성 템플릿·컨테이너·로더 |

Bough와 같이 Program에서 App의 TemplateDataLoader를 실행하여 생성 TemplateLoader.Load와 MakeRefTemplate을 호출한다. TemplateDeserializer가 JSON을 생성 타입으로 읽고 TemplateContainer<StringTemplate>에 로드한다. Debug에서는 출력 폴더의 Datas를 먼저 읽고 배포 시 App의 내장 리소스를 사용한다.

App의 공용 StringHelper가 ID·이름·템플릿으로 현재 언어의 문자열을 조회한다. 별도 번역 데이터 사전이나 병렬 원본은 만들지 않는다. Core는 문자열 서비스·UI 리소스 키·Excel 키 번호를 참조하지 않는다. App의 StudioMessageTemplates가 Core 오류·진행 코드와 Excel 키의 대응을 소유한다. LanguageService가 언어 변경을 화면 리소스에 반영하고 App의 SettingsStore가 선택 언어를 저장한다.

한국어·영어의 필수 값과 포맷 인자를 일치시킨다. 사용자가 입력한 프롬프트·모델 이름·캡션과 외부 도구의 원문은 번역하지 않는다. 새 작업 오류는 코드·인자로 저장해 표시할 때 번역하고 이전 이력의 원문 오류도 읽는다. UI 리소스 키·코드 대응 변경 시 App과 Excel을 함께 수정한다.
