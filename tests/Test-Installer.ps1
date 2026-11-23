param(
    [string]$Installer = (Join-Path $PSScriptRoot '..\artifacts\packages\GrasshopperThumbnails-Setup.exe'),
    [switch]$RemoveAfter,
    [switch]$SkipShellIntegration,
    [switch]$AllUsers
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$buildDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\bin'))
$scratch = Join-Path $PSScriptRoot ('..\artifacts\installer-tests\' + [Guid]::NewGuid().ToString('N') + ' ' + [char]0x03A9)
$null = New-Item -ItemType Directory -Path $scratch -Force
$scratch = (Resolve-Path -LiteralPath $scratch).Path
$standalone = Join-Path $scratch 'Grasshopper Thumbnails & Preview.exe'
Copy-Item -LiteralPath $Installer -Destination $standalone
$hive = if ($AllUsers) { 'HKEY_LOCAL_MACHINE' } else { 'HKEY_CURRENT_USER' }
$registryRoot = 'Registry::' + $hive + '\'
$settings = $registryRoot + 'Software\GrasshopperBitmapPreview'
$uninstall = $registryRoot + 'Software\Microsoft\Windows\CurrentVersion\Uninstall\GrasshopperFilePreview.Bundle_is1'
$legacyUninstall = $registryRoot + 'Software\Microsoft\Windows\CurrentVersion\Uninstall\GrasshopperBitmapPreview'
if ($AllUsers -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'All-users tests require an elevated disposable Windows account or CI runner.'
}

function Run-Uninstaller([string]$LogName) {
    $command = (Get-ItemProperty -LiteralPath $uninstall).UninstallString.Trim('"')
    if (-not (Test-Path -LiteralPath $command)) { throw 'Windows uninstaller is missing.' }
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $scratch $LogName) + '"'
    $process = Start-Process -FilePath $command -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $null = $process.Handle # Retain ExitCode on Windows PowerShell 5.1.
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Uninstaller timed out.' }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

function Run-Bundle([string]$Log, [string]$ScopeOverride = '') {
    $scope = if ($ScopeOverride) { $ScopeOverride } elseif ($AllUsers) { '/ALLUSERS' } else { '/CURRENTUSER' }
    $arguments = $scope + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + $Log + '"'
    $process = Start-Process -FilePath $standalone -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $null = $process.Handle
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Installer timed out.' }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

function Read-RegistrationState {
    $classes = $registryRoot + 'Software\Classes\'
    $roots = @($settings, $uninstall, $legacyUninstall,
        ($classes + 'CLSID\{9A67F1FF-DC27-4647-8BBD-81789C9BB7FC}'),
        ($classes + 'CLSID\{40E6D89F-3412-4FB6-9D32-1A56D4C4869B}'),
        ($classes + 'AppID\{2960CA73-5264-4BD0-953C-A84C57147A9A}'))
    foreach ($extension in @('.gh', '.ghx')) { $roots += $classes + $extension + '\shellex' }
    $backups = Join-Path $settings 'OverlayBackups'
    if (Test-Path -LiteralPath $backups) {
        foreach ($key in Get-ChildItem -LiteralPath $backups) { $roots += $classes + $key.PSChildName }
    }
    $values = foreach ($path in $roots) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $keys = @(Get-Item -LiteralPath $path) + @(Get-ChildItem -LiteralPath $path -Recurse)
        foreach ($key in $keys) {
            foreach ($name in $key.GetValueNames()) {
                [pscustomobject]@{ Path=$key.Name; Name=$name; Kind=$key.GetValueKind($name).ToString(); Value=$key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
            }
        }
    }
    return ConvertTo-Json -InputObject @($values | Sort-Object Path,Name) -Depth 8 -Compress
}

# A failed child setup must not be reported as a successful bundle installation.
$mutex = New-Object System.Threading.Mutex($false, 'Local\GrasshopperBitmapPreview.Setup')
$locked = $false
try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw 'Another setup is running; retry this test after it closes.' }
    if ((Run-Bundle (Join-Path $scratch 'failure.log')) -eq 0) { throw 'Bundle swallowed the setup failure.' }
    'PASS installer propagates child setup failure'
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

# A native file-copy failure happens after the worker has registered the handlers.
# The installer must restore the previous registration and Installed apps metadata.
$before = Read-RegistrationState
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $buildDirectory 'GrasshopperFilePreview.Setup.exe'))
$stage = $assembly.GetType('GrasshopperFilePreview.Registration').GetMethod('StagePayload', [Reflection.BindingFlags]'NonPublic,Static')
$staged = [string]$stage.Invoke($null, @($buildDirectory, $AllUsers.IsPresent))
$staged = [IO.Path]::GetFullPath($staged)
$baseFolder = if ($AllUsers) { $env:ProgramFiles } else { $env:LOCALAPPDATA }
$versions = [IO.Path]::GetFullPath((Join-Path $baseFolder 'GrasshopperFilePreview\versions')) + '\'
if (-not $staged.StartsWith($versions, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected staging directory.' }
$readme = Join-Path $staged 'README.md'
$createdReadme = -not (Test-Path -LiteralPath $readme)
$restored = $false
$readLock = $null
try {
    if ($createdReadme) { [IO.File]::WriteAllText($readme, 'Installer rollback test') }
    $readLock = [IO.File]::Open($readme, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failureLog = Join-Path $scratch 'file-copy-failure.log'
    if ((Run-Bundle $failureLog) -eq 0) { throw 'Expected native file-copy failure.' }
    if (-not (Select-String -LiteralPath $failureLog -SimpleMatch 'Previous handler registration restored.')) { throw 'Native failure did not restore handler registration.' }
    if ((Read-RegistrationState) -cne $before) { throw 'Failed installation changed the previous registration.' }
    $restored = $true
    'PASS native installation failure restores previous registration and Installed apps metadata'
}
finally {
    if ($readLock) { $readLock.Dispose() }
    if ($createdReadme) { [IO.File]::Delete($readme) }
    if (-not $restored -and (Read-RegistrationState) -cne $before) {
        if ((Run-Bundle (Join-Path $scratch 'restore-after-failure.log')) -ne 0) { Write-Warning 'Setup recovery failed; run the installer to restore the handlers.' }
    }
}

$log = Join-Path $scratch 'install.log'
if ((Run-Bundle $log) -ne 0) { throw "Standalone installation failed; see $log" }
$installed = (Get-ItemProperty -LiteralPath $settings).InstallDirectory
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildDirectory 'GrasshopperFilePreview.dll')).ProductVersion
$entry = Get-ItemProperty -LiteralPath $uninstall
if ($entry.DisplayName -ne 'Grasshopper Thumbnails & Preview' -or $entry.DisplayVersion -ne $version) {
    throw 'Installed app metadata does not match the bundle.'
}
if ((Test-Path -LiteralPath $legacyUninstall) -and (Get-Item -LiteralPath $legacyUninstall).GetValue('DisplayName')) {
    throw 'Upgrade left duplicate Installed apps entries.'
}
if (-not (Test-Path -LiteralPath $entry.UninstallString.Trim('"'))) { throw 'Standard Windows uninstaller missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $installed 'README.md'))) { throw 'Installer and worker payload directories differ.' }
foreach ($name in @('GrasshopperFilePreview.dll', 'GrasshopperFilePreview.Setup.exe',
    'GrasshopperFilePreview.Setup.exe.config', 'GrasshopperFilePreview.Diagnostics.exe',
    'GrasshopperFilePreview.Diagnostics.exe.config', 'Grasshopper.ico', 'GrasshopperXml.ico',
    'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $installed $name)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $buildDirectory $name)).Hash) {
        throw "Installed payload differs: $name"
    }
}
'PASS standalone EXE installs every runtime file from a path with spaces and Unicode'
$temporary = Select-String -LiteralPath $log -Pattern 'Bundle extraction directory: (.+)' | Select-Object -First 1
if (-not $temporary) { throw "The installer did not report its extraction directory; see $log" }
$extracted = $temporary.Matches[0].Groups[1].Value.Trim()
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ((Test-Path -LiteralPath $extracted) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if (Test-Path -LiteralPath $extracted) {
    Get-Content -LiteralPath $log | Select-String 'temporary|delete|Deinitializing|error'
    Get-ChildItem -LiteralPath $extracted -Force | Select-Object Name
    throw 'Installer temporary files were not cleaned up within 10 seconds.'
}
'PASS temporary extraction directory is removed after setup exits'

if ($AllUsers) {
    $before = Read-RegistrationState
    $scopeLog = Join-Path $scratch 'opposite-scope.log'
    if ((Run-Bundle $scopeLog '/CURRENTUSER') -eq 0) { throw 'Installer allowed conflicting current-user and all-users copies.' }
    if (-not (Select-String -LiteralPath $scopeLog -SimpleMatch 'An all-users installation already exists.')) { throw 'Opposite-scope rejection did not explain how to proceed.' }
    if ((Read-RegistrationState) -cne $before) { throw 'Opposite-scope attempt changed the all-users installation.' }
    'PASS current-user installation cannot overwrite an existing all-users installation'
}

# Failed removal must preserve the registration and files so the user can retry.
$mutex = New-Object System.Threading.Mutex($false, 'Local\GrasshopperBitmapPreview.Setup')
$locked = $false
try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw 'Another setup is running.' }
    if ((Run-Uninstaller 'failed-removal.log') -eq 0) { throw 'Uninstaller swallowed handler removal failure.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installed 'GrasshopperFilePreview.dll'))) { throw 'Failed removal deleted the provider.' }
    if ((Get-ItemProperty -LiteralPath $settings).InstallDirectory -ne $installed) { throw 'Failed removal changed registration.' }
    'PASS failed uninstall preserves installed files and registration'
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

if ($SkipShellIntegration) {
    'SKIP desktop Shell integration (requires a non-elevated Windows session)'
}
else {
    # Reproduce Windows caching a failed thumbnail while the handler is uninstalled.
    # These must recover through an ordinary Shell request after reinstall, without
    # WTS_FORCEEXTRACTION, touching timestamps, or selecting a file in the preview pane.
    function Run-Diagnostics([string]$Command, [string]$File, [string]$LogName) {
        $process = Start-Process -FilePath (Join-Path $buildDirectory 'GrasshopperFilePreview.Diagnostics.exe') `
            -ArgumentList ($Command + ' "' + $File + '"') -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $scratch ($LogName + '.out.log')) `
            -RedirectStandardError (Join-Path $scratch ($LogName + '.err.log'))
        try {
            $null = $process.Handle
            if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Shell diagnostic request timed out.' }
            return $process.ExitCode
        }
        finally { $process.Dispose() }
    }

    foreach ($extension in @('gh', 'ghx')) {
        $baseline = Join-Path $scratch ('Fresh bitmap.' + $extension)
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fixtures\Embedded bitmap.$extension") -Destination $baseline
        if ((Run-Diagnostics '--thumbnail' $baseline "baseline-$extension") -ne 0) {
            $details = Get-Content -LiteralPath (Join-Path $scratch "baseline-$extension.err.log") -Raw
            throw "Fresh Shell thumbnail failed before cache regression: $details"
        }
        if ((Run-Diagnostics '--probe' $baseline "preview-$extension") -ne 0) {
            $details = Get-Content -LiteralPath (Join-Path $scratch "preview-$extension.err.log") -Raw
            throw "Registered preview lifecycle failed: $details"
        }
    }
    'PASS fresh Shell thumbnails and registered preview lifecycle for both formats'

    $reinstalled = $false
    try {
        if ((Run-Uninstaller 'uninstall-before-cache.log') -ne 0) { throw 'Uninstall before cache regression test failed.' }
        if (Test-Path -LiteralPath (Join-Path $installed 'GrasshopperFilePreview.Setup.exe')) { throw 'Uninstaller left its runtime worker behind.' }
        if (Test-Path -LiteralPath (Join-Path $installed 'GrasshopperFilePreview.Diagnostics.exe')) { throw 'Uninstaller left diagnostics behind.' }
        'PASS standard uninstaller removes its installed runtime files'
        $cachedFiles = @()
        foreach ($extension in @('gh', 'ghx')) {
            foreach ($blocked in @($false, $true)) {
                $folder = Join-Path $scratch ("Cached $extension $blocked " + [char]0x03A9)
                $null = New-Item -ItemType Directory -Path $folder
                $file = Join-Path $folder ('Saved bitmap.' + $extension)
                Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fixtures\Embedded bitmap.$extension") -Destination $file
                if ($blocked) { Set-Content -LiteralPath $file -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3" -Encoding Ascii }
                $marker = if ($blocked) { Get-Content -LiteralPath $file -Stream Zone.Identifier -Raw } else { $null }
                $cachedFiles += [pscustomobject]@{ Path=$file; Hash=(Get-FileHash -LiteralPath $file).Hash; Modified=(Get-Item -LiteralPath $file).LastWriteTimeUtc; Marker=$marker }
                if ((Run-Diagnostics '--thumbnail' $file "before-$extension-$blocked") -eq 0) { throw 'Expected a failed thumbnail while uninstalled.' }
            }
        }
        'PASS reproduced cached thumbnail failures in four folders for both formats'
        if ((Run-Bundle (Join-Path $scratch 'reinstall.log')) -ne 0) { throw 'Reinstallation failed.' }
        $reinstalled = $true
        $index = 0
        foreach ($file in $cachedFiles) {
            $logName = 'after-' + $index++
            if ((Run-Diagnostics '--thumbnail' $file.Path $logName) -ne 0) {
                $details = Get-Content -LiteralPath (Join-Path $scratch ($logName + '.err.log')) -Raw
                throw ('Cached failure survived reinstall: ' + $file.Path + [Environment]::NewLine + $details)
            }
            if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Hash -or (Get-Item -LiteralPath $file.Path).LastWriteTimeUtc -ne $file.Modified) { throw 'Cache repair changed a source file.' }
            if ($file.Marker -and (Get-Content -LiteralPath $file.Path -Stream Zone.Identifier -Raw) -cne $file.Marker) { throw 'Cache repair changed a download marker.' }
        }
        'PASS reinstall restores normal thumbnails across folders, including blocked files, without changing source files'
    }
    finally {
        # Do not leave an existing installation removed if the regression check fails.
        if (-not $reinstalled) {
            $recovery = Run-Bundle (Join-Path $scratch 'restore.log')
            if ($recovery -ne 0) { Write-Warning 'Setup recovery failed; run the installer to restore the handlers.' }
        }
    }
}

if ($RemoveAfter) {
    if ((Run-Uninstaller 'final-removal.log') -ne 0) { throw 'Installed uninstaller failed.' }
    if (Test-Path -LiteralPath $uninstall) { throw 'Installed apps entry remained after removal.' }
    foreach ($extension in @('.gh', '.ghx')) {
        foreach ($category in @('{8895B1C6-B41F-4C1C-A562-0D564250836F}', '{E357FCCD-A995-4576-B01F-234630154E96}')) {
            $path = $registryRoot + 'Software\Classes\' + $extension + '\shellex\' + $category
            if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).GetValue('')) { throw 'Handler mapping remained after removal.' }
        }
    }
    'PASS installed uninstaller removes both handlers and its Installed apps entry'
}
