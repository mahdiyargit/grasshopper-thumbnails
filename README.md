# Grasshopper Thumbnails

*Embedded bitmap thumbnails and previews for .gh and .ghx files in Windows Explorer.*

Grasshopper Thumbnails & Preview displays the image saved inside a Grasshopper 1
file, with blue GH and purple GHX thumbnail badges. It never opens or solves the
definition. **Rhino is not required.**

## Install

1. [Download the Windows installer](https://github.com/mahdiyargit/grasshopper-thumbnails/releases/latest/download/GrasshopperThumbnails-Setup.exe).
2. Choose **Only for me** (default, no administrator rights) or **All users**
   (requires administrator rights).
3. Reopen Explorer folders. Use **Large icons** for thumbnails and **Alt+P** for
   the preview pane.

Requires **Windows x64 and .NET Framework 4.8 or newer**. Tested on Windows 11.
Windows ARM64 and Grasshopper 2 `.ghz` files are not supported.

Run the same installer to update or uninstall, or remove **Grasshopper Thumbnails
& Preview** from Windows Installed apps. Choose the same account scope for updates;
uninstall before changing scope. Other accounts' personal installations remain
independent and take precedence over an all-users installation.

The installer contains the app, icons, and licenses. It installs under
`%LOCALAPPDATA%\GrasshopperFilePreview` or `%ProgramFiles%\GrasshopperFilePreview`.
It preserves file-opening associations and restores previous thumbnail badge
settings when removing the app. Files still locked by Explorer may remain after removal.

Setup refreshes the thumbnail cache for the account that started it. Windows
rebuilds other thumbnails as you browse; other accounts retain their own caches.
File contents, timestamps, and download markers are preserved. Failed file
installation restores the previous handler registration.

The installer is unsigned. Its SHA-256 checksum is included with the release.

## Notes

- Files need a saved bitmap. Without one, Explorer keeps the normal icon and the
  preview pane displays a message. Image quality depends on the saved bitmap.
- If a thumbnail is missing, enable thumbnails in Explorer's Folder Options.
  Run **Install / update** to clear stale cached failures. Immediately after
  setup, Windows may briefly report a thumbnail as unavailable; refresh or reopen
  the folder. Individual files do not need changing.
- Downloaded-file previews are supported on tested Windows 11 builds. This uses
  an undocumented per-handler capability; other builds or managed policies may
  require **Preview anyway**. Windows handler isolation stays enabled.
- Conflicting preview or thumbnail handlers must be removed first. Distinct GH/GHX
  badges require distinct file-type associations; shared associations are skipped.

## Build and test

Run in PowerShell on Windows x64. Building uses the .NET Framework compiler;
packaging requires [Inno Setup 6.7 or newer](https://jrsoftware.org/isdl.php).
No Rhino SDK or NuGet packages are needed.

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

Build output goes to `artifacts/bin`. The installer and checksum go to
`artifacts/packages`. For a portable compiler, use
`Package.ps1 -InstallerCompiler 'C:\path\to\ISCC.exe'`.
See [tests/README.md](tests/README.md) for integration checks and diagnostics.

The reader supports GH1 PNG thumbnails with bounded reads: 256 MiB archive,
16 MiB image data, 8192 pixels per dimension, and 16 million pixels total.
Reads have a cooperative 15-second deadline; blocking system calls may take longer.
XML disables DTDs and external resolution. The binary reader is a bounded scanner,
not a general archive validator; nonstandard layouts may not work.

## License

Original code, documentation, and test data use the [MIT license](LICENSE).
Bundled McNeel icons are excluded; see [third-party notices](THIRD_PARTY_NOTICES.md)
for attribution and the unverified status of icon redistribution permission.
This independent utility is not endorsed by McNeel.
