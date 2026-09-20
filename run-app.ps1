<#
.SYNOPSIS
  Launch/stop/check the Webly backend (Webly.Api) + frontend (client) as background processes.
.DESCRIPTION
  Both processes start detached with stdout/stderr redirected to log files under .run/, and this
  script only prints a short status block. Re-running `start` is idempotent: a tracked process
  already listening on its port is left alone. Ports held by an untracked stale process are cleared.
  `stop` force-kills (a detached process has no console for a graceful close) and then removes any
  Testcontainers Postgres the backend left behind — never the docker-compose one.

  The backend is the single public origin (https://localhost:5000). It serves /api, /health and
  swagger itself and reverse-proxies everything else to the Angular dev server on :4200, so the
  browser only ever talks to :5000. The Angular dev server therefore runs plain HTTP — no dev cert
  needed on the frontend side.

  Dev database: the backend first tries the docker-compose Postgres (docker-compose.dev.yml,
  localhost:5434). If that is not reachable it falls back to a Testcontainers Postgres, which needs
  Docker Desktop running. GET /health reports which one is active.

.PARAMETER Action
  start | stop | status | logs

.PARAMETER Only
  backend | frontend | both (default: both)

.EXAMPLE
  ./run-app.ps1 start
  ./run-app.ps1 status
  ./run-app.ps1 logs -Only backend -Tail 50
  ./run-app.ps1 stop
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'status', 'logs')]
    [string]$Action = 'start',

    [ValidateSet('backend', 'frontend', 'both')]
    [string]$Only = 'both',

    [int]$BackendTimeoutSec = 180,
    [int]$FrontendTimeoutSec = 180,
    [int]$StopTimeoutSec = 20,
    [int]$Tail = 40
)

$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$RunDir = Join-Path $RepoRoot '.run'
$BackendDir = Join-Path $RepoRoot 'Webly.Api'
$ClientDir = Join-Path $RepoRoot 'client'

$Backend = @{
    Name       = 'backend'
    Port       = 5000
    Url        = 'https://localhost:5000/swagger/v1/swagger.json'
    PidFile    = Join-Path $RunDir 'backend.pid'
    OutLog     = Join-Path $RunDir 'backend.out.log'
    ErrLog     = Join-Path $RunDir 'backend.err.log'
    WorkingDir = $BackendDir
}
$Frontend = @{
    Name       = 'frontend'
    Port       = 4200
    Url        = 'http://localhost:4200'
    PidFile    = Join-Path $RunDir 'frontend.pid'
    OutLog     = Join-Path $RunDir 'frontend.out.log'
    ErrLog     = Join-Path $RunDir 'frontend.err.log'
    WorkingDir = $ClientDir
}

function Ensure-RunDir {
    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
}

function Test-PortListening([int]$Port) {
    try {
        return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    } catch {
        return $false
    }
}

function Get-TrackedProcess([hashtable]$Svc) {
    if (-not (Test-Path $Svc.PidFile)) { return $null }
    $procId = (Get-Content $Svc.PidFile -Raw).Trim()
    if (-not $procId) { return $null }
    try {
        return Get-Process -Id $procId -ErrorAction Stop
    } catch {
        return $null
    }
}

function Clear-StalePort([hashtable]$Svc) {
    $conns = Get-NetTCPConnection -LocalPort $Svc.Port -State Listen -ErrorAction SilentlyContinue
    $ownerPids = $conns | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($ownerPid in $ownerPids) {
        $proc = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        Write-Host "[$($Svc.Name)] port $($Svc.Port) held by untracked pid $ownerPid ($($proc.ProcessName)) - clearing it." -ForegroundColor Yellow
        taskkill /PID $ownerPid /T *> $null

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $halfTimeout = [Math]::Max(5, [int]($StopTimeoutSec / 2))
        while ($sw.Elapsed.TotalSeconds -lt $halfTimeout -and (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue)) {
            Start-Sleep -Seconds 1
        }
        if (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue) {
            Write-Host "[$($Svc.Name)] pid $ownerPid didn't exit gracefully - forcing it." -ForegroundColor Yellow
            taskkill /PID $ownerPid /T /F *> $null
            Start-Sleep -Seconds 1
        }
    }
    Remove-Item $Svc.PidFile -ErrorAction SilentlyContinue
}

function Start-Service([hashtable]$Svc, [string]$FilePath, [string[]]$Arguments) {
    $tracked = Get-TrackedProcess $Svc
    if ($tracked -and (Test-PortListening $Svc.Port)) {
        Write-Host "[$($Svc.Name)] already running (pid $($tracked.Id), port $($Svc.Port)) - skipping." -ForegroundColor Yellow
        return
    }
    if (Test-PortListening $Svc.Port) {
        Clear-StalePort $Svc
        if (Test-PortListening $Svc.Port) {
            throw "[$($Svc.Name)] port $($Svc.Port) is still in use after attempting to clear it. Free it manually and retry."
        }
    }

    Write-Host "[$($Svc.Name)] starting..." -ForegroundColor Cyan
    '' | Set-Content $Svc.OutLog
    '' | Set-Content $Svc.ErrLog
    $proc = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $Svc.WorkingDir `
        -RedirectStandardOutput $Svc.OutLog -RedirectStandardError $Svc.ErrLog `
        -WindowStyle Hidden -PassThru
    $proc.Id | Set-Content $Svc.PidFile
    Write-Host "[$($Svc.Name)] pid $($proc.Id), logs: $($Svc.OutLog)"
}

function Test-HttpReady([string]$Url) {
    try {
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
            return ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400)
        }
        # Windows PowerShell 5.1 has no -SkipCertificateCheck; curl.exe ships with Windows 10+.
        $code = & curl.exe -s -k -o NUL -w "%{http_code}" --max-time 5 $Url
        return ($code -match '^[23]\d\d$')
    } catch {
        return $false
    }
}

function Wait-ForHttp([hashtable]$Svc, [int]$TimeoutSec) {
    Write-Host "[$($Svc.Name)] waiting for $($Svc.Url) (timeout ${TimeoutSec}s)..."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $tracked = Get-TrackedProcess $Svc
        if (-not $tracked) {
            throw "[$($Svc.Name)] process exited before becoming ready. Check $($Svc.ErrLog)."
        }
        if (Test-HttpReady $Svc.Url) {
            Write-Host "[$($Svc.Name)] ready." -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 2
    }
    throw "[$($Svc.Name)] did not become ready within ${TimeoutSec}s. Check $($Svc.ErrLog) / $($Svc.OutLog)."
}

function Stop-Service([hashtable]$Svc) {
    $tracked = Get-TrackedProcess $Svc
    if (-not $tracked) {
        Write-Host "[$($Svc.Name)] not running (no tracked pid)." -ForegroundColor Yellow
        Remove-Item $Svc.PidFile -ErrorAction SilentlyContinue
        return
    }

    Write-Host "[$($Svc.Name)] stopping pid $($tracked.Id)..." -ForegroundColor Cyan
    # Force-kill the tree. A detached, hidden-window process has no console to receive a graceful
    # close, so `taskkill` without /F reliably times out here. The cost is that the backend cannot
    # tear down its own Testcontainers Postgres — Remove-OrphanTestcontainers below does that.
    taskkill /PID $tracked.Id /T /F *> $null

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $StopTimeoutSec) {
        if ($tracked.HasExited -or -not (Get-Process -Id $tracked.Id -ErrorAction SilentlyContinue)) {
            Remove-Item $Svc.PidFile -ErrorAction SilentlyContinue
            if ($Svc.Name -eq 'backend') { Remove-OrphanTestcontainers }
            Write-Host "[$($Svc.Name)] stopped." -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 1
    }
    Write-Host "[$($Svc.Name)] pid $($tracked.Id) is still alive ${StopTimeoutSec}s after a force-kill; stop it manually." -ForegroundColor Red
}

function Remove-OrphanTestcontainers {
    # Only the Testcontainers fallback containers — never the docker-compose one (webly-postgres-dev).
    $rows = docker ps --filter 'ancestor=postgres:17' --format '{{.ID}} {{.Names}}' 2>$null
    $ids = @($rows | Where-Object { $_ -and ($_ -split '\s+')[1] -ne 'webly-postgres-dev' } | ForEach-Object { ($_ -split '\s+')[0] })
    # By name, not `ancestor=testcontainers/ryuk`: an untagged ancestor filter only matches :latest.
    $ryuk = @(docker ps -q --filter 'name=testcontainers-ryuk' 2>$null | Where-Object { $_ })
    $all = $ids + $ryuk
    if ($all.Count -gt 0) {
        docker rm -f @all *> $null
        Write-Host "[backend] removed $($ids.Count) orphaned Testcontainers Postgres + $($ryuk.Count) ryuk container(s)." -ForegroundColor Yellow
    }
}

function Show-Status([hashtable]$Svc) {
    $tracked = Get-TrackedProcess $Svc
    $listening = Test-PortListening $Svc.Port
    if ($tracked -and $listening) {
        Write-Host "[$($Svc.Name)] UP   pid $($tracked.Id)  port $($Svc.Port)  $($Svc.Url)" -ForegroundColor Green
    } elseif ($tracked -and -not $listening) {
        Write-Host "[$($Svc.Name)] STARTING pid $($tracked.Id)  port $($Svc.Port) not listening yet" -ForegroundColor Yellow
    } elseif ($listening) {
        Write-Host "[$($Svc.Name)] port $($Svc.Port) is listening but not owned by a tracked pid (started outside this script?)" -ForegroundColor Yellow
    } else {
        Write-Host "[$($Svc.Name)] DOWN" -ForegroundColor DarkGray
    }
}

function Show-Logs([hashtable]$Svc, [int]$N) {
    Write-Host "==== [$($Svc.Name)] stdout (last $N) ====" -ForegroundColor Cyan
    if (Test-Path $Svc.OutLog) { Get-Content $Svc.OutLog -Tail $N } else { Write-Host '(no log yet)' }
    Write-Host "==== [$($Svc.Name)] stderr (last $N) ====" -ForegroundColor Cyan
    if (Test-Path $Svc.ErrLog) { Get-Content $Svc.ErrLog -Tail $N } else { Write-Host '(no log yet)' }
}

$targets = switch ($Only) {
    'backend'  { @($Backend) }
    'frontend' { @($Frontend) }
    default    { @($Backend, $Frontend) }
}

switch ($Action) {
    'start' {
        Ensure-RunDir

        if ($targets -contains $Backend) {
            Start-Service $Backend 'dotnet' @('run', '-c', 'Debug', '--launch-profile', 'https')
            Wait-ForHttp $Backend $BackendTimeoutSec
        }
        if ($targets -contains $Frontend) {
            # `yarn start` runs the prestart hook (ng-openapi-gen) against the live swagger, hence backend-first.
            # yarn on Windows is yarn.cmd — Start-Process can't launch a .cmd directly, route through cmd.exe.
            Start-Service $Frontend 'cmd.exe' @('/c', 'yarn start')
            Wait-ForHttp $Frontend $FrontendTimeoutSec
        }

        Write-Host ''
        Write-Host '==== ready ====' -ForegroundColor Green
        foreach ($svc in $targets) { Show-Status $svc }
        if ($targets -contains $Backend) {
            Write-Host 'Open https://localhost:5000 (the backend proxies the Angular app).' -ForegroundColor Green
        }
    }
    'stop' {
        # Frontend first, then backend.
        foreach ($svc in @($targets | Sort-Object { $_.Name -eq 'backend' })) { Stop-Service $svc }
    }
    'status' {
        foreach ($svc in $targets) { Show-Status $svc }
    }
    'logs' {
        foreach ($svc in $targets) { Show-Logs $svc $Tail }
    }
}
