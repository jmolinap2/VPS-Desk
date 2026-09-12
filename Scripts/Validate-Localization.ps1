param(
    [string]$Root = (Join-Path $PSScriptRoot '..' 'src' 'VpsDesk.Desktop' 'Localization' 'Languages')
)

$ErrorActionPreference = 'Stop'

$enPath = Join-Path $Root 'en-US' 'Strings.json'
$esPath = Join-Path $Root 'es-ES' 'Strings.json'

foreach ($path in @($enPath, $esPath)) {
    if (-not (Test-Path $path)) {
        throw "Missing localization pack: $path"
    }
}

$en = Get-Content $enPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
$es = Get-Content $esPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable

$missingInEs = @($en.Keys | Where-Object { -not $es.ContainsKey($_) } | Sort-Object)
$extraInEs = @($es.Keys | Where-Object { -not $en.ContainsKey($_) } | Sort-Object)
$emptyEn = @($en.GetEnumerator() | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Value) } | ForEach-Object Key | Sort-Object)
$emptyEs = @($es.GetEnumerator() | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Value) } | ForEach-Object Key | Sort-Object)

if ($missingInEs.Count -gt 0) {
    Write-Error ("Spanish pack is missing keys:`n - " + ($missingInEs -join "`n - "))
}
if ($extraInEs.Count -gt 0) {
    Write-Error ("Spanish pack has keys not present in the English fallback:`n - " + ($extraInEs -join "`n - "))
}
if ($emptyEn.Count -gt 0) {
    Write-Error ("English pack has empty values:`n - " + ($emptyEn -join "`n - "))
}
if ($emptyEs.Count -gt 0) {
    Write-Error ("Spanish pack has empty values:`n - " + ($emptyEs -join "`n - "))
}

Write-Host "Localization packs OK: $($en.Count) keys in en-US and es-ES."
