param([string]$BuildDirectory = (Join-Path $PSScriptRoot '..\artifacts\bin'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$diagnostics = Join-Path $BuildDirectory 'GrasshopperFilePreview.Diagnostics.exe'
$output = Join-Path $PSScriptRoot ('..\artifacts\blocked-fixtures\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $output -Force
foreach ($zone in @(3, 4)) {
    foreach ($extension in @('gh', 'ghx')) {
        $file = Join-Path $output "Blocked zone $zone.$extension"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fixtures\Embedded bitmap.$extension") -Destination $file
        Set-Content -LiteralPath $file -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=$zone" -Encoding Ascii
        $hash = (Get-FileHash -LiteralPath $file).Hash
        $marker = Get-Content -LiteralPath $file -Stream Zone.Identifier -Raw
        & $diagnostics --read $file
        if ($LASTEXITCODE -ne 0) { throw 'Blocked-file read failed.' }
        & $diagnostics --probe $file
        if ($LASTEXITCODE -ne 0) { throw 'Blocked-file COM lifecycle failed. Install the current build first.' }
        & $diagnostics --thumbnail $file
        if ($LASTEXITCODE -ne 0) { throw 'Blocked-file Shell thumbnail failed.' }
        if ($hash -ne (Get-FileHash -LiteralPath $file).Hash -or
            $marker -cne (Get-Content -LiteralPath $file -Stream Zone.Identifier -Raw)) {
            throw 'File contents or its download marker changed during preview.'
        }
        "PASS .$extension zone ${zone}: read, COM lifecycle, thumbnail, unchanged file and download marker"
    }
}
# COM activation exercises the provider but does not reproduce Explorer's gate.
# Also select these files in Explorer: images should appear without clicking
# Preview anyway on supported builds. A blocked HTML control must remain blocked.
"Fixtures for the additional Explorer check: $output"
