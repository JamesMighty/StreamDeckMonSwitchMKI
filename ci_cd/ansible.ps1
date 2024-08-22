param(
    [Parameter(Mandatory=$true)][string]$playbook,
    [string]$extravars = "{}"
)

function Check-Start-Process {
    param(
        [parameter(Mandatory=$true)][string] $processName,
        [parameter(Mandatory=$true)][string] $processPath,
                                    [int] $timeout = 5
    )
    $processList = Get-Process $processName -ErrorAction SilentlyContinue
    if (!$processList) {
        Start-Process  $processPath
    }
}

Check-Start-Process "Docker Desktop" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
$Env:PLAYBOOK = "--extra-vars=`"$extravars`" ${playbook}.yml"
echo $Env:PLAYBOOK
docker compose --profile ansible up
