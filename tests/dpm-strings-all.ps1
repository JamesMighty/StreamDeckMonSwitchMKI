# Extract ALL printable ASCII strings from a binary (no filtering).
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [int]$MinLen = 4
)

$bytes = [System.IO.File]::ReadAllBytes($Path)
$sb = New-Object System.Text.StringBuilder
$results = New-Object System.Collections.Generic.List[string]

foreach ($b in $bytes) {
    if ($b -ge 32 -and $b -le 126) {
        [void]$sb.Append([char]$b)
    } else {
        if ($sb.Length -ge $MinLen) {
            $results.Add($sb.ToString())
        }
        [void]$sb.Clear()
    }
}
if ($sb.Length -ge $MinLen) { $results.Add($sb.ToString()) }

$results | Sort-Object -Unique
