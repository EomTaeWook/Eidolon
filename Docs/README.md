# Eidolon 문서

현재 구현을 파악하려면 아키텍처를, 변경 작업을 시작하려면 작업 규칙을 읽는다.

| 문서 | 책임 |
|---|---|
| [사용법](Usage.md) | 처음 시작하기, 생성·편집·결과 관리, 에셋 제작·학습, 저장 위치와 문제 해결 |
| [아키텍처](EidolonArchitecture.md) | Core/App 경계, DI·수명, 생성 환경·설정 화면, ComfyUI·Codex 생성·편집, 모델 관리·학습, FIFO 큐, 저장·배포 |
| [작업 규칙](WorkingRules.md) | 파일별 타입 분리, 계층 경계, 문자열·생성물, 데이터·프로세스 소유권, 검수·커밋 기준 |
| [문자열 데이터 변환](StringData.md) | Excel 원본, 변환 명령과 생성 템플릿·로더 계약 |
| [MCP 연결](Mcp.md) | 별도 HTTP 라이브러리, 앱 도구, 요청별 지침과 클라이언트 연결 |
| [에셋 제작](AssetCreation.md) | ComfyUI·Codex 스프라이트와 사면도 초안, 재생·수정·삭제·내보내기 계약 |
| [외부 소프트웨어 고지](ThirdPartyNotices.md) | 외부 엔진·라이브러리와 기본 모델의 고지 |
| [재사용 아키텍처](ReusableArchitecture/README.md) | 프로젝트에서 선택해 적용하는 공통 구조·컨벤션 카탈로그 |

Eidolon은 재사용 문서 중 [CodingConvention](ReusableArchitecture/CodingConvention.md)과 [ReleasePolicy](ReusableArchitecture/ReleasePolicy.md)를 적용한다. 실제 프로젝트 책임·경로와 사용자 지시는 Eidolon 문서와 루트 [AGENTS.md](../AGENTS.md)를 따른다.

프로그램의 실행·사용 안내는 루트 [README.md](../README.md)에 있다. 현재 검증 중단 지시는 작업 규칙에 기록하며 문서 작성이나 Git 커밋·푸시가 실행 검증을 의미하지 않는다.
