<#
.SYNOPSIS
  Runs the Jiron Form Builder demo site (ROADMAP 3.3) at the address in its appsettings.json (http://127.0.0.1:5500).

.DESCRIPTION
  start   Builds the client assets when they are missing or stale, starts the demo in the background and waits until
          it is seeded and serves its home page in English and German. The first start is a fresh install with a
          generated admin and two generated library members, kept in .local\credentials.json ("demo") and passed to
          the demo through its environment only, never on a command line. The demo's email is written to
          .local\mail-pickup\demo as .eml files and never sent.
  stop    Stops the demo that this script started, with its whole process tree, and nothing else.
  reset   Stops it and deletes its database, logs, media and mail, so the next start is a fresh install. The
          generated credentials are kept.
  login   Prints the backoffice address and where the generated credentials are, never a password.

  The demo is never started any other way: without the imaging key this script passes, the demo refuses to start,
  so Umbraco can never write a generated key into the tracked appsettings.json (ROADMAP section 3.3).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 start
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('start', 'stop', 'reset', 'login')]
    [string] $Command
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$Repo = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Repo 'demo\Jiron.Umbraco.FormBuilder.Demo'
$Client = Join-Path $Repo 'src\Jiron.Umbraco.FormBuilder.Web\Client'
$State = Join-Path $Repo '.local\demo'
$PidFile = Join-Path $State 'demo.pid'
$LogFile = Join-Path $State 'demo.log'
$MailDir = Join-Path $Repo '.local\mail-pickup\demo'
$Credentials = Join-Path $Repo '.local\credentials.json'

# The demo's address is its own setting, which the checks and the browser tests read too ("One source for every value").
$Url = ([System.IO.File]::ReadAllText((Join-Path $Project 'appsettings.json')) | ConvertFrom-Json).Umbraco.CMS.WebRouting.UmbracoApplicationUrl
$Port = ([Uri] $Url).Port
if ($Port -lt 5500 -or $Port -gt 5599) {
    # Jiron Form Builder's servers use ports 5500 to 5599 only (CLAUDE.md rule 6, ROADMAP section 4.11).
    throw "The demo's address $Url is outside the ports 5500 to 5599."
}

function Write-Utf8NoBom([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $false))
}

function New-RandomText([int] $Bytes) {
    $buffer = New-Object byte[] $Bytes
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($buffer) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($buffer)
}

function New-Password {
    # About 32 random characters plus one of each class, so any password policy accepts it.
    return ((New-RandomText 24) -replace '[+/=]', '') + 'Aa1!'
}

function New-Person([string] $Name, [string] $Email) {
    return [pscustomobject]@{ name = $Name; email = $Email; password = (New-Password) }
}

function Get-DemoSecrets {
    # The demo's generated secrets in .local\credentials.json, created on first use and never printed: the admin, two
    # library members and Umbraco's imaging key. Other entries in the file (the sandbox, the tests) are kept as they are.
    $all = New-Object psobject
    if (Test-Path $Credentials) {
        $all = [System.IO.File]::ReadAllText($Credentials) | ConvertFrom-Json
    }
    # Match, not .Name: under strict mode an empty object (no credentials file yet, a fresh clone) has no Name to list.
    if ($all.PSObject.Properties.Match('demo').Count -eq 0) {
        $all | Add-Member -NotePropertyName 'demo' -NotePropertyValue ([pscustomobject]@{ url = "$Url/umbraco" })
    }
    $demo = $all.demo
    if ($demo.PSObject.Properties.Match('admin').Count -eq 0) {
        $demo | Add-Member -NotePropertyName 'admin' -NotePropertyValue (New-Person 'Demo admin' 'admin@lindenhall.example')
    }
    if ($demo.PSObject.Properties.Match('members').Count -eq 0) {
        $members = @(
            (New-Person 'Mira Jansen' 'mira.jansen@members.lindenhall.example'),
            (New-Person 'Jonas Weber' 'jonas.weber@members.lindenhall.example')
        )
        $demo | Add-Member -NotePropertyName 'members' -NotePropertyValue $members
    }
    if ($demo.PSObject.Properties.Match('imagingHmacSecretKey').Count -eq 0) {
        $demo | Add-Member -NotePropertyName 'imagingHmacSecretKey' -NotePropertyValue (New-RandomText 64)
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Credentials) | Out-Null
    Write-Utf8NoBom $Credentials ($all | ConvertTo-Json -Depth 10)
    return $demo
}

function Get-DemoProcess {
    # The process this script started, if it still runs. The PID file holds its id and start time, so a process that
    # later got the same id is never taken for the demo; nothing else is ever touched (CLAUDE.md rule 6).
    if (-not (Test-Path $PidFile)) { return $null }
    $recorded = [System.IO.File]::ReadAllText($PidFile) | ConvertFrom-Json
    $process = Get-Process -Id ([int] $recorded.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    if ($process.StartTime.ToUniversalTime().ToString('o') -ne [string] $recorded.started) { return $null }
    return $process
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
        $response = Invoke-WebRequest -UseBasicParsing -Uri ($Url + $Path) -TimeoutSec 10
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Invoke-Npm([string[]] $Arguments) {
    Push-Location $Client
    try {
        & npm.cmd @Arguments
        if ($LASTEXITCODE -ne 0) { throw "npm $($Arguments -join ' ') failed" }
    } finally {
        Pop-Location
    }
}

function Update-ClientAssets {
    # The demo embeds the website assets and serves the backoffice bundle, so they must be current (ADR-0026).
    & node (Join-Path $Client 'scripts\stamp.mjs') check | Out-Null
    if ($LASTEXITCODE -eq 0) { return }
    Write-Host 'demo: building the client assets'
    if (-not (Test-Path (Join-Path $Client 'node_modules'))) { Invoke-Npm @('ci', '--no-audit', '--no-fund') }
    Invoke-Npm @('run', 'build')
}

function Start-Demo {
    if (Get-DemoProcess) {
        Write-Host "demo: already running at $Url"
        return
    }
    if (Test-PortOpen) {
        throw "Port $Port is in use by a process this script did not start; it is left alone."
    }

    $secrets = Get-DemoSecrets
    Update-ClientAssets
    New-Item -ItemType Directory -Force -Path $State, $MailDir | Out-Null

    # The child gets these through its environment. They are set in this short-lived PowerShell process only for the
    # moment of the start and removed straight after; nothing is printed or written. Start-Process (shell execute)
    # inherits no handles, so the caller's console or pipe is not held open by the demo.
    $variables = [ordered]@{
        'ASPNETCORE_ENVIRONMENT' = 'Development'
        'ASPNETCORE_URLS' = $Url
        'DOTNET_NOLOGO' = '1'
        'MSBUILDDISABLENODEREUSE' = '1'
        'Umbraco__CMS__Unattended__UnattendedUserName' = $secrets.admin.name
        'Umbraco__CMS__Unattended__UnattendedUserEmail' = $secrets.admin.email
        'Umbraco__CMS__Unattended__UnattendedUserPassword' = $secrets.admin.password
        'Umbraco__CMS__Imaging__HMACSecretKey' = $secrets.imagingHmacSecretKey
        'Umbraco__CMS__Global__Smtp__PickupDirectoryLocation' = $MailDir
    }
    $index = 0
    foreach ($member in @($secrets.members)) {
        $variables["Demo__Members__$($index)__Name"] = $member.name
        $variables["Demo__Members__$($index)__Email"] = $member.email
        $variables["Demo__Members__$($index)__Password"] = $member.password
        $index++
    }
    try {
        foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process') }
        $process = Start-Process -FilePath $env:ComSpec -WorkingDirectory $Repo -WindowStyle Hidden -PassThru `
            -ArgumentList "/d /c dotnet run --project `"$Project`" --no-launch-profile > `"$LogFile`" 2>&1"
    } finally {
        foreach ($name in @($variables.Keys)) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        $variables.Clear()
    }
    $started = $process.StartTime.ToUniversalTime().ToString('o')
    Write-Utf8NoBom $PidFile (([pscustomobject]@{ pid = $process.Id; started = $started }) | ConvertTo-Json)
    Write-Host "demo: starting (pid $($process.Id)); log in .local\demo\demo.log"

    $deadline = (Get-Date).AddMinutes(6)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($process.HasExited) {
            Get-Content -Tail 25 $LogFile | ForEach-Object { Write-Host "  $_" }
            Remove-Item -Force $PidFile -ErrorAction SilentlyContinue
            throw "The demo exited with code $($process.ExitCode)."
        }
        if ((Test-Path $LogFile) -and (Select-String -Path $LogFile -SimpleMatch 'Demo seeding failed' -Quiet)) {
            Stop-Demo
            throw 'The demo seeding failed; see .local\demo\demo.log.'
        }
        # Ready means seeded: the seeder logs this line when it has finished.
        $seeded = (Test-Path $LogFile) -and (Select-String -Path $LogFile -SimpleMatch 'Demo seeding finished' -Quiet)
        if ($seeded -and (Test-Page '/en/') -and (Test-Page '/de/')) {
            Write-Host "demo: ready at $Url/en/ and $Url/de/ (backoffice $Url/umbraco)"
            return
        }
    }
    throw 'The demo did not answer within six minutes; see .local\demo\demo.log.'
}

function Stop-Demo {
    $process = Get-DemoProcess
    if ($process) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        Write-Host "demo: stopped (pid $($process.Id))"
    } else {
        Write-Host 'demo: not running'
    }
    Remove-Item -Force $PidFile -ErrorAction SilentlyContinue
}

function Reset-Demo {
    Stop-Demo
    Start-Sleep -Seconds 1
    foreach ($full in @((Join-Path $Project 'umbraco\Data'), (Join-Path $Project 'umbraco\Logs'), (Join-Path $Project 'wwwroot\media'), $MailDir, $LogFile)) {
        if (Test-Path $full) {
            Remove-Item -Recurse -Force $full
        }
    }
    Write-Host 'demo: reset; the next start is a fresh install (the generated credentials are kept)'
}

switch ($Command) {
    'start' { Start-Demo }
    'stop' { Stop-Demo }
    'reset' { Reset-Demo }
    'login' {
        Get-DemoSecrets | Out-Null
        Write-Host "Backoffice: $Url/umbraco"
        Write-Host 'Credentials: .local\credentials.json (demo.admin and demo.members; the passwords are never printed)'
    }
}
