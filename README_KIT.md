# Nexys Modbus Workbench — 하네스 킷 사용법

## 구성
| 파일 | 역할 |
|---|---|
| CLAUDE.md | 빌드 헌법 (최상위 규칙, 스택 고정, 금지사항) |
| PRD.md | 기능 요구사항 + 마일스톤 M1~M10 + DoD |
| DESIGN.md | 아키텍처, 프로토콜 상세, 골든 테스트 벡터(검증 완료) |
| PROMPT_ralph.md | Claude Code 자율 루프 프롬프트 |
| RALPH_LOG.md | 빌드 이력 (Ralph가 append) |

## 실행 (Mac Mini)
```bash
mkdir -p ~/Haku/builds/nexys-modbus-workbench && cd $_
# 킷 4개 파일 + RALPH_LOG.md 를 이 폴더에 복사
brew install --cask dotnet-sdk   # .NET 8 SDK (최초 1회)
claude   # Claude Code MAX 실행 후:
#   "PROMPT_ralph.md 를 읽고 그대로 수행해."
```
세션 1회 = milestone 1개. M1~M10 = 10세션. 각 세션 후 RALPH_LOG.md 리뷰.

## 최종 exe 생성 (Windows PC 또는 Mac에서 크로스 publish)
```bash
dotnet publish src/Nmw.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```
산출물: `publish/win-x64/Nmw.App.exe` — Windows 10/11에 복사해 더블클릭 실행.

## 디스크 참고 (Mac Mini)
- NuGet 캐시(~/.nuget) 약 300~500MB, bin/obj 수백 MB 수준. node_modules 급 폭주 없음.
- 프로젝트 종료 후 정리: `dotnet nuget locals all --clear` + `git clean -xdf`
