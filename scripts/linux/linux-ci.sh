#!/usr/bin/env bash
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# The workflow's Linux steps inside the container, by stage (ROADMAP 26.1); scripts/linux-ci.mjs starts it from the
# repository, which the container sees read-only at /src. Each step runs whatever the one before it did and says its
# verdict; the stage fails if any step failed. Tests run as the runner's user, as on GitHub: root would pass what a user
# may not do. Installs of system packages stay root's. Logs go to /work/logs/<step>.log.
#   linux-ci.sh sync                      bring the working tree of /src into /work/repo (uncommitted changes too)
#   linux-ci.sh build                     the build job: checks, script tests, client, Release build, unit and AI tests
#   linux-ci.sh integration [args]        scripts/test-integration.mjs with these arguments (none: every share)
#   linux-ci.sh smoke                     the browser smoke and website tests
#   linux-ci.sh run <dir> <command...>    one command as the runner's user, from a folder of the repository
# It starts no database server: the integration tests run on SQLite, and a SQL Server setting is refused.
set -u
REPO=/work/repo
CLIENT=src/Jiron.Umbraco.FormBuilder.Web/Client
failed=0
mkdir -p /work/logs "$REPO"
chown -R runner /work 2>/dev/null
# As on GitHub's runner, which does not poll for file changes (the SDK's image sets it).
export DOTNET_USE_POLLING_FILE_WATCHER=false

# as_runner <dir> <command...>
as_runner() {
  local dir="$1"; shift
  runuser -u runner -- env HOME=/home/runner bash -c 'cd "$1" && shift && exec "$@"' _ "$REPO/$dir" "$@"
}

# step <name> <dir> <command...>   (ROOT=1 step ... runs it as root)
step() {
  local name="$1" dir="$2"; shift 2
  local start=$SECONDS
  local log="/work/logs/$(echo "$name" | tr " /'" '___').log"
  local ok=0
  if [ "${ROOT:-0}" = 1 ]; then (cd "$REPO/$dir" && "$@") > "$log" 2>&1 || ok=1; else as_runner "$dir" "$@" > "$log" 2>&1 || ok=1; fi
  if [ $ok = 0 ]; then
    echo "linux-ci: $name passed in $((SECONDS - start)) s"
  else
    echo "linux-ci: $name FAILED in $((SECONDS - start)) s (log $log)"; failed=1
  fi
}

case "${1:-}" in
  sync)
    # What a build makes stays the container's own; the repository's history is not needed.
    runuser -u runner -- rsync -a --delete \
      --exclude .git --exclude node_modules --exclude bin --exclude obj --exclude .local --exclude .private \
      --exclude TestResults --exclude artifacts --exclude '.nuget' --exclude 'wwwroot/App_Plugins' \
      --exclude 'test-results' --exclude 'playwright-report' \
      /src/ "$REPO/" && echo "linux-ci: synced" || { echo "linux-ci: sync FAILED"; exit 1; }
    ;;
  build)
    step "scripts npm ci" . npm ci --prefix scripts --no-audit --no-fund
    step "check-text" . node scripts/check-text.mjs --tracked
    step "check-workflows" . node scripts/check-workflows.mjs
    step "check-npm-licences" . node scripts/check-npm-licences.mjs
    ROOT=1 step "chromium's system packages" scripts npx playwright install-deps chromium
    step "chromium for the script tests" scripts npx playwright install chromium
    step "script tests" . bash -c 'node --test "scripts/tests/*.test.mjs"'
    step "client npm ci" "$CLIENT" npm ci --no-audit --no-fund
    step "client lint" "$CLIENT" npm run lint
    step "client tests" "$CLIENT" npm test
    step "client build" "$CLIENT" npm run build
    step "size budgets" . node scripts/size-check.mjs
    step "dotnet restore" . dotnet restore
    step "dotnet build" . dotnet build -c Release --no-restore
    step "dotnet format" . dotnet format --verify-no-changes --no-restore
    step "nuget licences" . node scripts/check-nuget-licences.mjs
    step "third-party notices" . node scripts/third-party-notices.mjs --check
    step "pack" . node scripts/pack.mjs --no-build --output artifacts/packages
    step "unit tests" . dotnet test tests/Jiron.Umbraco.FormBuilder.Tests.Unit -c Release --no-build --blame-hang --blame-hang-timeout 10m --blame-hang-dump-type none
    step "AI tests" . dotnet test tests/Jiron.Umbraco.FormBuilder.Tests.AI -c Release --no-build --blame-hang --blame-hang-timeout 10m --blame-hang-dump-type none
    ;;
  integration)
    shift
    for arg in "$@"; do
      case "$arg" in *[Ss][Qq][Ll][Ss]erver*|*[Mm][Ss][Ss][Qq][Ll]*) echo "linux-ci: refused: no SQL Server here ($arg)"; exit 2 ;; esac
    done
    if [ -n "${Tests__Database__DatabaseType:-}" ] && [ "${Tests__Database__DatabaseType}" != "Sqlite" ]; then
      echo "linux-ci: refused: the integration tests run on SQLite here, not ${Tests__Database__DatabaseType}"; exit 2
    fi
    step "scripts npm ci" . npm ci --prefix scripts --no-audit --no-fund
    step "client npm ci" "$CLIENT" npm ci --no-audit --no-fund
    step "client build" "$CLIENT" npm run build
    step "integration build" . dotnet build tests/Jiron.Umbraco.FormBuilder.Tests.Integration -c Release
    step "integration tests" . node scripts/test-integration.mjs --results TestResults/integration "$@"
    ;;
  smoke)
    step "e2e npm ci" tests/e2e npm ci --no-audit --no-fund
    step "e2e check" tests/e2e npm run check
    ROOT=1 step "chromium's system packages" tests/e2e npx playwright install-deps chromium
    step "chromium for the browser tests" tests/e2e npx playwright install chromium
    step "browser smoke tests" tests/e2e npx playwright test --project=smoke --project=website
    ;;
  run)
    shift
    dir="${1:-.}"; shift
    as_runner "$dir" "$@"; exit $?
    ;;
  *) echo "usage: linux-ci.sh <sync|build|integration|smoke|run>"; exit 2 ;;
esac
exit $failed
