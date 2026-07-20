# CLAUDE.md — Nexys Modbus Workbench 빌드 헌법

이 문서는 이 저장소에서 작업하는 모든 Claude Code 세션이 무조건 따라야 하는 최상위 규칙이다.
PRD.md(무엇을), DESIGN.md(어떻게), PROMPT_ralph.md(작업 루프)와 함께 읽는다.
규칙 충돌 시 우선순위: **CLAUDE.md > DESIGN.md > PRD.md**.

---

## 1. 프로젝트 정의

- 제품명: **Nexys Modbus Workbench** (내부 코드명 `nmw`)
- 목적: Modbus Poll / ModScan 을 대체하는 넥시스 시스템사업부 자체 Modbus Master 진단 도구
- 타깃: Windows 10/11 x64, **단일 실행파일(exe), 런타임 설치 불요(self-contained)**
- 최우선 가치: **핵심 기능(폴링·읽기/쓰기·포맷 변환·트래픽 로그)의 확실하고 강력한 동작**. 화려함보다 정확성과 안정성.

## 2. 기술 스택 (고정 — 변경 금지)

| 영역 | 선택 | 비고 |
|---|---|---|
| 언어/런타임 | C# 12 / .NET 8 LTS | `net8.0` |
| UI | Avalonia 11.x (Fluent theme) | 크로스플랫폼 → macOS에서 개발/실행 테스트, Windows로 publish |
| 시리얼 | `System.IO.Ports` NuGet 패키지 | |
| 테스트 | xUnit + 자체 Modbus TCP 테스트 슬레이브 | |
| 직렬화 | `System.Text.Json` | 워크스페이스 저장 |
| 배포 | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` | M10에서만 |

### 절대 금지
- **외부 Modbus 라이브러리 금지** (NModbus, FluentModbus, EasyModbus 등). 프로토콜 코어는 `Nmw.Core`에 직접 구현한다. 이유: raw 프레임 로깅, 골든 벡터 검증, IP 재사용.
- LangChain / LangGraph 등 LLM 프레임워크 금지 (이 프로젝트와 무관하지만 명시).
- Electron / Node.js / npm 사용 금지. `node_modules` 생성 절대 금지.
- MVVM 프레임워크 추가 금지 (Prism, ReactiveUI 전체 도입 금지). Avalonia 기본 + `CommunityToolkit.Mvvm`만 허용.
- UI 스레드에서 블로킹 I/O 금지.

## 3. 정확성 규칙 (검증 우선)

1. **테스트 없는 milestone 완료는 없다.** 각 milestone의 DoD(Definition of Done)에 명시된 테스트가 전부 통과해야 완료다.
2. 프로토콜 코덱(CRC16, MBAP, RTU 프레이밍, 데이터 포맷 변환)은 반드시 **DESIGN.md §7의 골든 테스트 벡터**로 검증한다. 벡터를 임의로 수정하거나 삭제하지 않는다. 테스트가 실패하면 구현이 틀린 것이다.
3. 추측성 구현 금지. Modbus 사양(function code 프레임 구조, exception 코드)은 DESIGN.md에 정의된 대로만 구현한다. DESIGN.md에 없는 동작이 필요하면 RALPH_LOG.md에 `[미정]`으로 기록하고 가장 보수적인 동작(에러 반환)을 택한다.
4. placeholder/가짜 데이터 금지. 데모용 하드코딩 레지스터 값 금지. 테스트 슬레이브의 값은 테스트 코드가 명시적으로 세팅한다.

## 4. 코드 규칙

- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- 모든 I/O는 `async/await` + `CancellationToken`. `.Result` / `.Wait()` 금지.
- `Nmw.Core`는 UI 참조 금지 (Avalonia 네임스페이스 import 시 빌드 헌법 위반).
- public API에는 XML doc 주석. 내부 구현은 주석 최소화, 자명한 코드.
- 예외를 삼키지 않는다. 통신 오류는 `ModbusError` 타입으로 상태에 반영하고 UI에 표시한다.
- 파일 인코딩 UTF-8. 한국어 리소스 문자열은 실제 한글로 작성 (`\uXXXX` 이스케이프 금지).

## 5. 저장소/디스크 규칙

- `bin/`, `obj/`, `publish/`, `*.user`는 `.gitignore` 대상. 커밋 금지.
- `dotnet publish`는 **M10에서만** 실행한다. 중간 milestone에서 publish 산출물 생성 금지.
- 불필요한 NuGet 패키지 추가 금지. 허용 목록: Avalonia.*, CommunityToolkit.Mvvm, System.IO.Ports, xunit*, Microsoft.NET.Test.Sdk, coverlet.collector.

## 6. "완성"의 정의

- 완성 = **리뷰 가능한 소스 코드 + 통과하는 테스트 + RALPH_LOG.md 기록**.
- 최종 exe 생성(M10)은 publish 프로파일과 명령어를 README에 문서화하는 것까지 포함한다. macOS에서 win-x64 publish는 가능하므로 실제로 1회 수행해 산출물 크기와 명령 성공을 확인한 뒤 `publish/` 폴더는 삭제한다 (exe는 Windows PC에서 재생성).

## 7. 작업 방식

- 작업 루프는 PROMPT_ralph.md를 따른다. 한 세션 = 한 milestone.
- 질문하지 않는다. 애매하면 보수적으로 결정하고 RALPH_LOG.md에 결정 근거를 남긴다.
- 부분 diff가 아니라 **완전한 파일 단위**로 생성/수정한다.
