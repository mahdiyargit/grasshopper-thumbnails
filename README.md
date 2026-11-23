# Grasshopper Thumbnails

See the bitmap saved inside your `.gh` and `.ghx` files, both as a thumbnail and in the preview pane in Windows Explorer.

It runs independently of Rhino, so there is no Rhino version to match. You do not need Rhino installed.

## Install

1. [Download the installer](https://github.com/mahdiyargit/grasshopper-thumbnails/releases/latest/download/GrasshopperThumbnails-Setup.exe).
2. Choose **Only for me** or **All users**. All users requires administrator rights.
3. Reopen your Explorer folders. Use **Large icons** for thumbnails and **Alt+P** for the preview pane.

Requires **Windows x64 and .NET Framework 4.8 or newer**. Tested on Windows 11.

Run the installer again to update or uninstall. You can also remove **Grasshopper Thumbnails & Preview** from Windows Installed apps. Uninstall first if you want to change the account scope.

## Notes

- Files need a saved bitmap. Files without one keep their normal icon.
- The app reads the image without running the definition or changing your files.
- Setup refreshes your thumbnail cache. If a thumbnail is missing, refresh or reopen the folder. You can run **Install / update** to clear old cached failures.
- Downloaded files are supported on tested Windows 11 builds. Some Windows settings may still require **Preview anyway**.
- Supports Grasshopper 1 `.gh` and `.ghx` files. Windows ARM64 and Grasshopper 2 `.ghz` files are not supported.

The installer is unsigned. A SHA-256 checksum is included with the [release](https://github.com/mahdiyargit/grasshopper-thumbnails/releases/latest).

<details>
<summary>Build and test</summary>

Run in PowerShell on Windows x64. Packaging requires [Inno Setup 6.7 or newer](https://jrsoftware.org/isdl.php). No Rhino SDK or NuGet packages are needed.

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

Build output goes to `artifacts/bin`; the installer and checksum go to `artifacts/packages`. For a portable compiler, use `Package.ps1 -InstallerCompiler 'C:\path\to\ISCC.exe'`.

See [tests/README.md](tests/README.md) for integration checks and diagnostics.

The reader supports GH1 PNG thumbnails up to 16 MiB, 8192 pixels per dimension, and 16 million pixels total. Archive reads are limited to 256 MiB and have a cooperative 15-second deadline. Blocking system calls may take longer. XML disables DTDs and external resolution. Nonstandard binary layouts may not work.

</details>

## License

Original code and test data use the [MIT license](LICENSE). Bundled McNeel icons are covered separately in [third-party notices](THIRD_PARTY_NOTICES.md); redistribution permission has not been verified. This project is independent of McNeel.
