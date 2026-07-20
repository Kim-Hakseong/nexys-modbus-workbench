# DESIGN.md — Nexys Modbus Workbench 설계서

## 1. 솔루션 구조

```
NexysModbusWorkbench.sln
├─ src/
│  ├─ Nmw.Core/            # 프로토콜 코어 (UI 의존 0, 순수 .NET)
│  │  ├─ Protocol/         #   PDU 빌더/파서, function code 정의, exception
│  │  ├─ Framing/          #   Crc16, MbapFraming, RtuFraming
│  │  ├─ Transport/        #   ITransport, TcpTransport, SerialTransport, RtuOverTcpTransport
│  │  ├─ Client/           #   ModbusMaster (요청 직렬화 큐, 타임아웃/재시도)
│  │  ├─ Polling/          #   PollDefinition, PollEngine, PollStats, PollSnapshot
│  │  ├─ Data/             #   RegisterFormatter (포맷/워드오더 변환), AddressBase
│  │  └─ Workspace/        #   WorkspaceDocument (JSON 직렬화 모델)
│  └─ Nmw.App/             # Avalonia UI
│     ├─ ViewModels/       #   CommunityToolkit.Mvvm 기반
│     ├─ Views/            #   MainWindow, ConnectionDialog, WriteDialog, PollView, TrafficLogView
│     └─ Services/         #   UiDispatcher, FileDialogService
└─ tests/
   ├─ Nmw.Core.Tests/      # 골든 벡터 + 단위 테스트
   └─ Nmw.Integration.Tests/  # TestSlave(TCP) 상대 왕복 테스트
      └─ TestSlave/        #   인메모리 레지스터맵 Modbus TCP 슬레이브 (테스트 전용)
```

의존 방향: `Nmw.App → Nmw.Core`. 역방향 금지.

## 2. 프로토콜 코어

### 2.1 지원 Function Code

| FC | 이름 | 요청 PDU | 정상 응답 PDU |
|---|---|---|---|
| 0x01 | Read Coils | FC, addr(2), qty(2) [qty 1..2000] | FC, byteCount(1), coil bytes |
| 0x02 | Read Discrete Inputs | 동일 | 동일 |
| 0x03 | Read Holding Registers | FC, addr(2), qty(2) [qty 1..125] | FC, byteCount(1), reg×2 bytes |
| 0x04 | Read Input Registers | 동일 | 동일 |
| 0x05 | Write Single Coil | FC, addr(2), value(2: FF00/0000) | 요청 에코 |
| 0x06 | Write Single Register | FC, addr(2), value(2) | 요청 에코 |
| 0x0F | Write Multiple Coils | FC, addr(2), qty(2), byteCount(1), data | FC, addr(2), qty(2) |
| 0x10 | Write Multiple Registers | FC, addr(2), qty(2), byteCount(1), data | FC, addr(2), qty(2) |

모든 멀티바이트 필드는 **big-endian**. 응답 FC의 MSB(0x80)가 설정되면 exception 응답이며 다음 1바이트가 exception code.

### 2.2 Exception Code 매핑 (UI 표시 문자열)

| 코드 | 명칭 |
|---|---|
| 0x01 | Illegal Function |
| 0x02 | Illegal Data Address |
| 0x03 | Illegal Data Value |
| 0x04 | Slave Device Failure |
| 0x05 | Acknowledge |
| 0x06 | Slave Device Busy |
| 0x08 | Memory Parity Error |
| 0x0A | Gateway Path Unavailable |
| 0x0B | Gateway Target Failed to Respond |

### 2.3 CRC16 (RTU)

- 다항식 0xA001(reflected 0x8005), 초기값 0xFFFF, XOR out 없음.
- 프레임 끝에 **low byte 먼저** 붙인다.
- 256-entry 룩업 테이블로 구현 (정적 초기화).

### 2.4 MBAP (TCP)

`TransactionId(2) | ProtocolId(2)=0x0000 | Length(2) | UnitId(1) | PDU`
- Length = UnitId(1) + PDU 길이.
- TransactionId는 요청마다 증가하는 ushort. 응답 수신 시 TransactionId 불일치면 해당 응답 폐기 후 계속 대기(타임아웃까지).
- TCP 스트림은 부분 수신 가능 → 헤더 7바이트 완독 후 Length 만큼 추가 완독하는 상태머신으로 조립.

### 2.5 RTU 프레이밍/수신 전략

- 요청: `UnitId(1) | PDU | CRC(2)`.
- 수신은 문자간 침묵(3.5 char) 하드웨어 검출 대신 **길이 예측 + 타임아웃** 방식:
  1. 최소 헤더(UnitId+FC) 수신 → FC로 기대 길이 계산 (읽기 응답은 byteCount 수신 후 확정, 에코형은 고정 길이, exception은 5바이트)
  2. 기대 길이 도달 시 CRC 검증
  3. 응답 타임아웃 내 미도달 → TimeoutError
- 요청 간 최소 지연(inter-frame delay, 기본 자동 = 3.5 char time을 보레이트로 계산, 수동 override 가능)을 두어 저가 컨버터 호환성 확보.
- 시리얼 채널은 **동시에 1개 요청만** 진행 (요청 큐 직렬화). TCP도 단순화를 위해 채널당 직렬화 (Modbus Poll과 동일한 동작).

### 2.6 ModbusMaster (Client)

```csharp
public sealed class ModbusMaster : IAsyncDisposable
{
    // 요청은 Channel<PendingRequest> 큐로 직렬화. 워커 태스크 1개가 소비.
    Task<ModbusResult<bool[]>>   ReadCoilsAsync(byte unitId, ushort addr, ushort qty, CancellationToken ct);
    Task<ModbusResult<ushort[]>> ReadHoldingRegistersAsync(...);
    Task<ModbusResult<Unit>>     WriteSingleRegisterAsync(...);
    // ... FC별 메서드
    event EventHandler<TrafficEvent> Traffic;   // 방향, raw bytes, timestamp — 로그뷰가 구독
}
```

- `ModbusResult<T>`: `Ok(T value, TimeSpan elapsed)` | `Fail(ModbusError error)`.
- `ModbusError`: `Timeout | CrcMismatch | Exception(code) | TransportClosed | InvalidResponse`.
- 재시도: 설정 횟수만큼 Timeout/CrcMismatch에 한해 재시도. Exception 응답은 재시도하지 않음(정상 프로토콜 응답임).
- 예외를 던지지 않고 Result로 반환 — 폴링 루프가 에러를 통계로 축적하기 위함.

## 3. 폴링 엔진

- `PollDefinition { Id, Name, UnitId, Function, StartAddress, Quantity, ScanRateMs, Format, WordOrder, Dictionary<ushort,string> Aliases }`
- `PollEngine`: 폴마다 독립 async 루프 (`PeriodicTimer`). 실제 요청은 채널의 ModbusMaster 큐로 들어가므로 시리얼에서도 안전.
- 결과는 불변 `PollSnapshot { ushort[] Raw, PollStats Stats, DateTimeOffset At, ModbusError? LastError }`로 발행 (`IObservable` 또는 이벤트). UI는 Dispatcher로 마샬링해 그리드 갱신.
- `PollStats { long TxCount, ValidRx, ErrorCount, double LastResponseMs, string? LastErrorText }` — 스레드 안전하게 엔진 내부에서만 갱신.
- Scan rate 하한 10ms. 이전 요청이 끝나지 않았으면 해당 틱은 skip (백프레셔).

## 4. 데이터 포맷 변환 (RegisterFormatter)

원본은 항상 `ushort[]`(레지스터) 또는 `bool[]`(코일). 표시 시점에 변환.

| 포맷 | 소비 레지스터 | 규칙 |
|---|---|---|
| U16 / S16 | 1 | S16은 2의 보수 |
| Hex16 / Bin16 | 1 | `0x%04X` / 16자리 2진 |
| U32 / S32 / Float32 | 2 | 워드오더 적용 후 big-endian 해석 |
| S64 / Double64 | 4 | 워드오더 적용(2워드 단위 스왑 규칙을 4워드로 확장) |
| ASCII | n | 레지스터당 2문자, 상위 바이트 먼저 |

워드오더 정의 (32bit, 레지스터 r0=AB, r1=CD 수신 기준으로 바이트열 재배열):

| 이름 | 바이트열 | 통칭 |
|---|---|---|
| ABCD | A B C D | Big-endian |
| CDAB | C D A B | Word swap (Modicon 전통) |
| BADC | B A D C | Byte swap |
| DCBA | D C B A | Little-endian |

## 5. 주소 표기 (AddressBase)

- 내부 저장은 항상 **프로토콜 주소(0-base)**.
- UI 토글 시 표시만 변환: PLC 표기 = 영역 프리픽스 + (주소+1). 코일 0xxxx, 접점 1xxxx, 입력레지스터 3xxxx, 홀딩 4xxxx. 예: 프로토콜 주소 0의 홀딩 레지스터 = `40001`.
- 입력 파싱도 동일 규칙 역변환. 5자리/6자리(400001 스타일) 모두 허용.

## 6. UI 설계 (Avalonia)

```
MainWindow
├─ 상단 툴바: [연결/해제] [폴 추가] [쓰기] [주소 0/1-base 토글] [워크스페이스 열기/저장]
├─ 좌측: 채널 트리 (채널 → 폴 목록)  ※ M9에서 탭과 연동
├─ 중앙: TabControl — 폴마다 탭 1개
│   └─ PollView: 상태바(Tx/Ok/Err/응답ms/에러명) + DataGrid(주소 | 별칭 | 값)
├─ 하단: TrafficLogView (토글 접이식)
│   └─ [에러만] [클리어] [저장] + 가상화 리스트 (링버퍼 5000라인)
└─ 상태바: 채널 연결 상태, 마지막 오류
```

- 쓰기 다이얼로그: FC 선택 → 주소/개수/값 그리드 → 전송 → 결과(성공/exception 명칭) 표시. 폴 그리드 셀 더블클릭 시 주소·현재값 프리필.
- 그리드 갱신은 스냅샷 diff 없이 셀 값 문자열 갱신 (10ms 폴에서도 렌더 부담 없도록 관찰 가능 컬렉션 재생성 금지, 항목 재사용).
- 연결 다이얼로그: 모드(TCP / RTU / RTU over TCP) 라디오 → 모드별 파라미터 패널 + 타임아웃/재시도/프레임간지연.

## 7. 골든 테스트 벡터 (수정 금지 — 검증 완료 값)

### 7.1 CRC16 (프레임 → 뒤에 붙는 CRC 바이트 lo, hi)

| 프레임(hex) | CRC lo hi |
|---|---|
| `01 03 00 00 00 0A` | `C5 CD` |
| `11 03 00 6B 00 03` | `76 87` (Modbus 스펙 표준 예제) |
| `01 06 00 01 00 03` | `98 0B` |
| `01 83 02` | `C0 F1` |
| `01 05 00 AC FF 00` | `4C 1B` |
| `11 10 00 01 00 02 04 00 0A 01 02` | `C6 F0` |

### 7.2 PDU 빌드

- FC03, addr=0x006B, qty=3 → PDU `03 00 6B 00 03`
- FC05, addr=0x00AC, ON → PDU `05 00 AC FF 00`
- FC16, addr=0x0001, regs=[0x000A, 0x0102] → PDU `10 00 01 00 02 04 00 0A 01 02`

### 7.3 응답 파싱

- FC03 응답 PDU `03 06 02 2B 00 00 00 64` → regs `[0x022B, 0x0000, 0x0064]`
- FC01 응답 PDU `01 03 CD 6B 05` → coils 19개 요청 시 `CD`=coil0..7(1,0,1,1,0,0,1,1), `6B`=coil8..15, `05`=coil16..18(1,0,1)
- PDU `83 02` → Exception(IllegalDataAddress)

### 7.4 MBAP

- TxId=0x0001, UnitId=0x11, PDU `03 00 6B 00 03` → ADU `00 01 00 00 00 06 11 03 00 6B 00 03`

### 7.5 포맷 변환 (regs 수신값 → 표시값)

| regs | 포맷/오더 | 기대값 |
|---|---|---|
| `[0x4049, 0x0FDB]` | Float32 ABCD | 3.1415927f (float32 pi) |
| `[0x0FDB, 0x4049]` | Float32 CDAB | 3.1415927f |
| `[0x4940, 0xDB0F]` | Float32 BADC | 3.1415927f |
| `[0xDB0F, 0x4940]` | Float32 DCBA | 3.1415927f |
| `[0xFFFE, 0x1DC0]` | S32 ABCD | -123456 |
| `[0xFFF6]` | S16 | -10 |
| `[0x405E, 0xDD2F, 0x1A9F, 0xBE77]` | Double64 ABCD | 123.456 |

### 7.6 통합 테스트 시나리오 (TestSlave 상대)

1. TestSlave 홀딩 0..9 = [0,10,20,...,90] 세팅 → FC03 addr0 qty10 → 값 일치
2. FC06 addr5 = 0xBEEF 쓰기 → FC03 재독 → 0xBEEF
3. FC16 addr0 regs [1,2,3] → 재독 일치
4. FC05 addr3 ON → FC01 재독 → true
5. FC15 addr0 [T,F,T,T] → 재독 일치
6. 미구현 주소 읽기 → Exception(0x02) 반환 확인
7. TestSlave 응답 지연 > 타임아웃 → Timeout 에러 + 재시도 카운트 확인
8. 잘못된 TransactionId 응답 주입 → 폐기 후 정상 응답 수신 확인

## 8. 워크스페이스 파일 (.nmw)

JSON. 스키마 버전 필드 포함.

```json
{
  "schemaVersion": 1,
  "channels": [
    { "id": "ch1", "type": "Tcp", "host": "192.168.0.10", "port": 502,
      "timeoutMs": 1000, "retries": 1, "interFrameDelayMs": 0 }
  ],
  "polls": [
    { "channelId": "ch1", "name": "보일러 온도", "unitId": 1, "function": 3,
      "startAddress": 0, "quantity": 10, "scanRateMs": 500,
      "format": "Float32", "wordOrder": "CDAB",
      "aliases": { "0": "급수온도", "2": "출구온도" } }
  ],
  "ui": { "addressBase": "OneBase" }
}
```

로드 시 schemaVersion 미지원이면 명확한 에러 메시지. 부분 손상 시 통째 실패(부분 로드 금지).

## 9. 스레딩 모델 요약

```
[UI Thread] ── 명령 ──▶ [PollEngine 폴 루프 ×N (Task)]
                              │ 요청 enqueue
                              ▼
                     [ModbusMaster 워커 ×채널당 1]
                              │ ITransport (async I/O)
                              ▼
                        TCP/Serial 장비
스냅샷/트래픽 이벤트 ──▶ Dispatcher.UIThread.Post ──▶ ViewModel 갱신
```

- 공유 가변 상태 없음: 엔진→UI는 불변 스냅샷만 전달.
- 종료 시퀀스: PollEngine 정지 → Master 큐 드레인 → Transport dispose (CancellationToken 전파).

## 10. publish (M10)

```bash
dotnet publish src/Nmw.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

macOS에서 실행 가능(크로스 publish). 성공·크기 확인 후 `publish/` 삭제, 명령을 README에 기록.
