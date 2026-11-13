# Extract printable ASCII strings from a binary and search for keywords.
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$Pattern = 'host|switch|channel|pair|connect|device|rpc|grpc|pipe|socket',
    [int]$MinLen = 6
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

$unique = $results | Sort-Object -Unique
$matched = $unique | Where-Object { $_ -match $Pattern }
"=== $Path ==="
"Total unique strings: $($unique.Count), matched: $($matched.Count)"
$matched | ForEach-Object { $_ }
