# Nexys Modbus Workbench (nmw)

Modbus Poll / ModScan을 대체하는 넥시스 시스템사업부 자체 Modbus Master 진단 도구.

- **타깃**: Windows 10 (1809+) / 11 x64 — 단일 exe, .NET 런타임 설치 불요
- **개발/테스트**: macOS에서도 빌드·실행 가능 (Avalonia 크로스플랫폼)
- **스택**: C# 12 / .NET 8 LTS, Avalonia 11 (Fluent), CommunityToolkit.Mvvm, System.IO.Ports
- **프로토콜 코어**: 외부 Modbus 라이브러리 없이 `Nmw.Core`에 직접 구현 (raw 프레임 로깅·골든 벡터 검증 목적)

## 설치 방법 (일반 사용자)

설치 과정이 따로 없다. 실행파일 하나만 받으면 된다.

1. 이 저장소의 **[Releases](../../releases)** 페이지에서 최신 버전의 `Nmw.App.exe`(또는 zip)를 다운로드한다.
2. 원하는 폴더에 두고 **더블클릭 실행**한다.
   - .NET 런타임 설치 불필요 (self-contained 단일 exe)
   - 지원 OS: Windows 10 (1809 이상) / Windows 11, x64
3. 최초 실행 시 Windows SmartScreen 경고가 뜨면 **[추가 정보] → [실행]**을 누른다 (코드 서명 없음).
4. 삭제는 exe 파일을 지우면 끝이다.

> RTU(시리얼) 사용 시: USB-RS485 컨버터 드라이버(예: FTDI/CH340)는 별도로 설치해야 COM 포트가 보인다.

## 기능 요약

| 기능 | 내용 |
|---|---|
| 연결 | Modbus TCP / RTU(시리얼) / RTU over TCP, 타임아웃·재시도·프레임간 지연 설정 |
| 읽기 폴링 | FC01/02/03/04, 폴별 독립 주기(최소 10ms), 시작/정지, 멀티 폴 탭(동일 채널 공유) |
| 쓰기 | FC05/06/15/16 다이얼로그, 그리드 더블클릭 프리필, 응답/exception 명칭 표시 |
| 데이터 포맷 | U16/S16/Hex/Bin, 32bit U/S/Float(워드오더 ABCD·CDAB·BADC·DCBA), 64bit S/Double, ASCII |
| 통계 | 폴별 Tx / Valid Rx / Error / 마지막 응답시간(ms) / 마지막 에러 명칭 |
| 트래픽 로그 | 타임스탬프 + TX/RX raw hex + 오류, 링버퍼 5000라인, 에러만 필터, .txt 저장 |
| 주소 표기 | 프로토콜 주소(0-base) ↔ PLC 표기(1-base, 40001 스타일, 5/6자리) 토글 |
| 워크스페이스 | 연결 설정 + 폴 정의 + 별칭을 `.nmw`(JSON)로 저장/복원 |
| **내장 시뮬레이터** | 물리 장비 없이 테스트 가능한 내장 Modbus TCP 슬레이브 (FC01~06,15,16, 값 편집·자동 변화) |

## 장비 없이 테스트하기 (내장 시뮬레이터)

물리 Modbus 장비가 없어도 앱 단독으로 전체 기능을 테스트할 수 있다.

1. 툴바의 **[시뮬레이터...]** 클릭 → 시뮬레이터 창에서 포트(기본 1502) 확인 후 **[시작/정지]**
2. 시뮬레이터 그리드(홀딩/입력 레지스터, 코일, 접점)에서 값을 직접 입력하거나
   **[값 자동 변화]**를 켜서 레지스터 값이 계속 바뀌게 한다
3. 메인 창 **[연결...]** → Modbus TCP, 호스트 `127.0.0.1`, 포트 `1502` 입력 후 연결
4. 폴 시작 → 시뮬레이터 값이 그리드에 표시된다. 쓰기(FC05/06/15/16)를 하면
   시뮬레이터 그리드에 즉시 반영된다
5. 에러 테스트: 시뮬레이터 영역(기본 주소 0~999) 밖 주소를 폴링하면
   `Illegal Data Address`가 표시된다. 시뮬레이터를 정지하면 Timeout/연결 오류를 볼 수 있다

시뮬레이터는 127.0.0.1 전용이며 모든 Unit ID에 응답한다. 다중 연결을 지원하므로
폴 탭 여러 개를 동시에 돌려볼 수 있다.

## 저장소 구조

```
src/Nmw.Core/       프로토콜 코어 (UI 의존 없음): Protocol, Framing, Transport, Client, Polling, Data, Workspace
src/Nmw.App/        Avalonia UI (ViewModels / Views / Models)
tests/Nmw.Core.Tests/         골든 벡터 + 단위 테스트
tests/Nmw.Integration.Tests/  테스트 슬레이브(TCP) 상대 왕복 통합 테스트
tests/Nmw.App.Tests/          Avalonia.Headless 기반 UI 스모크 테스트
```

## 빌드 / 테스트 / 실행

```bash
dotnet build NexysModbusWorkbench.sln   # 경고 0 기준 (TreatWarningsAsErrors)
dotnet test  NexysModbusWorkbench.sln   # 전체 테스트
dotnet run --project src/Nmw.App        # 개발 실행 (macOS/Windows)
```

## 배포 (win-x64 단일 exe)

```bash
dotnet publish src/Nmw.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

- 산출물: `publish/win-x64/Nmw.App.exe` (검증 시 약 43.2MB — 기준 90MB 이내)
- macOS에서도 크로스 publish 가능. 배포용 exe는 Windows PC에서 위 명령으로 재생성한다.
- `publish/` 폴더는 커밋하지 않는다 (.gitignore 대상).

## 수용 스모크 체크리스트

배포 전 [SMOKE_CHECKLIST.md](SMOKE_CHECKLIST.md)를 따라 확인한다.

## 문서

- [PRD.md](PRD.md) — 요구사항/마일스톤
- [DESIGN.md](DESIGN.md) — 설계서 + 골든 테스트 벡터 (수정 금지)
- [RALPH_LOG.md](RALPH_LOG.md) — 빌드 이력
