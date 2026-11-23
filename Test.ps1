param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'artifacts\bin'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $BuildDirectory 'Tests.exe'
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /platform:x64 /target:exe /warn:4 /warnaserror+ "/out:$runner" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll "/r:$BuildDirectory\GrasshopperFilePreview.dll" tests\PortableTests.cs tests\ThumbnailTests.cs tests\StreamTests.cs tests\SetupTests.cs src\ComInput.cs src\Registration.cs src\RegistryJournal.cs src\ThumbnailCache.cs src\ProductInfo.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $runner (Join-Path $PSScriptRoot 'tests\fixtures')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally { Pop-Location }
