<#
.SYNOPSIS
  Jiron Form Builder's local CI mirror: the gate before every push that touches code (ROADMAP 3.5, ADR-0011).

.DESCRIPTION
  Runs what .github/workflows/ci.yml runs, except the SQL Server and load-balancing jobs, because this machine never
  connects to a SQL Server (CLAUDE.md rule 6). Stops at the first failure, and prints the time of every stage.

  1. Repository checks: the secret scan, the term guard and the text check of every tracked file, the workflow and
     Dependabot check, the NuGet audit suppressions, the npm licences, and the script tests.
  2. Client: npm ci, lint, tests, build, the size budgets.
  3. .NET: restore, a Release build with warnings as errors, dotnet format, the NuGet licences, the third-party
     notices.
  4. Packages: the three packages from the Release build, through scripts/pack.mjs as CI and the release pack them.
  5. Unit and integration tests on SQLite; a test that hangs fails after 10 minutes and is named.
  6. The browser projects on a fresh demo host from the Release build, in Edge, through scripts/e2e-gate.mjs. When
     a host's SQLite database hung (Umbraco 17's start-up stall, ADR-0011), the harness leaves the hang marker, and
     then, and only then, the projects run once more ("Flaky means broken", ROADMAP "How to use").

  Every native command is judged by its exit code only: Windows PowerShell 5.1 turns lines a tool writes to stderr
  into error records when the output is redirected, and tools print warnings there.

  Run node scripts/fix.mjs first: a gate that fails on formatting alone wastes a long run.

.PARAMETER SkipE2E
  Leaves out the browser projects, for a quicker run while working. Such a run is not the gate.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ci-local.ps1
#>
[CmdletBinding()]
param([switch] $SkipE2E)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$Repo = Split-Path -Parent $PSScriptRoot
$Client = Join-Path $Repo 'src\Jiron.Umbraco.FormBuilder.Web\Client'
$E2E = Join-Path $Repo 'tests\e2e'
$Packages = Join-Path $Repo '.local\ci-packages'
$Stages = New-Object System.Collections.Generic.List[object]
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

function Invoke-Stage([string] $Name, [scriptblock] $Action) {
    Write-Host ''
    Write-Host "== $Name" -ForegroundColor Cyan
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
    } catch {
        $Stages.Add([pscustomobject]@{ Stage = $Name; Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1); Result = 'failed' })
        $Stages | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host "ci-local: FAILED in '$Name': $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    $Stages.Add([pscustomobject]@{ Stage = $Name; Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1); Result = 'passed' })
}

# Build nodes that stay behind would hold this repository's folders for the next build (CLAUDE.md rule 6).
$PreviousNodeReuse = $env:MSBUILDDISABLENODEREUSE
$env:MSBUILDDISABLENODEREUSE = '1'
try {
    Invoke-Stage 'Repository checks' {
        Invoke-Native 'npm.cmd' @('ci', '--prefix', 'scripts', '--no-audit', '--no-fund')
        Invoke-Native 'node' @('scripts/secret-scan.mjs', '--tracked')
        Invoke-Native 'node' @('scripts/term-guard.mjs', '--tracked')
        Invoke-Native 'node' @('scripts/check-text.mjs', '--tracked')
        Invoke-Native 'node' @('scripts/check-workflows.mjs')
        Invoke-Native 'node' @('scripts/check-audit-suppressions.mjs')
        Invoke-Native 'node' @('scripts/check-npm-licences.mjs')
        Invoke-Native 'node' @('--test', 'scripts/tests/*.test.mjs')
    }

    Invoke-Stage 'Client: lint, tests, build and size budgets' {
        Invoke-Native 'npm.cmd' @('ci', '--no-audit', '--no-fund') $Client
        Invoke-Native 'npm.cmd' @('run', 'lint') $Client
        Invoke-Native 'npm.cmd' @('test') $Client
        Invoke-Native 'npm.cmd' @('run', 'build') $Client
        Invoke-Native 'npm.cmd' @('run', 'size') $Client
    }

    Invoke-Stage '.NET: build, formatting, licences and notices' {
        Invoke-Native 'dotnet' @('restore', '-nologo')
        Invoke-Native 'dotnet' @('build', '-c', 'Release', '--no-restore', '-nologo', '-nodeReuse:false')
        Invoke-Native 'dotnet' @('format', '--verify-no-changes', '--no-restore')
        Invoke-Native 'node' @('scripts/check-nuget-licences.mjs')
        Invoke-Native 'node' @('scripts/third-party-notices.mjs', '--check')
    }

    Invoke-Stage 'Packages' {
        # The Release build above, packed the way CI and the release pack it (scripts/pack.mjs).
        Invoke-Native 'node' @('scripts/pack.mjs', '--no-build', '--output', $Packages)
    }

    Invoke-Stage 'Unit and integration tests (SQLite)' {
        Invoke-Native 'dotnet' @('test', '-c', 'Release', '--no-build', '-nologo', '--blame-hang', '--blame-hang-timeout', '10m', '--blame-hang-dump-type', 'none')
    }

    if ($SkipE2E) {
        Write-Host ''
        Write-Host 'ci-local: the browser projects were skipped (-SkipE2E); this run is not the gate.' -ForegroundColor Yellow
    } else {
        Invoke-Stage 'Browser projects (Edge)' {
            Invoke-Native 'npm.cmd' @('ci', '--no-audit', '--no-fund') $E2E
            Invoke-Native 'npm.cmd' @('run', 'check') $E2E
            # The client and the demo were built above; the projects start the Release build.
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
$Stages | Format-Table -AutoSize | Out-String | Write-Host
$verdict = if ($SkipE2E) { 'passed without the browser projects' } else { 'all checks passed' }
Write-Host ("ci-local: {0} in {1:n0} s" -f $verdict, $Total.Elapsed.TotalSeconds) -ForegroundColor Green
