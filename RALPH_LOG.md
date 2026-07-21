# RALPH_LOG.md — 빌드 이력

이 파일은 Ralph 루프가 milestone 완료 시마다 맨 아래에 엔트리를 append하는 로그다.
사람이 임의로 엔트리를 수정하지 않는다. (형식: PROMPT_ralph.md 참조)

---

## M1 — 솔루션 스캐폴드 + Nmw.Core PDU 빌더/파서 + CRC16 · 2026-07-20 19:59
- 상태: ✅ 완료
- 생성/수정 파일:
  - NexysModbusWorkbench.sln, .gitignore
  - src/Nmw.Core/Nmw.Core.csproj
  - src/Nmw.Core/Framing/Crc16.cs
  - src/Nmw.Core/Protocol/FunctionCode.cs
  - src/Nmw.Core/Protocol/ModbusExceptionCode.cs
  - src/Nmw.Core/Protocol/ModbusError.cs
  - src/Nmw.Core/Protocol/PduParseResult.cs (Unit 포함)
  - src/Nmw.Core/Protocol/PduBuilder.cs
  - src/Nmw.Core/Protocol/PduParser.cs
  - tests/Nmw.Core.Tests/Nmw.Core.Tests.csproj, GlobalUsings.cs, TestHex.cs
  - tests/Nmw.Core.Tests/Crc16GoldenTests.cs (DESIGN §7.1 벡터 6종)
  - tests/Nmw.Core.Tests/PduBuilderGoldenTests.cs (§7.2 벡터 3종 + 경계 검증)
  - tests/Nmw.Core.Tests/PduParserGoldenTests.cs (§7.3 벡터 3종 + 규격 검증)
- 테스트: 45/45 passed (dotnet build 경고 0, 에러 0)
- [결정] M1 스캐폴드 범위를 sln + Nmw.Core + Nmw.Core.Tests로 한정. Nmw.App(Avalonia)은 M6, Nmw.Integration.Tests는 M4에서 생성 — "다음 milestone을 미리 건드리지 않는다" 규칙과 DoD(골든 벡터 통과)에 앱 프로젝트가 불필요하기 때문.
- [결정] FC15/FC16 쓰기 개수 상한은 DESIGN §2.1에 없어 Modbus 스펙 표준값(1968 / 123) 적용. 근거: byteCount 필드가 1바이트라 상한이 필수이며, 표준값이 가장 보수적·호환적. 빌더의 범위 위반 입력은 ArgumentOutOfRangeException으로 즉시 거부(파서와 달리 호출측 프로그래밍 오류이므로).
- [결정] 파서는 예외를 던지지 않고 PduParseResult<T>(Ok|Fail(ModbusError))로 반환 — DESIGN §2.6의 "Result로 반환" 원칙을 파싱 계층에도 적용. ModbusError는 §2.6의 5종(Timeout/CrcMismatch/Exception/TransportClosed/InvalidResponse)을 미리 정의(M3 Client에서 재사용).
- [결정] 비골든 검증 테스트(FC15 비트패킹) 최초 작성 시 스펙 예제 코일 순서를 잘못 옮겨 1회 실패 → 골든 §7.3의 LSB-first 규칙과 대조해 테스트 데이터 쪽 오류로 확인, 테스트 데이터 수정(골든 벡터는 무수정).
- 다음 세션 참고: M2는 MBAP(§2.4, 골든 §7.4) + RTU 프레이밍(§2.5) 구현. Crc16.Validate/ComputeBytes와 PduParser의 CheckHeader(기대 길이 계산에 필요한 FC별 응답 형태)를 재사용할 것. TCP 부분 수신 상태머신(헤더 7바이트 → Length만큼 추가 완독)과 RTU 길이 예측(읽기 응답은 byteCount 수신 후 확정, 에코형 고정 5+CRC, exception 3+CRC) 구현이 핵심.

## M2 — 프레이밍: MBAP(TCP) + RTU 조립/타임아웃 기반 수신 · 2026-07-20 20:05
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Framing/MbapFraming.cs (ADU 빌드, 골든 §7.4)
  - src/Nmw.Core/Framing/MbapAssembler.cs (부분 수신 상태머신: 헤더 7바이트 → Length 완독)
  - src/Nmw.Core/Framing/RtuFraming.cs (UnitId|PDU|CRC 프레임 빌드)
  - src/Nmw.Core/Framing/RtuResponseAssembler.cs (FC 기반 길이 예측 수신: 읽기=byteCount 후 확정, 에코형=8, exception=5)
  - tests/Nmw.Core.Tests/MbapFramingTests.cs, RtuFramingTests.cs
- 테스트: 65/65 passed (dotnet build 경고 0, 에러 0)
- [결정] MBAP 스트림에서 ProtocolId≠0 또는 Length∉[2,254]는 복구 불가능한 스트림 손상으로 간주 → MbapAssembler.IsCorrupted 상태로 표시하고 프레임 생산 중단(호출측이 재연결). DESIGN §2.4는 TxId 불일치만 폐기를 정의하므로 그 외 규격 위반은 가장 보수적으로 처리.
- [결정] RTU 조립기는 응답 FC(마스크 후)가 요청 FC와 다르면 길이 예측이 불가능하므로 Invalid 상태 반환(트랜스포트가 에러 처리). 타임아웃 판정은 M3 트랜스포트 책임으로 분리.
- 다음 세션 참고: M3는 ITransport(Tcp/Serial/RtuOverTcp) + ModbusMaster(Channel 큐 직렬화, ModbusResult<T>, Timeout/CrcMismatch만 재시도, Traffic 이벤트). MbapAssembler.Reset()과 RtuResponseAssembler(요청당 1개 생성)를 그대로 사용. TxId 불일치 응답은 폐기 후 계속 대기.

## M3 — 트랜스포트 + ModbusMaster (재시도·타임아웃) · 2026-07-20 20:12
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Transport/ITransport.cs (ModbusFramingMode, 바이트 스트림 인터페이스)
  - src/Nmw.Core/Transport/TcpTransport.cs (TcpTransportBase + TcpTransport(MBAP) + RtuOverTcpTransport(RTU))
  - src/Nmw.Core/Transport/SerialTransport.cs (System.IO.Ports, 3.5 char time 프레임간 지연 계산)
  - src/Nmw.Core/Client/ModbusResult.cs, TrafficEvent.cs, ModbusMasterOptions.cs, ModbusMaster.cs
  - src/Nmw.Core/Protocol/ModbusError.cs (TransportClosed에 detail 파라미터 추가)
  - src/Nmw.Core/Nmw.Core.csproj (System.IO.Ports 8.0.0 추가)
  - tests/Nmw.Core.Tests/FakeTransport.cs, ModbusMasterTests.cs (17케이스)
- 테스트: 83/83 passed (dotnet build 경고 0, 에러 0)
- [결정] ITransport는 원시 바이트 스트림만 담당, 프레이밍/재시도는 ModbusMaster에 집중 — RTU/MBAP 로직을 한 곳에서 테스트하기 위함. 페이크 트랜스포트로 M3 DoD(페이크 스트림 테스트) 충족.
- [결정] 호출자 CancellationToken 취소/마스터 dispose 시 예외 대신 Fail(TransportClosed) 반환 — Result 모델 일관성 유지(폴링 루프가 통계로 축적).
- [결정] F-01의 자동 재접속은 트랜스포트가 아니라 앱 계층(M6 채널 서비스)에서 구현하기로 함 — 마스터는 연결 끊김을 TransportClosed로 보고만 한다.
- [결정] MBAP RX 트래픽 로그는 조립된 프레임을 ADU로 재구성해 발행(부분 수신 청크 단위가 아니라 프레임 단위). TxId 불일치로 폐기되는 프레임도 로그에는 남긴다(진단 목적).
- [결정] 테스트 기대값(FC16 ADU Length 필드) 계산 실수 1회 → 구현이 아니라 테스트 수정 (Length = UnitId 1 + PDU 10 = 0x0B).
- 다음 세션 참고: M4는 tests/Nmw.Integration.Tests + TestSlave(TCP). TestSlave는 §7.6 시나리오용 응답 지연 주입·TxId 오염 주입 기능이 필요. ModbusMaster + TcpTransport 실제 소켓 왕복으로 FC01~06,15,16 + exception 검증.

## M4 — 테스트 슬레이브(TCP) + 통합 테스트 · 2026-07-20 20:17
- 상태: ✅ 완료
- 생성/수정 파일:
  - tests/Nmw.Integration.Tests/Nmw.Integration.Tests.csproj, GlobalUsings.cs
  - tests/Nmw.Integration.Tests/TestSlave/TestSlave.cs (FC01~06,15,16 인메모리 슬레이브 + 지연/TxId 오염 주입)
  - tests/Nmw.Integration.Tests/RoundTripTests.cs (§7.6 시나리오 1~8 + 추가 왕복)
  - src/Nmw.Core/Client/ModbusMaster.cs (수신 대기 방식 수정 — 아래 결정 참조)
- 테스트: 93/93 passed (Core 83 + Integration 10, dotnet build 경고 0, 에러 0)
- [결정] **중요**: .NET에서 소켓 ReceiveAsync를 CancellationToken으로 취소하면 소켓이 abort되어 타임아웃 후 재시도가 불가능함을 통합 테스트(시나리오 7)에서 발견. 타임아웃을 소켓 취소 대신 Task.WhenAny(수신, Task.Delay) 대기로 변경하고, 미완료 수신 태스크(_pendingReceive)는 보존해 다음 시도에서 이어받도록 수정. Dispose 시 잔여 태스크 예외는 관찰 처리.
- [결정] 시나리오 7 검증에서 슬레이브의 요청 카운트는 지연 응답이 끝난 뒤에야 2가 되므로 폴링 대기(최대 3초) 후 단언하도록 테스트 작성.
- [결정] TestSlave 맵 크기는 100(주소 0..99)으로 고정 — §7.6-6 "미구현 주소" 시나리오는 주소 500 읽기로 Exception(0x02) 확인.
- 다음 세션 참고: M5는 RegisterFormatter(골든 §7.5 — Float32 워드오더 4종, S32, S16, Double64) + PollEngine(PeriodicTimer, 백프레셔 skip, PollStats/PollSnapshot 불변 발행). 폴링 테스트는 TestSlave 또는 FakeTransport 상대로 짧은 scan rate 사용.

## M5 — 폴링 엔진 + 통계 + 데이터 포맷 변환기 · 2026-07-20 20:24
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Data/RegisterFormatter.cs (RegisterFormat/WordOrder + 변환·표시 문자열)
  - src/Nmw.Core/Polling/PollDefinition.cs, PollSnapshot.cs (PollStats 포함), PollEngine.cs
  - tests/Nmw.Core.Tests/RegisterFormatterTests.cs (골든 §7.5 7종 + 확장 검증)
  - tests/Nmw.Core.Tests/PollEngineTests.cs (7케이스), FakeTransport.cs (ReceiveDelay 추가)
- 테스트: 126/126 passed (Core 116 + Integration 10, dotnet build 경고 0, 에러 0)
- [결정] 워드오더 일반화: CDAB/DCBA=워드 역순, BADC/DCBA=워드 내 바이트 스왑으로 정의하면 32bit 골든 4종을 모두 만족하며 64bit(4워드)로 자연 확장됨. Double64 CDAB 확장은 "4워드 전체 역순"으로 구현하고 테스트로 고정.
- [결정] PollSnapshot은 Registers(ushort[])/Bits(bool[]) 분리 — DESIGN §3의 "ushort[] 또는 bool[]" 원본 규칙을 타입으로 표현. ASCII 비인쇄 문자(0x20~0x7E 외)는 '.'로 표시.
- [결정] 백프레셔는 루프 내 await(요청 중 다음 틱 시작 불가) + PeriodicTimer(틱 누적 최대 1개) 조합으로 구현 — DESIGN §3 "이전 요청이 끝나지 않았으면 skip" 충족. 느린 응답(80ms) + 10ms 주기 테스트로 폭주 없음 검증.
- [결정] 폴 통계 TxCount는 폴 수준 요청 수(마스터 내부 재시도 미포함). 채널 레벨 재시도 트래픽은 M8 트래픽 로그에서 확인 가능.
- 다음 세션 참고: M6는 Nmw.App(Avalonia 11 Fluent) 신설 — 연결 다이얼로그(TCP/RTU/RTU over TCP), 폴 그리드 1개, 시작/정지. CommunityToolkit.Mvvm만 사용. 스모크는 TestSlave 백그라운드 + 앱 실행으로 수행하고 Avalonia.Headless.XUnit 도입 검토(허용 목록 Avalonia.* 내).

## M6 — Avalonia UI 셸 (연결·폴 그리드·시작/정지) · 2026-07-20 20:34
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.App/Nmw.App.csproj (Avalonia 11.1.4 + CommunityToolkit.Mvvm 8.3.2)
  - src/Nmw.App/Program.cs, App.axaml(.cs), Strings.cs (한국어 문자열 상수 분리)
  - src/Nmw.App/Models/ConnectionParameters.cs (TCP/RTU/RTU over TCP + 타임아웃/재시도/프레임간지연)
  - src/Nmw.App/ViewModels/MainWindowViewModel.cs, RegisterRowViewModel.cs
  - src/Nmw.App/Views/MainWindow.axaml(.cs), ConnectionDialog.axaml(.cs)
  - tests/Nmw.App.Tests/ (Avalonia.Headless.XUnit 기반 스모크 3케이스)
- 테스트: 129/129 passed (Core 116 + Integration 10 + App 3, dotnet build 경고 0, 에러 0)
- 스모크 수행 내용:
  1. 헤드리스 스모크(자동화): TestSlave 실제 TCP 기동 → 앱 윈도우 Show → 연결 적용 → FC03 10개/50ms 폴 시작 → 그리드 값(0,50,90) 및 Tx 통계 갱신 확인 → 정지 → 해제. 미연결 폴 시작 시 오류 표시, 범위 밖 주소 폴링 시 "Illegal Data Address" 표시 확인.
  2. 실제 실행: `dotnet run --project src/Nmw.App`를 백그라운드로 12초 실행 → 크래시 없이 유지(APP_ALIVE) 확인 후 종료. 프로세스 잔류 없음.
- [결정] 그리드 갱신은 행 뷰모델 재사용(Value 문자열만 변경) — DESIGN §6 "관찰 가능 컬렉션 재생성 금지" 준수. 스냅샷은 Dispatcher.UIThread.Post로 마샬링.
- [결정] 연결 다이얼로그는 코드비하인드에서 값 검증 후 ConnectionParameters 반환(MVVM 다이얼로그 서비스는 M6 범위에 과함). VM은 ApplyConnectionAsync(파라미터)로 다이얼로그와 분리되어 헤드리스 테스트 가능.
- [결정] 컴파일드 바인딩은 DataGrid 칼럼 바인딩 호환성 문제를 피하려 x:CompileBindings="False"로 비활성(리플렉션 바인딩 사용).
- [결정] ASCII 포맷은 그리드 1행(전체 문자열)으로 표시. 32/64bit 포맷은 개수의 나머지 레지스터를 행에서 제외.
- [미정] F-01 자동 재접속은 아직 미구현(연결 끊김은 TransportClosed 오류로만 표시) — M8~M9 사이 채널 서비스에 추가 예정.
- 다음 세션 참고: M7은 쓰기 다이얼로그(FC05/06/15/16, Master 직접 호출) + 주소 0/1-base 토글(AddressBase 표시 변환) + 별칭 컬럼. 폴 그리드 더블클릭 프리필(F-15)도 이때 함께. AddressBase 변환 로직은 Nmw.Core/Data에 두고 단위 테스트 작성.

## M7 — 쓰기 다이얼로그 + 주소 base 토글 + 별칭 · 2026-07-20 20:32
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Data/AddressNotation.cs (AddressBase/AddressArea + Format/TryParse, 5·6자리 PLC 표기)
  - src/Nmw.App/Models/WriteInputParser.cs (10진/0x16진, 콤마·공백 목록 파서)
  - src/Nmw.App/Views/WriteDialog.axaml(.cs) (FC05/06/15/16 전송 + 성공(ms)/exception 명칭 표시)
  - src/Nmw.App/Views/MainWindow.axaml(.cs) (쓰기 버튼, PLC 주소 토글, 별칭 컬럼, 그리드 더블클릭 프리필)
  - src/Nmw.App/ViewModels/MainWindowViewModel.cs, RegisterRowViewModel.cs (별칭 dict, raw 값 추적, base 재표기)
  - tests/Nmw.Core.Tests/AddressNotationTests.cs, tests/Nmw.App.Tests/WriteInputParserTests.cs, WriteAndAddressSmokeTests.cs
- 테스트: 182/182 passed (Core 145 + Integration 10 + App 27, dotnet build 경고 0, 에러 0)
- 스모크 수행 내용: 헤드리스로 (1) FC06 0xBEEF 쓰기 → 폴 그리드 48879 반영, (2) FC16 [1,2,3] 쓰기 → 슬레이브 맵 확인 + 범위 밖 쓰기 "Illegal Data Address", (3) 0↔1-base 토글 시 40001 표기/역파싱(40010→프로토콜 9) 확인, (4) 별칭 편집이 폴 재시작 후 유지. 실제 앱 10초 실행 크래시 없음(APP_ALIVE).
- [결정] OneBase 입력에서 5자리 미만 숫자는 프리픽스 없는 1-base로 해석(Modbus Poll 관행). 5자리 이상은 영역 프리픽스 검증(불일치 시 파싱 실패 = 보수적).
- [결정] 쓰기 다이얼로그 값 입력은 FC별 단일 텍스트박스 + 힌트로 통일 (FC05: 1/0, FC06: 단일값, FC15/16: 목록). 별칭은 VM 딕셔너리(프로토콜 주소 키)에 저장해 폴 재시작에도 유지, M9 워크스페이스 저장 대상.
- 다음 세션 참고: M8은 트래픽 로그 뷰 — ModbusMaster.Traffic 구독, 링버퍼(기본 5000라인) + 에러만 필터 + 클리어 + .txt 저장. 링버퍼는 Core에 TrafficLogBuffer로 두고 단위 테스트. UI는 하단 접이식 패널 + 가상화 리스트.

## M8 — 트래픽 로그 뷰 + 파일 저장 + 에러 표시 + 로그 제한 · 2026-07-20 20:36
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Client/TrafficLog.cs (TrafficLogEntry/TrafficLogKind + TrafficLogBuffer 링버퍼, hex 포매터)
  - src/Nmw.App/ViewModels/TrafficLogViewModel.cs (미러 컬렉션, 에러만 필터, 클리어, .txt 저장)
  - src/Nmw.App/ViewModels/MainWindowViewModel.cs (Traffic 이벤트 구독, 폴 오류 → ERR 라인)
  - src/Nmw.App/Views/MainWindow.axaml(.cs) (접이식 트래픽 로그 패널 + StorageProvider 저장)
  - tests/Nmw.Core.Tests/TrafficLogBufferTests.cs (8케이스), tests/Nmw.App.Tests/TrafficLogSmokeTests.cs (4케이스)
- 테스트: 194/194 passed (Core 153 + Integration 10 + App 31, dotnet build 경고 0, 에러 0)
- 스모크 수행 내용: 헤드리스로 (1) 폴링 중 TX/RX hex 라인(요청 PDU "03 00 00 00 02" 포함) 표시, (2) 범위 밖 주소 폴링 시 "ERR Illegal Data Address" 라인 + 에러만 필터 + 클리어, (3) MemoryStream 저장 내용 검증, (4) 용량 10 링버퍼에서 30라인 추가 시 10라인 유지. 실제 앱 10초 실행 크래시 없음(APP_ALIVE).
- [결정] 로그 링버퍼(기본 5000)는 Nmw.Core에 두고(24시간 폴링 메모리 상한, PRD 비기능 요구), 오류 엔트리 수를 오버플로 드랍까지 반영해 유지. UI 미러 컬렉션은 버퍼 Add가 반환하는 드랍 엔트리로 동기화.
- [결정] ERR 라인은 폴 스냅샷의 LastError 발생 시마다 1줄 기록(마스터 재시도 프레임은 TX/RX 라인으로 자연 기록됨). 저장 시 현재 필터(에러만) 기준으로 기록.
- 다음 세션 참고: M9는 멀티 폴 탭 + 워크스페이스(.nmw) 저장/로드. WorkspaceDocument(schemaVersion=1, channels/polls/ui)를 Nmw.Core/Workspace에 System.Text.Json으로 구현하고 라운드트립 테스트. UI는 TabControl로 폴 N개(엔진은 이미 멀티 폴 지원). 부분 손상 시 통째 실패 규칙 준수.

## M9 — 멀티 폴 탭 + 워크스페이스 저장/로드(.nmw) · 2026-07-20 20:44
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Workspace/WorkspaceDocument.cs (ChannelConfig/PollConfig/UiConfig, schemaVersion=1)
  - src/Nmw.Core/Workspace/WorkspaceSerializer.cs (System.Text.Json, 통째 실패, 한글 이스케이프 없음)
  - src/Nmw.App/ViewModels/PollViewModel.cs (탭 단위 폴: 설정+그리드+통계+별칭), PollOptions.cs
  - src/Nmw.App/ViewModels/MainWindowViewModel.cs (멀티 폴 관리, 폴 추가/삭제, 워크스페이스 저장/로드+자동연결)
  - src/Nmw.App/Models/WorkspaceMapping.cs (ConnectionParameters ↔ ChannelConfig)
  - src/Nmw.App/Views/MainWindow.axaml(.cs) (TabControl 폴 탭, 열기/저장 .nmw 파일 피커)
  - tests/Nmw.Core.Tests/WorkspaceSerializerTests.cs (라운드트립, DESIGN §8 예시 JSON, 스키마/손상 실패)
  - tests/Nmw.App.Tests/WorkspaceSmokeTests.cs + 기존 스모크 3파일 폴 탭 구조로 갱신
- 테스트: 206/206 passed (Core 161 + Integration 10 + App 35, dotnet build 경고 0, 에러 0)
- 스모크 수행 내용: 헤드리스로 (1) 동일 채널 공유 폴 탭 2개 동시 폴링(주소 0/50 값 각각 갱신), (2) 저장→로드 라운드트립: 연결설정+폴 2개+별칭("급수온도")+1-base 표기 복원, 자동 재연결 후 폴 재시작·값 확인, (3) 손상 JSON/미지원 스키마 로드 시 통째 실패+기존 상태 유지+명확한 메시지, (4) 마지막 폴 1개 삭제 방지. 실제 앱 10초 실행 크래시 없음(APP_ALIVE).
- [결정] 워크스페이스 JSON에서 한글이 \uXXXX로 이스케이프되던 문제를 UnsafeRelaxedJsonEscaping 인코더로 수정 (파일 인코딩 UTF-8 원칙과 일치, 현장에서 사람이 읽을 수 있는 파일).
- [결정] **버그 수정**: 주소 base 토글 시 폴의 시작주소 입력 텍스트가 이전 표기로 남아 재해석되는 문제 발견(테스트가 잡음) → 토글 시 입력값도 새 표기로 변환(ConvertStartAddressBase).
- [결정] 워크스페이스 로드 시 채널이 있으면 자동 연결 시도(실패해도 폴 정의는 복원). 로드는 부분 적용 없이 파싱 성공 후에만 상태 변경(통째 실패 규칙).
- 다음 세션 참고: M10은 win-x64 self-contained single-file publish 1회 검증(크기 기록, ≤90MB 확인) 후 publish/ 삭제, README(빌드/publish/사용법) + 스모크 체크리스트 문서 작성. DESIGN §10 명령 사용.

## M10 — win-x64 publish 검증 + README + 스모크 체크리스트 · 2026-07-20 20:46
- 상태: ✅ 완료
- 생성/수정 파일:
  - README.md (기능 요약, 구조, 빌드/테스트/실행, publish 명령·산출물 크기, 문서 링크)
  - SMOKE_CHECKLIST.md (배포 전 Windows 실기 수용 체크리스트 — PRD F-01~F-12 기준 7개 섹션)
- publish 검증: DESIGN §10 명령으로 macOS에서 win-x64 크로스 publish 1회 수행 성공.
  산출물 `publish/win-x64/Nmw.App.exe` = **45,344,736 bytes (약 43.2MB)** — 기준 90MB 이내 충족.
  확인 후 `publish/` 폴더 삭제 완료 (배포 exe는 Windows PC에서 재생성).
- 테스트: 206/206 passed (Core 161 + Integration 10 + App 35, dotnet build 경고 0, 에러 0)
- [결정] pdb/xml 문서 파일이 publish 폴더에 포함되나 단일 exe 실행에는 불필요 — 배포 시 exe만 복사하면 됨을 README에 명시(산출물 경로로 안내).
- [미정] (사람 확인 필요) ① 실제 Windows PC + .NET 미설치 환경에서 exe 실행 확인, ② USB-RS485 실물 장비 대상 RTU 시리얼 테스트, ③ F-01 자동 재접속은 미구현 상태(연결 끊김은 오류로 표시) — 필요 시 후속 사이클(P2)에서 채널 서비스로 추가.
- 다음 세션 참고: M1~M10 전체 완료. 남은 개선 후보: 자동 재접속, RTU 실기 검증, P2 항목(CSV 내보내기, 값 변화 하이라이트, 시뮬레이터 모드).

## S1 — 내장 슬레이브 시뮬레이터 (장비 없는 테스트, P2 조기 구현) · 2026-07-20 21:02
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Simulator/ModbusTcpSimulator.cs (FC01~06,15,16 완전 지원 인메모리 슬레이브: 루프백 바인딩, 다중 클라이언트, 모든 UnitId 응답, 스레드 안전 데이터 영역 4종, 범위 밖=0x02·잘못된 개수/값=0x03, 요청/클라이언트 카운트, DataChangedByMaster 이벤트, IncrementRegisters)
  - src/Nmw.App/ViewModels/SimulatorViewModel.cs (시작/정지, 포트·표시 개수 설정, 값 편집 그리드 4탭, 값 자동 변화, 500ms 갱신 타이머)
  - src/Nmw.App/Views/SimulatorWindow.axaml(.cs) (비모달 창), MainWindow에 [시뮬레이터...] 버튼
  - tests/Nmw.Integration.Tests/SimulatorTests.cs (7케이스: 전 FC 왕복, exception, 동시 2클라이언트, 쓰기 이벤트, 재시작, 임의 UnitId)
  - tests/Nmw.App.Tests/SimulatorSmokeTests.cs (3케이스: 앱 단독 전체 루프, 자동 변화, 포트 오류)
  - README.md ("장비 없이 테스트하기" 섹션), SMOKE_CHECKLIST.md (7장 시뮬레이터 체크 + 장비 없이 수행 가능 명시)
- 테스트: 216/216 passed (Core 161 + Integration 17 + App 38, dotnet build 경고 0, 에러 0)
- 스모크 수행 내용: 헤드리스로 앱 단독 전체 루프 검증 — 시뮬레이터 시작(임시 포트) → 그리드에서 값 편집(홀딩 123, 코일 ON) → 마스터 127.0.0.1 연결 → 폴 그리드 값 확인 → FC06 쓰기 456 → 시뮬레이터 그리드·폴 그리드 동시 반영 → 코일 폴 탭 추가 확인 → 정지. 자동 변화 ON 시 폴 값 연속 증가 확인. 실제 앱 10초 실행 크래시 없음(APP_ALIVE).
- [결정] 시뮬레이터는 PRD P2 "간이 슬레이브 시뮬레이터"를 조기 구현한 것 (사용자 요청: 물리 장비 없이 확실한 테스트 수단 필요). 테스트 전용 TestSlave(지연/TxId 오염 주입)는 별도 유지 — 프로덕션 시뮬레이터에 결함 주입 훅을 넣지 않기 위함.
- [결정] 기본 포트 1502 (Windows에서 502는 관리자 권한 필요), 127.0.0.1 전용 바인딩(방화벽 경고 없음), 영역 크기 기본 1000 (범위 밖 주소로 exception 표시 테스트 가능).
- 다음 세션 참고: 남은 개선 후보는 자동 재접속(F-01), RTU 실기 검증, CSV 내보내기 등 P2 항목.

## S2 — RTU(시리얼) 슬레이브 시뮬레이터 추가 · 2026-07-21 08:12
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.Core/Simulator/SimulatorDataStore.cs (신설 — 데이터 영역 + ProcessPdu를 TCP 시뮬레이터에서 추출, TCP/RTU 공유)
  - src/Nmw.Core/Simulator/ModbusTcpSimulator.cs (공용 저장소 사용으로 리팩터링, 공개 API 유지)
  - src/Nmw.Core/Framing/RtuRequestAssembler.cs (신설 — 슬레이브 측 요청 길이 예측: FC01~06=8바이트 고정, FC15/16=byteCount 후 9+n 확정, CRC 검증, Reset 재사용)
  - src/Nmw.Core/Simulator/ModbusRtuSlaveSimulator.cs (신설 — RtuSlaveEngine(스트림 기반, 테스트 가능) + SerialPort 래퍼. CRC 오류 요청은 실슬레이브처럼 무응답 폐기)
  - src/Nmw.App/ViewModels/SimulatorViewModel.cs (모드 선택 TCP/RTU, 시리얼 설정, 공용 저장소 — 재시작해도 값 유지)
  - src/Nmw.App/Views/SimulatorWindow.axaml (모드 콤보 + 시리얼 패널 + com0com 안내), HelpWindow(⑤ 항목), README, SMOKE_CHECKLIST
  - tests/Nmw.Core.Tests/RtuRequestAssemblerTests.cs (8케이스, §7.1 골든 요청 프레임)
  - tests/Nmw.Integration.Tests/RtuSlaveEngineTests.cs (6케이스 — 인메모리 duplex 파이프로 FC03 골든 왕복, FC06/16 쓰기, CRC 오염 무응답+재동기화, 연속 2요청, 범위 밖 exception 프레임)
  - tests/Nmw.App.Tests/SimulatorSmokeTests.cs (RTU 모드 검증 + 저장소 유지 2케이스 추가)
- 테스트: 234/234 passed (Core 169 + Integration 23 + App 42, dotnet build 경고 0, 에러 0)
- [결정] RTU 슬레이브 처리부(RtuSlaveEngine)를 SerialPort와 분리해 스트림 기반으로 구현 — macOS/CI에 시리얼 하드웨어가 없어도 인메모리 duplex 파이프로 프레이밍·처리·재동기화를 완전 검증하기 위함. 실포트 검증은 Windows + com0com 실기 체크리스트 항목으로 남김.
- [결정] CRC 불일치/미지원 FC 요청은 실제 RTU 슬레이브 동작대로 무응답 폐기 후 재동기화(Reset). 시뮬레이터 데이터 저장소는 TCP/RTU가 공유하며 시뮬레이터 재시작에도 값이 유지된다.
- [미정] (사람 확인) Windows에서 com0com 가상 포트 쌍 대상 실기 스모크 — 체크리스트 7장에 추가됨.
- 다음 세션 참고: 남은 개선 후보는 자동 재접속(F-01), CSV 내보내기 등 P2 항목.

## S3 — UI 전면 개편 (Crextio 레퍼런스 스타일) · 2026-07-21 08:28
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.App/App.axaml (전면 재작성 — 팔레트/그라데이션/전역 스타일: 웜 그레이→소프트 옐로 대각 그라데이션 배경, 흰색 라운드 카드(BoxShadow), 필 버튼, 차콜 잉크 accent 버튼, 옐로 체크 토글, 다크 필 탭, 라벨-위 필드 스타일, Inter 폰트 전역 적용, SystemAccentColor=옐로)
  - src/Nmw.App/Views/MainWindow.axaml (상단 바 카드+로고 필, 폴 탭 카드, 설정영역 소프트 카드+필드 라벨 상단 배치, 통계 잉크 칩, 트래픽 로그 카드)
  - src/Nmw.App/Views/ConnectionDialog.axaml, WriteDialog.axaml, SimulatorWindow.axaml, HelpWindow.axaml (카드 래핑 + 스타일 클래스 적용, 컨트롤/핸들러/바인딩 무변경)
  - tests/Nmw.App.Tests/TestAppBuilder.cs (Inter 글리프 생성을 위해 헤드리스 드로잉 → Skia 렌더러 전환)
- 테스트: 234/234 passed (dotnet build 경고 0, 에러 0). 기능 코드(ViewModel/Core) 무변경 — 기존 스모크 전체 통과로 기능 보존 확인.
- 검증: 헤드리스 Skia 렌더 캡처로 메인/연결/쓰기/시뮬레이터/도움말 5개 창 PNG 확인 — 그라데이션 배경, 카드, 필 버튼, 다크 필 탭이 레퍼런스와 일치. 실제 앱 실행 크래시 없음(APP_ALIVE).
- [결정] 다크/라이트 자동 전환(기존 Fluent 기본)을 제거하고 RequestedThemeVariant=Light 고정 — 레퍼런스 팔레트가 라이트 전용이라 다크 모드에서 색 대비가 깨지기 때문. 필요 시 다크 팔레트는 후속 작업.
- [결정] Fluent 내장 Button.accent 스타일(SystemAccentColor)이 커스텀 잉크 버튼을 덮는 문제 발견(스냅샷 검증에서 확인) → 템플릿 프레젠터 레벨 스타일로 오버라이드.
- 다음 세션 참고: 남은 개선 후보 동일 (자동 재접속, CSV 내보내기 등).

## S4 — 메뉴 버튼 픽토그램 추가 · 2026-07-21 09:08
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.App/App.axaml (StreamGeometry 아이콘 17종 + PathIcon 스타일: 기본 잉크, accent 버튼 내 흰색, 토글 체크 시 잉크)
  - src/Nmw.App/Views/MainWindow.axaml (툴바 10개 버튼 아이콘+라벨: 연결/해제/쓰기/PLC 주소/폴 추가·삭제/열기/저장/시뮬레이터/도움말, 트래픽 로그 3버튼, 폴 시작/정지. 툴바 WrapPanel 전환 + 창 폭 1280 확대)
  - src/Nmw.App/Views/ConnectionDialog.axaml, WriteDialog.axaml, SimulatorWindow.axaml, HelpWindow.axaml (취소/연결/닫기/전송/시작·정지/값 자동 변화 버튼 아이콘)
- 테스트: 234/234 passed (dotnet build 경고 0, 에러 0). 헤드리스 렌더 캡처로 아이콘 표시·툴바 한 줄 배치 확인, 실제 앱 실행 크래시 없음.
- [결정] 아이콘은 이모지/비트맵 대신 Material 계열 24x24 벡터 지오메트리(StreamGeometry + PathIcon) — 레퍼런스의 모노톤 미니멀 스타일 유지, 배경색에 따라(잉크/흰색) 자동 전환. WrapPanel의 ItemSpacing은 Avalonia 11.1에 없어 자식 Margin 스타일로 간격 처리.

## S5 — NEXYS 브랜딩 적용 (와인 레드 팔레트 + 로고 + 앱 아이콘) + 도움말 FC 대응표 · 2026-07-21 10:03
- 상태: ✅ 완료
- 생성/수정 파일:
  - src/Nmw.App/App.axaml (포인트 컬러 옐로 → NEXYS 웹사이트 와인 레드 #7A1020(--red)·#9C2030(--red-bright)·#5A0B18(--red-deep). 그라데이션 방향/구조 유지, 끝색만 로즈 틴트 #EFCEC7. 체크 토글/pressed 전경 흰색으로 조정)
  - src/Nmw.App/Assets/ (nexys-logo.png — 제공 로고, favicon-nxs.png — 웹사이트 파비콘, app.ico — 파비콘 256px PNG-in-ICO 변환)
  - src/Nmw.App/Nmw.App.csproj (AvaloniaResource Assets + ApplicationIcon → exe 아이콘)
  - src/Nmw.App/Views/MainWindow.axaml (타이틀 필 "Nexys Modbus"→"Modbus Workbench", 하단 우측 NEXYS 로고(h14) + "© 2026 NEXYS Co., Ltd. All rights reserved.", Window Icon)
  - 전체 윈도우 5종에 Icon="/Assets/app.ico" (타이틀바/작업표시줄 아이콘 = 웹사이트 파비콘)
  - src/Nmw.App/Views/HelpWindow.axaml (6장에 FC↔PLC 주소 프리픽스 대응표 추가 — FC03↔4xxxx/FC04↔3xxxx 엇갈림 설명)
- 테스트: 234/234 passed (경고 0). 헤드리스 렌더 캡처로 와인 그라데이션/로고/저작권/타이틀 확인, 실행 크래시 없음.
- [결정] 웹사이트 CSS 변수에서 포인트 컬러 추출(--red #7A1020). app.ico는 외부 도구 없이 PNG-in-ICO(Vista+) 형식으로 생성.
