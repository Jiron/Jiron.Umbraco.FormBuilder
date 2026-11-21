<#
.SYNOPSIS
  Jiron Form Builder's local CI mirror: the gate before every push that touches code (ROADMAP 3.5, ADR-0011).

.DESCRIPTION
  Runs what .github/workflows/ci.yml runs, except the SQL Server and load-balancing jobs, because this machine never
  connects to a SQL Server (CLAUDE.md rule 6). Stops at the first failure, and prints the time of every stage and lane.

  The first two stages run lanes side by side through scripts/lanes.mjs (ROADMAP 24.1, ADR-0261): each lane's commands
  in order, each line of output marked with its lane, the first failure stopping the other lanes. A lane holds only
  what needs nothing of the lanes beside it.

  1. Checks and builds, side by side:
     - Repository checks: the secret scan, the term guard and the text check of every tracked file, the workflow and
       Dependabot check, the NuGet audit suppressions, the npm licences, the Marketplace listings against the
       Marketplace's schema (downloaded once into .local\marketplace), and the script tests.
     - Client build: npm ci, the build, the size budgets.
     - Browser set-up (not with -SkipE2E): npm ci and the check of tests/e2e.
  2. Client and .NET, side by side; the .NET build embeds the client's build of stage 1:
     - Client lint.
     - Client tests.
     - .NET: restore, a Release build with warnings as errors, dotnet format, the NuGet licences, the third-party
       notices, then the four packages from that build (the three a site installs and the AI add-on) through
       scripts/pack.mjs as CI and the release pack them, which checks every entry against the allowlist and every
       nuspec (scripts/check-packages.mjs).
  3. Unit and integration tests on SQLite, through scripts/test-integration.mjs (ADR-0261): the integration tests in
     shares, several processes at once (half the cores, at most eight), each with a working folder of its own; the
     unit and AI tests beside them; then the tests that hold the product to a time budget (the NUnit category
     "Timed") alone. The stage fails unless the tests that ran add up to exactly the integration assembly's tests. A
     test that hangs fails after 10 minutes and is named.
  4. The browser projects from the Release build, in Edge, through scripts/e2e-gate.mjs (ADR-0261): two sites side
     by side, three workers each, each with a fresh demo host, ports, database and people of its own, every spec file
     on one site; then the budgets project alone. The stage fails unless the tests that ran add up to exactly the
     projects' tests. When a site's SQLite database hung (Umbraco 17's start-up stall, ADR-0011), the harness leaves
     the hang marker, and then, and only then, that site runs once more ("Flaky means broken", ROADMAP "How to use").
     No traces are recorded; a failed test's summary line gives the command that runs it again with its trace.

  Every native command is judged by its exit code only: Windows PowerShell 5.1 turns lines a tool writes to stderr
  into error records when the output is redirected, and tools print warnings there.

  Run node scripts/fix.mjs first: a gate that fails on formatting alone wastes a long run.

.PARAMETER SkipE2E
  Leaves out the browser projects and their set-up, for a quicker run while working. Such a run is not the gate.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ci-local.ps1
#>
[CmdletBinding()]
param([switch] $SkipE2E)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$Repo = Split-Path -Parent $PSScriptRoot
$Client = 'src/Jiron.Umbraco.FormBuilder.Web/Client'
$E2E = 'tests/e2e'
$Reports = Join-Path $Repo '.local\ci-lanes'
$Stages = New-Object System.Collections.Generic.List[object]
$LaneRows = New-Object System.Collections.Generic.List[object]
$Total = [System.Diagnostics.Stopwatch]::StartNew()

function Invoke-Native([string] $File, [string[]] $Arguments, [string] $Directory = $Repo) {
    Push-Location $Directory
    # Only the exit code decides; see the description.
    $ErrorActionPreference = 'Continue'
    try {
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') exited with code $LASTEXITCODE" }
    } finally {
        Pop-Location
    }
}

# Runs lanes side by side (scripts/lanes.mjs) and keeps each lane's seconds and result for the stage table. Each lane
# is an array: its name, then '--cwd' and a folder if it has one, then its commands, each one string of plain words.
function Invoke-Lanes([string] $Name, [object[]] $Lanes) {
    $report = Join-Path $Reports "$Name.json"
    Remove-Item $report -ErrorAction SilentlyContinue
    $arguments = @('scripts/lanes.mjs', '--report', $report)
    foreach ($lane in $Lanes) {
        $arguments += '--lane'
        $arguments += $lane
    }
    try {
        Invoke-Native 'node' $arguments
    } finally {
        if (Test-Path $report) {
            foreach ($row in (Get-Content $report -Raw | ConvertFrom-Json)) {
                $result = if ($row.result -eq 'passed') { 'passed' } else { "$($row.result) at $($row.failed)" }
                $LaneRows.Add([pscustomobject]@{ Stage = "  - $($row.name)"; Seconds = [math]::Round($row.seconds, 1); Result = $result })
            }
        }
    }
}

function Add-StageRows([string] $Name, [System.Diagnostics.Stopwatch] $Watch, [string] $Result) {
    $Stages.Add([pscustomobject]@{ Stage = $Name; Seconds = [math]::Round($Watch.Elapsed.TotalSeconds, 1); Result = $Result })
    foreach ($row in $LaneRows) { $Stages.Add($row) }
    $LaneRows.Clear()
}

function Invoke-Stage([string] $Name, [scriptblock] $Action) {
    Write-Host ''
    Write-Host "== $Name" -ForegroundColor Cyan
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
    } catch {
        Add-StageRows $Name $watch 'failed'
        $Stages | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
        Write-Host "ci-local: FAILED in '$Name': $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    Add-StageRows $Name $watch 'passed'
}

# Build nodes that stay behind would hold this repository's folders for the next build (CLAUDE.md rule 6).
$PreviousNodeReuse = $env:MSBUILDDISABLENODEREUSE
$env:MSBUILDDISABLENODEREUSE = '1'
try {
    Invoke-Stage 'Checks and builds' {
        $lanes = @(
            , @('Repository checks',
                'npm ci --prefix scripts --no-audit --no-fund',
                'node scripts/secret-scan.mjs --tracked',
                'node scripts/term-guard.mjs --tracked',
                'node scripts/check-text.mjs --tracked',
                'node scripts/check-workflows.mjs',
                'node scripts/check-audit-suppressions.mjs',
                'node scripts/check-npm-licences.mjs',
                # Before the script tests, which read the schema it downloads.
                'node scripts/check-marketplace.mjs',
                'node --test scripts/tests/*.test.mjs')
            , @('Client build', '--cwd', $Client,
                'npm ci --no-audit --no-fund',
                'npm run build',
                'npm run size')
        )
        if (-not $SkipE2E) {
            $lanes += , @('Browser set-up', '--cwd', $E2E,
                'npm ci --no-audit --no-fund',
                'npm run check')
        }
        Invoke-Lanes 'checks-and-builds' $lanes
    }

    Invoke-Stage 'Client and .NET' {
        Invoke-Lanes 'client-and-dotnet' @(
            , @('Client lint', '--cwd', $Client, 'npm run lint')
            , @('Client tests', '--cwd', $Client, 'npm test')
            , @('.NET',
                'dotnet restore -nologo',
                'dotnet build -c Release --no-restore -nologo -nodeReuse:false',
                'dotnet format --verify-no-changes --no-restore',
                'node scripts/check-nuget-licences.mjs',
                'node scripts/third-party-notices.mjs --check',
                # The Release build above, packed the way CI and the release pack it (scripts/pack.mjs).
                'node scripts/pack.mjs --no-build --output .local/ci-packages')
        )
    }

    Invoke-Stage 'Unit and integration tests (SQLite)' {
        # The integration tests in shares side by side, the unit and AI tests beside them, then the timed tests alone;
        # it fails unless the tests that ran are exactly the integration assembly's (scripts/test-integration.mjs).
        Invoke-Native 'node' @('scripts/test-integration.mjs',
            '--beside', 'tests/Jiron.Umbraco.FormBuilder.Tests.Unit/bin/Release/net10.0/Jiron.Umbraco.FormBuilder.Tests.Unit.dll',
            '--beside', 'tests/Jiron.Umbraco.FormBuilder.Tests.AI/bin/Release/net10.0/Jiron.Umbraco.FormBuilder.Tests.AI.dll')
    }

    if ($SkipE2E) {
        Write-Host ''
        Write-Host 'ci-local: the browser projects were skipped (-SkipE2E); this run is not the gate.' -ForegroundColor Yellow
    } else {
        Invoke-Stage 'Browser projects (Edge)' {
            # The client and the demo were built above, and tests/e2e was installed and checked beside them; the
            # projects start the Release build.
            $env:JFB_E2E_SKIP_BUILD = '1'
            $env:JFB_E2E_CONFIGURATION = 'Release'
            try {
                # Every project, and once more only when a host left the hang marker in this run (e2e-gate.mjs).
                Invoke-Native 'node' @('scripts/e2e-gate.mjs')
            } finally {
                Remove-Item Env:\JFB_E2E_SKIP_BUILD, Env:\JFB_E2E_CONFIGURATION -ErrorAction SilentlyContinue
            }
        }
    }
} finally {
    if ($null -eq $PreviousNodeReuse) { Remove-Item Env:\MSBUILDDISABLENODEREUSE -ErrorAction SilentlyContinue } else { $env:MSBUILDDISABLENODEREUSE = $PreviousNodeReuse }
}

Write-Host ''
$Stages | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$verdict = if ($SkipE2E) { 'passed without the browser projects' } else { 'all checks passed' }
Write-Host ("ci-local: {0} in {1:n0} s" -f $verdict, $Total.Elapsed.TotalSeconds) -ForegroundColor Green
