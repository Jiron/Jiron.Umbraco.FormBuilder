<#
.SYNOPSIS
  Installs Jiron Form Builder from packages into a fresh Umbraco site, or upgrades one from the released schema
  (ROADMAP 16.5; 18.3 reuses it). ADR-0240.

.DESCRIPTION
  -Mode install  Packs the current build as <VersionPrefix>-dev.<yyyyMMddHHmm> (scripts\pack.mjs) into a local feed,
                 creates a fresh site from the Umbraco template of the Umbraco.Cms version in Directory.Packages.props
                 (installed into this run's own template hive, so the machine's templates stay as they are: SQLite, an
                 unattended install with the admin generated into .local\credentials.json) under
                 %TEMP%\jfb-install-<timestamp>, installs Jiron.Umbraco.FormBuilder from the local feed only (package
                 source mapping, through a fresh package cache %TEMP%\jfb-nuget-<timestamp>), and starts it on
                 http://127.0.0.1:5530 with email to a pickup folder. The checks: the migration state the plan in the
                 code ends at, then tests\e2e\install\check.ts: a form made, published, rendered, sent and read back.
  -Mode upgrade  Also checks out the tag baseline/schema-v1 (4.8) with git worktree into %TEMP%\jfb-baseline-<timestamp>
                 and packs it as 17.0.0-alpha.0.baseline. The site installs the baseline first, on the baseline's own
                 Umbraco; on its first start the baseline's migrations run and scripts\install-test\BaselineSeed writes
                 its data through the baseline's own services. The site stops, swaps the baseline for the current
                 packages, and starts again with Umbraco:CMS:Unattended:UpgradeUnattended: the remaining migrations
                 run, and the checks read the data back through the database and today's APIs, then send a new entry.
  -WriteFixture  With -Mode upgrade: writes tests\fixtures\upgrade\schema-v1.json from the baseline's database before
                 the upgrade, for UpgradeFixtureTests and StoredDefinitionFixtureTests.
  -Keep          Keeps the temporary folders and the worktree (to look into a failure).
  -Version       Packs this version instead of <VersionPrefix>-dev.<yyyyMMddHHmm>, for a release check.

  Every build and pack runs with the throwaway-build settings (CLAUDE.md rule 6): no MSBuild node reuse, no build
  server, no shared compiler. The site's process is recorded in .local\install-test\site.pid.json with its start time,
  and stopped with its whole tree (taskkill /T /F /PID), also when a step fails; a later run stops a site an earlier
  run left only when the record still names that process. Everything the script creates is removed at the end, unless
  -Keep: the site, the feed, the package cache and the worktree (through Node's fs.rmSync, past MAX_PATH, then git
  worktree prune). Passwords are never printed.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-test.ps1 -Mode upgrade
#>
[CmdletBinding()]
param(
    [ValidateSet('install', 'upgrade')]
    [string] $Mode = 'install',

    [switch] $WriteFixture,

    [switch] $Keep,

    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$Repo = Split-Path -Parent $PSScriptRoot
$Client = [System.IO.Path]::Combine($Repo, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Client')
$Helper = Join-Path $PSScriptRoot 'install-test'
$E2E = Join-Path $Repo (Join-Path 'tests' 'e2e')
$State = Join-Path $Repo (Join-Path '.local' 'install-test')
$PidFile = Join-Path $State 'site.pid.json'
$Credentials = Join-Path $Repo (Join-Path '.local' 'credentials.json')
$Fixture = [System.IO.Path]::Combine($Repo, 'tests', 'fixtures', 'upgrade', 'schema-v1.json')
$Run = Get-Date -Format 'yyyyMMddHHmmss'
$Root = Join-Path $env:TEMP "jfb-install-$Run"
$Cache = Join-Path $env:TEMP "jfb-nuget-$Run"
$Worktree = Join-Path $env:TEMP "jfb-baseline-$Run"
$Feed = Join-Path $Root 'feed'
$Templates = Join-Path $Root 'templates'
$Site = Join-Path $Root 'site'
$Seed = Join-Path $Root 'seed'
$Mail = Join-Path $Root 'mail'
$Project = Join-Path $Site 'JfbInstall.csproj'
$Database = [System.IO.Path]::Combine($Site, 'umbraco', 'Data', 'Umbraco.sqlite.db')
# Section 4.11: the install-test site's port.
$Port = 5530
$Url = "http://127.0.0.1:$Port"
$BaselineTag = 'baseline/schema-v1'
$BaselineVersion = '17.0.0-alpha.0.baseline'
$Packages = @('Jiron.Umbraco.FormBuilder')
$script:SiteProcess = $null

# The throwaway-build settings (CLAUDE.md rule 6) for this process and everything it starts: no MSBuild node reuse, no
# build server, no shared compiler, so nothing keeps the run's folders or package cache locked once it ends.
$BuildArguments = @('-nologo', '-v', 'q', '-nodeReuse:false', '-p:UseSharedCompilation=false')
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:UseSharedCompilation = 'false'
$env:DOTNET_NOLOGO = '1'

function Write-Utf8NoBom([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $false))
}

function Invoke-Native([string] $File, [string[]] $Arguments, [string] $Directory = $Repo) {
    Push-Location $Directory
    try {
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    } finally {
        Pop-Location
    }
}

function Get-PropsValue([string] $Checkout, [string] $Name) {
    $match = [regex]::Match([System.IO.File]::ReadAllText((Join-Path $Checkout 'Directory.Build.props')), "<$Name>([^<]+)</$Name>")
    if (-not $match.Success) { throw "Directory.Build.props has no $Name." }
    return $match.Groups[1].Value.Trim()
}

function Get-PackageVersion([string] $Checkout, [string] $Id) {
    # The version a checkout's Directory.Packages.props gives a host package (Umbraco.Cms).
    $props = [xml](Get-Content -Raw (Join-Path $Checkout 'Directory.Packages.props'))
    $node = $props.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq $Id } | Select-Object -First 1
    if (-not $node) { throw "$Id is not in $(Join-Path $Checkout 'Directory.Packages.props')." }
    return $node.Version
}

function New-Password {
    $bytes = New-Object byte[] 24
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return ([Convert]::ToBase64String($bytes) -replace '[+/=]', '') + 'Aa1!'
}

function Get-Admin {
    # The install sites' admin, generated once into .local\credentials.json and never printed.
    $all = New-Object psobject
    if (Test-Path $Credentials) { $all = [System.IO.File]::ReadAllText($Credentials) | ConvertFrom-Json }
    if ($all.PSObject.Properties.Match('installTest').Count -eq 0) {
        $admin = [pscustomobject]@{ name = 'Install admin'; email = 'admin@install.jfb.test'; password = (New-Password) }
        $all | Add-Member -NotePropertyName 'installTest' -NotePropertyValue ([pscustomobject]@{ url = "$Url/umbraco"; admin = $admin })
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Credentials) | Out-Null
        Write-Utf8NoBom $Credentials ($all | ConvertTo-Json -Depth 10)
    }
    return $all.installTest.admin
}

function Test-PortOpen {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        return $connect.AsyncWaitHandle.WaitOne(500) -and $client.Connected
    } finally {
        $client.Close()
    }
}

function Test-Page([string] $Path) {
    try {
        return (Invoke-WebRequest -UseBasicParsing -Uri ($Url + $Path) -TimeoutSec 10).StatusCode -eq 200
    } catch {
        return $false
    }
}

function Get-StartTime([int] $Id) {
    # The start as its ticks in digits: PowerShell 7's ConvertFrom-Json turns a date in a string into a date, which never
    # equals the string again, so the run would not know its own site (16.5's review). Neither edition reads digits as a date.
    $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    return [string]$process.StartTime.ToUniversalTime().Ticks
}

function Get-RecordedSite {
    # The site this script started, as its record names it; null when there is none or its process is another now.
    if (-not (Test-Path $PidFile)) { return $null }
    $record = [System.IO.File]::ReadAllText($PidFile) | ConvertFrom-Json
    if ((Get-StartTime ([int]$record.pid)) -ne [string]$record.startedAt) { return $null }
    return $record
}

function Stop-RecordedSite {
    # Stops the recorded site with its whole tree, only while the record still names that very process.
    $record = Get-RecordedSite
    if ($null -ne $record) {
        & taskkill.exe /T /F /PID $record.pid 2>&1 | Out-Null
        for ($i = 0; $i -lt 60 -and $null -ne (Get-RecordedSite); $i++) { Start-Sleep -Milliseconds 500 }
    }
    $script:SiteProcess = $null
}

function Build-ClientAssets([string] $ClientDir, [string] $PackageVersion) {
    # The assets carry the package's version (umbraco-package.json drives cache busting), so each pack builds its own;
    # without a version they are built for the repository's own (Directory.Build.props).
    if (-not (Test-Path (Join-Path $ClientDir 'node_modules'))) { Invoke-Native 'npm.cmd' @('ci', '--no-audit', '--no-fund') $ClientDir }
    $previous = $env:JFB_VERSION
    try {
        $env:JFB_VERSION = $PackageVersion
        Invoke-Native 'npm.cmd' @('run', 'build', '--silent') $ClientDir
    } finally {
        $env:JFB_VERSION = $previous
    }
}

function Write-NuGetConfig([string] $Directory) {
    # nuget.org and the run's feed only, whatever the machine's own NuGet.config says; Jiron Form Builder only from the
    # feed (package source mapping). In the run's folder, so the site and the seed restore through it.
    Write-Utf8NoBom (Join-Path $Directory 'NuGet.config') @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="jfb-install" value="$Feed" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="jfb-install">
      <package pattern="Jiron.Umbraco.FormBuilder" />
      <package pattern="Jiron.Umbraco.FormBuilder.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
}

function New-Site([string] $UmbracoVersion) {
    # The Umbraco template of that version, from this run's own template hive (section 3.4).
    Invoke-Native 'dotnet' @('new', 'install', "Umbraco.Templates::$UmbracoVersion", '--debug:custom-hive', $Templates)
    Invoke-Native 'dotnet' @('new', 'umbraco', '-n', 'JfbInstall', '-o', $Site, '--development-database-type', 'SQLite', '--no-restore', '--debug:custom-hive', $Templates)
}

function Set-SitePackage([string] $Id, [string] $PackageVersion) {
    Invoke-Native 'dotnet' @('add', $Project, 'package', $Id, '--version', $PackageVersion, '--no-restore') $Site
}

function Build-Site([string[]] $Extra = @()) {
    Invoke-Native 'dotnet' (@('build', $Project, '-c', 'Release') + $BuildArguments + $Extra) $Site
}

function Start-Site([string] $SeedOutput = '') {
    if (Test-PortOpen) { throw "Port $Port is in use by a process this script did not start; it is left alone." }
    $admin = Get-Admin
    $log = Join-Path $Root "site-$(Get-Date -Format 'HHmmss').log"
    $variables = [ordered]@{
        'ASPNETCORE_ENVIRONMENT' = 'Development'
        'ASPNETCORE_URLS' = $Url
        'Umbraco__CMS__Unattended__InstallUnattended' = 'true'
        'Umbraco__CMS__Unattended__PackageMigrationsUnattended' = 'true'
        'Umbraco__CMS__Unattended__UpgradeUnattended' = 'true'
        'Umbraco__CMS__Unattended__UnattendedUserName' = $admin.name
        'Umbraco__CMS__Unattended__UnattendedUserEmail' = $admin.email
        'Umbraco__CMS__Unattended__UnattendedUserPassword' = $admin.password
        # Plain http on this machine: the backoffice's sign-in needs it, as on the demo.
        'Umbraco__CMS__Global__UseHttps' = 'false'
        'Umbraco__CMS__Global__Smtp__From' = 'site@install.jfb.test'
        'Umbraco__CMS__Global__Smtp__DeliveryMethod' = 'SpecifiedPickupDirectory'
        'Umbraco__CMS__Global__Smtp__PickupDirectoryLocation' = $Mail
        'JFB_INSTALL_SEED' = $SeedOutput
    }
    try {
        # Set in this process only for the moment of the start; the site inherits them, nothing is printed or written.
        foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process') }
        $script:SiteProcess = Start-Process -FilePath 'dotnet' -ArgumentList "`"$([System.IO.Path]::Combine($Site, 'bin', 'Release', 'net10.0', 'JfbInstall.dll'))`"" `
            -WorkingDirectory $Site -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    } finally {
        foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        $variables.Clear()
    }
    $record = [pscustomobject]@{ pid = $script:SiteProcess.Id; startedAt = (Get-StartTime $script:SiteProcess.Id) }
    New-Item -ItemType Directory -Force -Path $State | Out-Null
    Write-Utf8NoBom $PidFile ($record | ConvertTo-Json)
    Write-Host "install-test: the site starts (pid $($record.pid))"
    $deadline = (Get-Date).AddMinutes(6)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($script:SiteProcess.HasExited) {
            Get-Content -Tail 30 $log | ForEach-Object { Write-Host "  $_" }
            throw "The site exited with code $($script:SiteProcess.ExitCode)."
        }
        $seeded = (-not $SeedOutput) -or (Test-Path $SeedOutput)
        if ($seeded -and (Test-Page '/umbraco')) {
            Write-Host "install-test: the site answers at $Url"
            return
        }
    }
    throw "The site did not answer within six minutes; see $log."
}

function Invoke-Node([string[]] $Arguments, [string] $Directory = $Repo) {
    Invoke-Native 'node' (@('--no-warnings') + $Arguments) $Directory
}

function Get-NodeLine([string[]] $Arguments) {
    $line = & node --no-warnings @Arguments
    if ($LASTEXITCODE -ne 0) { throw "node $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    return "$line".Trim()
}

function Assert-That([bool] $Condition, [string] $What) {
    if (-not $Condition) { throw "install-test: failed: $What" }
    Write-Host "  ok: $What"
}

function Remove-Folder([string] $Folder) {
    # Through Node, because the worktree's node_modules and the package cache go past MAX_PATH, where Remove-Item fails.
    if (-not $Folder -or -not (Test-Path $Folder)) { return }
    # (No double quotes in the script: Windows PowerShell 5.1 drops them on the way to a native program.)
    & node -e 'require(''fs'').rmSync(process.argv[1], { recursive: true, force: true, maxRetries: 5, retryDelay: 1000 })' $Folder 2>&1 | Out-Null
}

function Remove-Everything {
    # Cleaning up never throws: a failure here would hide the run's own error.
    $ErrorActionPreference = 'Continue'
    $recorded = $null
    if (Test-Path $PidFile) { $recorded = ([System.IO.File]::ReadAllText($PidFile) | ConvertFrom-Json).pid }
    Stop-RecordedSite
    if ($null -ne $recorded) {
        if ($null -eq (Get-Process -Id $recorded -ErrorAction SilentlyContinue) -or $null -eq (Get-RecordedSite)) {
            Remove-Item -Force $PidFile
            Write-Host "install-test: no process of the run is left (the site's pid $recorded and its tree are gone)"
        } else {
            Write-Warning "install-test: the site's pid $recorded still runs"
        }
    }
    if ($Keep) {
        Write-Host "install-test: kept $Root and $Cache$(if (Test-Path $Worktree) { " and $Worktree" })"
        return
    }
    foreach ($folder in @($Root, $Cache, $Worktree)) { Remove-Folder $folder }
    & git -C $Repo worktree prune 2>&1 | Out-Null
    $left = @(@($Root, $Cache, $Worktree) | Where-Object { Test-Path $_ })
    $listed = (& git -C $Repo worktree list) -match [regex]::Escape("jfb-baseline-$Run")
    if ($left.Count -eq 0 -and -not $listed) {
        Write-Host 'install-test: the site, the feed, the package cache and the worktree are removed'
    } else {
        Write-Warning "install-test: still there: $($left -join ', ')$(if ($listed) { ' and the worktree in git' })"
    }
}

$started = Get-Date
$previousCache = $env:NUGET_PACKAGES
$passed = $false
try {
    # A site an earlier run left (a run that was stopped by hand) goes first, only if its record still names it.
    Stop-RecordedSite
    New-Item -ItemType Directory -Force -Path $Feed, $Cache, $Mail | Out-Null
    # A fresh package cache for every restore of this run, so no package comes from an earlier build (ROADMAP D16).
    $env:NUGET_PACKAGES = $Cache
    if (-not $Version) {
        $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmm')
        $Version = "$(Get-PropsValue $Repo 'VersionPrefix')-dev.$stamp"
    }

    Write-Host "install-test: packing the current build as $Version"
    Invoke-Node @((Join-Path $Repo (Join-Path 'scripts' 'pack.mjs')), '--output', $Feed, '--version', $Version)

    if ($Mode -eq 'upgrade') {
        Write-Host "install-test: packing $BaselineTag as $BaselineVersion"
        Invoke-Native 'git' @('worktree', 'add', '--detach', $Worktree, $BaselineTag)
        # The baseline's NuGet.config names its git-ignored local feed, which a fresh checkout does not have.
        New-Item -ItemType Directory -Force -Path (Join-Path $Worktree (Join-Path '.local' 'nuget-feed')) | Out-Null
        Build-ClientAssets ([System.IO.Path]::Combine($Worktree, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Client')) $BaselineVersion
        # A historic build: advisories published since would stop it, and it ships nowhere.
        Invoke-Native 'dotnet' (@('pack', (Join-Path $Worktree (Join-Path 'src' 'Jiron.Umbraco.FormBuilder.Packages.slnf')), '-c', 'Release', '-o', $Feed, "-p:Version=$BaselineVersion", '-p:NuGetAudit=false') + $BuildArguments) $Worktree
    }

    # The upgrade starts on the baseline's own Umbraco, the install on the line's (ROADMAP D16).
    $umbraco = Get-PackageVersion $Repo 'Umbraco.Cms'
    $siteUmbraco = $umbraco
    if ($Mode -eq 'upgrade') { $siteUmbraco = Get-PackageVersion $Worktree 'Umbraco.Cms' }
    Write-Host "install-test: creating a fresh Umbraco $siteUmbraco site in $Site"
    New-Site $siteUmbraco
    Write-NuGetConfig $Root

    if ($Mode -eq 'upgrade') {
        foreach ($id in $Packages) { Set-SitePackage $id $BaselineVersion }
        # The seed, as a project of the run's own that the site references for its first start only.
        New-Item -ItemType Directory -Force -Path $Seed | Out-Null
        Copy-Item (Join-Path $Helper (Join-Path 'BaselineSeed' '*')) $Seed
        Invoke-Native 'dotnet' @('add', $Project, 'reference', (Join-Path $Seed 'JfbBaselineSeed.csproj')) $Site
        Build-Site @("-p:JfbBaselineVersion=$BaselineVersion")
        $expected = Join-Path $Root 'baseline-data.json'
        Start-Site $expected
        $written = [System.IO.File]::ReadAllText($expected)
        Assert-That (-not $written.StartsWith('error:')) "the baseline's seed wrote its data $(if ($written.StartsWith('error:')) { $written })"
        Stop-RecordedSite
        $baselinePlan = [System.IO.Path]::Combine($Worktree, 'src', 'Jiron.Umbraco.FormBuilder.Core', 'Persistence', 'Migrations', 'FormBuilderMigrationPlan.cs')
        $baselineState = Get-NodeLine @((Join-Path $Helper 'migration-plan.mjs'), $baselinePlan)
        $stateBefore = Get-NodeLine @((Join-Path $Helper 'jfb-db.mjs'), 'state', $Database)
        Assert-That ($stateBefore -eq $baselineState) "the baseline ran its migrations to its plan's last state ($stateBefore)"
        $umbracoBefore = Get-NodeLine @((Join-Path $Helper 'jfb-db.mjs'), 'umbraco', $Database)
        if ($WriteFixture) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Fixture) | Out-Null
            Invoke-Node @((Join-Path $Helper 'jfb-db.mjs'), 'dump', $Database, $Fixture)
        }

        Write-Host "install-test: upgrading the site to $Version"
        Invoke-Native 'dotnet' @('remove', $Project, 'reference', (Join-Path $Seed 'JfbBaselineSeed.csproj')) $Site
        if ($siteUmbraco -ne $umbraco) {
            Write-Host "install-test: and Umbraco from $siteUmbraco to $umbraco"
            Set-SitePackage 'Umbraco.Cms' $umbraco
        }
        foreach ($id in $Packages) { Set-SitePackage $id $Version }
        Build-Site
        Start-Site
        $umbracoAfter = Get-NodeLine @((Join-Path $Helper 'jfb-db.mjs'), 'umbraco', $Database)
        if ($siteUmbraco -eq $umbraco) {
            Assert-That ($umbracoAfter -eq $umbracoBefore) "Umbraco's own migration state is untouched ($umbracoAfter)"
        } else {
            Write-Host "  ok: Umbraco's own migration state after $siteUmbraco to $umbraco is $umbracoAfter"
        }
        Write-Host 'install-test: checks'
        Invoke-Node @((Join-Path $Helper 'jfb-db.mjs'), 'check', $Database, $expected)
        Invoke-Node @((Join-Path 'install' 'check.ts'), '--url', $Url, '--mode', 'upgrade', '--expected', $expected) $E2E
    } else {
        foreach ($id in $Packages) { Set-SitePackage $id $Version }
        Build-Site
        Start-Site
        Write-Host 'install-test: checks'
        Invoke-Node @((Join-Path $Helper 'jfb-db.mjs'), 'check', $Database)
        Invoke-Node @((Join-Path 'install' 'check.ts'), '--url', $Url, '--mode', 'install') $E2E
    }
    $passed = $true
} catch {
    Write-Host "install-test: FAILED: $($_.Exception.Message)"
    Write-Host $_.ScriptStackTrace
    throw
} finally {
    $env:NUGET_PACKAGES = $previousCache
    Remove-Everything
    # The pack restored the repository's projects into the run's package cache, which is gone now: their restore files
    # point at the machine's own cache again, so a build without a restore (dotnet format --no-restore) finds its packages.
    try { Invoke-Native 'dotnet' @('restore', (Join-Path $Repo 'Jiron.Umbraco.FormBuilder.slnx'), '-v', 'q', '--nologo') } catch { Write-Warning "install-test: restoring the repository's projects failed: $($_.Exception.Message)" }
    # The repository's own client assets again, for its own version (the pack built them for the packed one).
    try { Build-ClientAssets $Client $null } catch { Write-Warning "install-test: rebuilding the repository's client assets failed: $($_.Exception.Message)" }
    if ($passed) { Write-Host "install-test: $Mode passed in $([int]((Get-Date) - $started).TotalSeconds) s" }
}
