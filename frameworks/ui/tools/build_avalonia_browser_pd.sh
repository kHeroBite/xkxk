#!/usr/bin/env bash
# Avalonia.Browser 12.1.4 유휴 rAF 패치판(12.1.4-pd.1)을 로컬 피드 vendor/feed 로 pack 하는 스크립트(Q9 ⑤, 기본 dry-run)
#
# 사용:
#   tools/build_avalonia_browser_pd.sh            # dry-run: 확인만(패치 적용 상태·SDK·피드 폴더·실행할 명령 출력), 빌드 안 함
#   tools/build_avalonia_browser_pd.sh --pack     # 실제 pack(빌드 동결 해제 뒤 odev-4b 빌드 순서에서만)
#   tools/build_avalonia_browser_pd.sh --fetch    # 원본 소스만 다시 받기(태그 12.1.4 + sparse + 커밋 대조 + 패치 적용). 소스가 없으면 다른 모드도 자동으로 먼저 받는다
#
# 전제(web_idle_raf_patch_step1.md):
#   - 소스: vendor/avalonia-12.1.4(태그 12.1.4, 커밋 2d79ba5, sparse checkout) + vendor/patches/*.pd.1.patch 적용 상태
#   - 서명: 저장소 build/avalonia.snk(SharedVersion.props) — 원본과 같은 공개 키 토큰이어야 다른 Avalonia 어셈블리·InternalsVisibleTo 와 맞는다
#   - PdStandalone=true: Avalonia·Skia·HarfBuzz 는 NuGet 12.1.4 참조(이 어셈블리만 빌드)
#   - webapp: csproj 의 BunInstall/BunRunBuild 가 dnx Bun.Unofficial.Tool 로 avalonia.js 를 만든다(네트워크 필요)
#   - .NET SDK 10.0.201 이상(vendor global.json)
set -euo pipefail

HERE="$(cd "$(dirname "$0")/.." && pwd)"
VENDOR="$HERE/vendor/avalonia-12.1.4"
PROJ="$VENDOR/src/Browser/Avalonia.Browser/Avalonia.Browser.csproj"
PATCH="$HERE/vendor/patches/avalonia-browser-12.1.4-idle-raf.pd.1.patch"
FEED="$HERE/vendor/feed"
VERSION="12.1.4-pd.1"
DOTNET="${DOTNET_EXE:-/mnt/c/Program Files/dotnet/dotnet.exe}"
MODE="${1:-dry-run}"

# 원본 재취득(ADR-044 — 원본 소스는 저장소에 넣지 않는다). root·Docker 없이 git 만 쓴다(ADR-043).
# 태그 12.1.4 를 얕게(depth 1) 받되 파일 내용은 필요한 경로만(blob 지연 + sparse), 커밋 해시를 대조한 뒤 PD 패치를 적용한다.
TAG="12.1.4"
COMMIT="2d79ba55fae1b1e71c69f93a668080c9029e6a31"
SPARSE_PATHS=(
  /src/Browser/Avalonia.Browser/          # 패치 대상 어셈블리
  /src/Shared/RawEventGrouping.cs         # Avalonia.Browser.csproj 가 링크로 포함
  /src/Directory.Build.props              # src 공통 설정(SharedVersion·SourceLink 등 import)
  /build/                                 # BuildTargets·SharedVersion(서명 키 avalonia.snk) 등
  /packages/Avalonia/                     # DevSingleProject.targets 가 import 하는 AvaloniaSingleProject.targets
  /licence.md                             # MIT 원문(고지 유지)
  /Directory.Build.props
  /Directory.Build.targets                # MSBuild 자동 import — 없으면 조용히 빠짐
  /Directory.Packages.props               # 중앙 패키지 버전 관리
  /global.json                            # SDK 10.0.201+
  /NuGet.Config
)

fetch_source() {
  echo "[pd] 원본 받기: AvaloniaUI/Avalonia 태그 $TAG → $VENDOR"
  rm -rf "$VENDOR"
  git clone --depth 1 --branch "$TAG" --filter=blob:none --sparse --no-checkout https://github.com/AvaloniaUI/Avalonia.git "$VENDOR"
  git -C "$VENDOR" sparse-checkout set --no-cone "${SPARSE_PATHS[@]}"
  git -C "$VENDOR" checkout "$TAG"
  local head
  head="$(git -C "$VENDOR" rev-parse HEAD)"
  [ "$head" = "$COMMIT" ] || { echo "[pd] 커밋 불일치: $head (기대 $COMMIT) — 태그가 옮겨졌을 수 있음, 중단"; exit 6; }
  git -C "$VENDOR" apply "$PATCH"
  echo "[pd] 원본 받기·패치 적용 완료(커밋 $COMMIT)"
}

if [ "$MODE" = "--fetch" ] || [ ! -f "$PROJ" ]; then
  fetch_source
  [ "$MODE" = "--fetch" ] && exit 0
fi

echo "[pd] 대상 $PROJ"
test -f "$PROJ" || { echo "[pd] 원본 소스 없음 — tools/build_avalonia_browser_pd.sh --fetch 로 받으십시오"; exit 2; }

# 패치 적용 상태: 작업 트리 diff 가 패치 파일과 같아야 한다
if ! diff -q <(git -C "$VENDOR" diff) "$PATCH" >/dev/null; then
  echo "[pd] 작업 트리가 패치 파일과 다릅니다 — git -C vendor/avalonia-12.1.4 checkout . && git apply $PATCH"
  exit 3
fi
echo "[pd] 패치 적용 상태 확인(작업 트리 diff == $(basename "$PATCH"))"

"$DOTNET" --list-sdks | grep -q '^10\.0\.2' || { echo "[pd] .NET SDK 10.0.2xx 없음"; exit 4; }
mkdir -p "$FEED"

PROJ_WIN="$(wslpath -w "$PROJ")"
FEED_WIN="$(wslpath -w "$FEED")"
CMD=("$DOTNET" pack "$PROJ_WIN" -c Release -p:PdStandalone=true -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true -o "$FEED_WIN" -nologo -v:minimal)

if [ "$MODE" != "--pack" ]; then
  echo "[pd] dry-run — 실행하지 않음:"
  printf ' %q' "${CMD[@]}"; echo
  echo "[pd] 소비: 웹 빌드에 -p:PdAvaloniaBrowserPatch=true (Directory.Build.props → Avalonia.Browser $VERSION, 피드 vendor/feed)"
  exit 0
fi

"${CMD[@]}"
ls -l "$FEED" | grep -i "avalonia.browser.$VERSION" || { echo "[pd] 패키지가 만들어지지 않았습니다"; exit 5; }
echo "[pd] 완료 — 공개 키 토큰을 NuGet 원본 12.1.4 Avalonia.Browser.dll 과 대조하십시오(sn -T 또는 리플렉션)."
