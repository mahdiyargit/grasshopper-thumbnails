param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\bin'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler is required.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$common = @('/nologo', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
Push-Location $PSScriptRoot
try {
    $library = Join-Path $OutputDirectory 'GrasshopperFilePreview.dll'
    & $compiler @common /target:library "/out:$library" src\ProductInfo.cs src\BitmapReader.cs src\Handler.cs src\ThumbnailHandler.cs src\ShellInterfaces.cs src\ComInput.cs src\PreviewSurface.cs src\PreviewSession.cs
    if ($LASTEXITCODE -ne 0) { throw 'Preview DLL build failed.' }
    & $compiler @common /r:System.Runtime.Serialization.dll /target:winexe "/out:$OutputDirectory\GrasshopperFilePreview.Setup.exe" "/r:$library" src\ProductInfo.cs src\App.cs src\Registration.cs src\RegistryJournal.cs src\ThumbnailCache.cs
    if ($LASTEXITCODE -ne 0) { throw 'Setup app build failed.' }
    & $compiler @common /r:System.Runtime.Serialization.dll /target:exe "/out:$OutputDirectory\GrasshopperFilePreview.Diagnostics.exe" "/r:$library" src\ProductInfo.cs src\Diagnostics.cs src\Registration.cs src\RegistryJournal.cs src\ThumbnailCache.cs
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostics build failed.' }
    Copy-Item -LiteralPath 'assets\Grasshopper.ico', 'assets\GrasshopperXml.ico', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md' -Destination $OutputDirectory -Force
    Copy-Item -LiteralPath 'src\App.config' -Destination (Join-Path $OutputDirectory 'GrasshopperFilePreview.Setup.exe.config') -Force
    Copy-Item -LiteralPath 'src\App.config' -Destination (Join-Path $OutputDirectory 'GrasshopperFilePreview.Diagnostics.exe.config') -Force
    "Built Windows x64 release in $OutputDirectory"
}
finally { Pop-Location }
