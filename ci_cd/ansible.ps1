param(
    [Parameter(Mandatory=$true)][string]$playbook,
    [string]$extravars = "{}",
    [switch]$Force
)

function Ensure-Docker-Healthy {
    param(
        [string]$dockerPath = "C:\Program Files\Docker\Docker\Docker Desktop.exe",
        [int]$maxWaitSeconds = 180
    )

    # Check if the engine is actually responsive (not just the process running)
    docker info 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Docker engine is healthy."
        return
    }

    Write-Host "Docker engine is unresponsive. Restarting Docker Desktop..."

    # Kill all Docker processes
    Stop-Process -Name "Docker Desktop" -Force -ErrorAction SilentlyContinue
    Stop-Process -Name "com.docker.backend" -Force -ErrorAction SilentlyContinue
    Stop-Process -Name "com.docker.build" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 5

    # Start Docker Desktop
    Start-Process $dockerPath

    # Wait for the engine to become responsive
    $deadline = (Get-Date).AddSeconds($maxWaitSeconds)
    while ((Get-Date) -lt $deadline) {
        docker info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Docker engine recovered after restart."
            return
        }
        Start-Sleep -Seconds 5
    }

    Write-Error "Docker engine did not recover within $maxWaitSeconds seconds."
    exit 1
}

Ensure-Docker-Healthy

# Inject force flag into extravars if requested
if ($Force) {
    $parsed = $extravars | ConvertFrom-Json
    $parsed | Add-Member -NotePropertyName "force" -NotePropertyValue $true -Force
    $extravars = $parsed | ConvertTo-Json -Compress
}

$Env:PLAYBOOK = "--extra-vars=`"$extravars`" ${playbook}.yml"
echo $Env:PLAYBOOK
docker compose --profile ansible up
