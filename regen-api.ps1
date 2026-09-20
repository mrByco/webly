<#
.SYNOPSIS
  Regenerates the Angular API client (client/src/app/api) from the backend Swagger spec.
.DESCRIPTION
  1. Reuses a backend already serving Swagger on :5000, or starts one in the background.
  2. Waits for Swagger to be reachable.
  3. Runs ng-openapi-gen to regenerate client/src/app/api/.
  4. Stops only a backend this script started (never one that was already running), so it
     composes with run-app.ps1 instead of fighting it for the port.
#>
[CmdletBinding()]
param(
    [string]$BackendDir = (Join-Path $PSScriptRoot 'Webly.Api'),
    [string]$ClientDir = (Join-Path $PSScriptRoot 'client'),
    [string]$SwaggerUrl = 'https://localhost:5000/swagger/v1/swagger.json',
    [int]$BackendStartTimeoutSec = 120,
    [int]$BackendPollIntervalSec = 2
)

$ErrorActionPreference = 'Stop'
$backendProc = $null

function Test-SwaggerReady {
    param([string]$Url)
    try {
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            return (Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -SkipCertificateCheck).StatusCode -eq 200
        }
        # Windows PowerShell 5.1 has no -SkipCertificateCheck and fails the TLS handshake against
        # the Kestrel dev cert; curl.exe ships with Windows 10+ and handles both.
        return (& curl.exe -s -k -o NUL -w "%{http_code}" --max-time 5 $Url) -eq '200'
    } catch {
        return $false
    }
}

try {
    if (Test-SwaggerReady -Url $SwaggerUrl) {
        Write-Host 'Backend already serving Swagger; reusing it.' -ForegroundColor Green
    }
    else {
        Write-Host 'Starting backend (Debug)...' -ForegroundColor Cyan
        $backendProc = Start-Process -FilePath 'dotnet' -ArgumentList 'run','-c','Debug','--launch-profile','https' -WorkingDirectory $BackendDir -PassThru -WindowStyle Hidden
        Write-Host "Backend PID: $($backendProc.Id)"
    }

    $ready = $null -eq $backendProc
    if (-not $ready) {
        Write-Host 'Waiting for Swagger to be reachable...'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt $BackendStartTimeoutSec) {
            if ($backendProc.HasExited) {
                throw "Backend process exited unexpectedly (code $($backendProc.ExitCode))."
            }
            Start-Sleep -Seconds $BackendPollIntervalSec
            if (Test-SwaggerReady -Url $SwaggerUrl) {
                $ready = $true
                Write-Host 'Swagger is ready.' -ForegroundColor Green
                break
            }
        }
        $sw.Stop()
    }
    if (-not $ready) {
        throw "Backend did not expose Swagger within $BackendStartTimeoutSec seconds."
    }

    Write-Host 'Regenerating Angular API client...' -ForegroundColor Cyan
    Push-Location $ClientDir
    try {
        $genConfigPath = Join-Path $ClientDir 'open-api-gen.json'
        $genConfig = Get-Content $genConfigPath -Raw | ConvertFrom-Json
        $genConfig.input = $SwaggerUrl
        $tempConfigPath = Join-Path $ClientDir 'open-api-gen.temp.json'
        # BOM-less: Windows PowerShell 5.1 -Encoding UTF8 emits a BOM, which the generator's JSON parser rejects.
        [System.IO.File]::WriteAllText(
            $tempConfigPath,
            ($genConfig | ConvertTo-Json -Depth 5),
            (New-Object System.Text.UTF8Encoding $false))
        $prevTlsReject = $env:NODE_TLS_REJECT_UNAUTHORIZED
        try {
            # Backend uses the self-signed Kestrel dev cert; Node rejects it unless this is disabled.
            $env:NODE_TLS_REJECT_UNAUTHORIZED = '0'
            $prevEap = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & npx ng-openapi-gen --config open-api-gen.temp.json 2>&1 | ForEach-Object { "$_" }
            }
            finally {
                $ErrorActionPreference = $prevEap
            }
            if ($LASTEXITCODE -ne 0) {
                throw "ng-openapi-gen exited with code $LASTEXITCODE."
            }
        }
        finally {
            $env:NODE_TLS_REJECT_UNAUTHORIZED = $prevTlsReject
            Remove-Item $tempConfigPath -ErrorAction SilentlyContinue
        }
    }
    finally {
        Pop-Location
    }
    Write-Host 'API client regenerated.' -ForegroundColor Green
}
finally {
    if ($backendProc -and -not $backendProc.HasExited) {
        Write-Host 'Stopping backend...' -ForegroundColor Yellow
        try { taskkill /PID $backendProc.Id /T /F *> $null } catch {}
        try { Stop-Process -Id $backendProc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
}
