param(
    [string]$Destination = (Join-Path $PSScriptRoot 'artifacts\packages'),
    [string]$InstallerCompiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$null = New-Item -ItemType Directory -Path $Destination -Force
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not $InstallerCompiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $InstallerCompiler = $command.Source }
    else {
        $InstallerCompiler = @(
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $InstallerCompiler -or -not (Test-Path -LiteralPath $InstallerCompiler)) {
    throw 'Inno Setup 6.7 or newer is required to package the installer. Supply -InstallerCompiler with the path to ISCC.exe.'
}
$binaryNames = @('GrasshopperFilePreview.dll', 'GrasshopperFilePreview.Setup.exe',
    'GrasshopperFilePreview.Diagnostics.exe', 'Grasshopper.ico', 'GrasshopperXml.ico', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
    'GrasshopperFilePreview.Setup.exe.config', 'GrasshopperFilePreview.Diagnostics.exe.config', 'README.md')
$binaryFiles = @($binaryNames | ForEach-Object { Join-Path $PSScriptRoot "artifacts\bin\$_" })
foreach ($file in $binaryFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Required release file is missing: $file" }
}
$buildDirectory = Join-Path $PSScriptRoot 'artifacts\bin'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildDirectory 'GrasshopperFilePreview.dll')).ProductVersion
# Match Registration.StagePayload's ordered hashes; the installer test verifies this contract.
$hasher = [Security.Cryptography.SHA256]::Create()
try {
    $hashInput = ($binaryFiles | Where-Object { [IO.Path]::GetFileName($_) -ne 'README.md' } | ForEach-Object {
        $stream = [IO.File]::OpenRead($_)
        try { [Convert]::ToBase64String($hasher.ComputeHash($stream)) }
        finally { $stream.Dispose() }
    }) -join "`n"
    $signature = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput))).Replace('-', '').Substring(0,16)
}
finally { $hasher.Dispose() }
$payloadDirectory = $version + '-' + $signature
& $InstallerCompiler /Qp "/DBuildDirectory=$buildDirectory" "/DOutputDirectory=$Destination" "/DAppVersion=$version" "/DPayloadDirectory=$payloadDirectory" (Join-Path $PSScriptRoot 'installer\Bundle.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $Destination 'GrasshopperThumbnails-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllLines((Join-Path $Destination 'SHA256SUMS.txt'), @("$hash  $([IO.Path]::GetFileName($installer))"), [Text.Encoding]::ASCII)
"Created Windows installer and checksum in $Destination"
