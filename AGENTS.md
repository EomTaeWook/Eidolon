# Eidolon

작업 전에 [작업 규칙](Docs/WorkingRules.md), [기술 구조](Docs/EidolonArchitecture.md), [코딩 컨벤션](Docs/ReusableArchitecture/CodingConvention.md)을 읽는다.

모델을 제외한 최상위 타입은 파일별로 분리한다. Core에 언어·테마·번역 서비스나 UI 리소스 키를 넣지 않는다. 다국어와 DI·템플릿 로딩은 App이 소유한다.

사용자 지시에 따라 빌드·테스트는 실행하지 않는다. 전체 검수는 정적 코드 검수로 진행하며, 실행 검증은 사용자가 별도로 요청할 때만 수행한다.
