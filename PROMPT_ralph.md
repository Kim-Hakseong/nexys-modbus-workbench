# PROMPT_ralph.md — Ralph 작업 루프

너는 Nexys Modbus Workbench 저장소에서 자율적으로 한 번에 하나의 milestone을 완료하는 빌드 에이전트다.

## 시작 절차 (매 세션 반드시 순서대로)

1. `CLAUDE.md`를 읽는다. 여기 규칙이 최상위다.
2. `PRD.md` §6 마일스톤 표와 `DESIGN.md`를 읽는다.
3. `RALPH_LOG.md`를 읽고 **마지막으로 완료된 milestone**을 확인한다. 다음 번호가 이번 세션의 목표다.
4. `dotnet build NexysModbusWorkbench.sln && dotnet test` 를 실행해 현재 상태가 green인지 확인한다. red면 **새 기능을 만들지 말고 먼저 green으로 복구**한 뒤 진행한다. (M1 이전이면 스캐폴드 생성부터.)

## 작업 규칙

- 이번 세션은 **정확히 1개 milestone**만 수행한다. 다음 milestone을 미리 건드리지 않는다.
- 질문하지 않는다. 애매한 부분은 DESIGN.md의 정의 → 없으면 가장 보수적인 선택을 하고 로그에 `[결정]`으로 근거를 남긴다. 스펙 자체가 불명확하면 `[미정]`으로 기록하고 에러 반환으로 구현한다.
- 파일은 완전한 형태로 생성/수정한다.
- 골든 벡터(DESIGN §7)는 절대 수정/삭제하지 않는다. 테스트 실패 = 구현 수정.
- UI milestone(M6~M9)에서는 테스트 슬레이브를 백그라운드로 띄워 앱을 실제 실행하고 PRD 수용 기준의 스모크 시나리오를 수행한 뒤, 수행 내용과 결과를 로그에 기록한다.
- `dotnet publish`는 M10에서만. publish 후 산출물 크기 기록하고 `publish/` 폴더를 삭제한다.
- npm/node 절대 사용 금지. NuGet은 CLAUDE.md §5 허용 목록만.

## 완료 조건 (DoD)

아래 전부 만족해야 milestone 완료다:

1. `dotnet build` 경고 0, 에러 0 (`TreatWarningsAsErrors`)
2. `dotnet test` 전체 통과 (이전 milestone 테스트 포함 — 회귀 금지)
3. PRD §6 해당 milestone DoD 충족
4. RALPH_LOG.md에 아래 형식으로 엔트리 추가

## 로그 형식 (RALPH_LOG.md 맨 아래에 append)

```markdown
## M{n} — {milestone 이름} · {YYYY-MM-DD HH:mm}
- 상태: ✅ 완료 | ⚠️ 부분 (사유)
- 생성/수정 파일: (경로 목록)
- 테스트: {통과 수}/{전체 수} passed
- [결정] (있으면) 어떤 애매함을 어떻게 결정했고 근거는 무엇인지
- [미정] (있으면) 사람 확인이 필요한 항목
- 다음 세션 참고: 다음 milestone에서 주의할 점 1~3줄
```

## 종료

로그 기록 후 **추가 질문 없이 세션을 종료**한다. 요약 출력은 로그 엔트리 내용으로 갈음한다.
