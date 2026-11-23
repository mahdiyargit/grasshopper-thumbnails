# Tests and diagnostics

`Build.ps1` and `Test.ps1` require Windows x64 and .NET Framework 4.8. The suite
covers image decoding, malformed data, limits, cancellation, native thumbnails,
COM streams, file staging, registry rollback, association preservation, paths,
and GH/GHX badge selection. Registry tests use isolated temporary keys.

## Integration checks

Run from the repository root after building and packaging:

```powershell
# Installs/reinstalls for the current account. Use a non-elevated desktop session.
.\tests\Test-Installer.ps1

# Requires installed handlers; tests both formats with download zones 3 and 4.
.\tests\Test-BlockedFiles.ps1

# Elevated disposable account/CI only: tests Program Files installation and removal.
.\tests\Test-Installer.ps1 -AllUsers -RemoveAfter
```

These tests change handler registration and reset the current account's thumbnail
cache. They check payload hashes, installation rollback, failed removal, scope
conflicts, registered preview lifecycles, and thumbnail-cache recovery. Source
files, timestamps, and download markers must remain unchanged. Only use
`-RemoveAfter` when the installation should be removed after testing.

CI checks both account scopes. Its elevated runner uses `-SkipShellIntegration`
for current-user installation because elevated COM cannot load per-user classes;
all-users registration receives full Shell integration checks.

On some Windows builds, an immediate post-install thumbnail request can return
`0x8007138E`; a subsequent ordinary request succeeds. The regression test reports
this failure without retries. Also check the Explorer preview pane directly:
COM activation alone does not exercise Explorer's downloaded-file permission gate.

Before releasing, check the account selector, install/update, uninstall and its
cancellation, previews while resizing or switching files, both badges, and files
without embedded images. Close the setup wizard before automated installer tests.

## Diagnostics

Run from `artifacts/bin` or an installed version directory:

```powershell
.\GrasshopperFilePreview.Diagnostics.exe --status
.\GrasshopperFilePreview.Diagnostics.exe --read "example.gh"
.\GrasshopperFilePreview.Diagnostics.exe --extract "example.ghx" "bitmap.png"
.\GrasshopperFilePreview.Diagnostics.exe --probe "example.gh"
.\GrasshopperFilePreview.Diagnostics.exe --thumbnail "example.ghx" "thumbnail.png"
```

`--probe` and `--thumbnail` need installed handlers. Reading and extraction do not.
Use non-confidential files when reporting issues.

## Fixtures

`fixtures/` contains synthetic GH1 archives written with GH_IO. The original
300 x 200 generated bitmap is `Expected.png`. Archives include chunks before and
after `Thumbnail`; missing-image fixtures omit it. No tutorial definitions,
third-party geometry, Rhino libraries, or user files are included. Generated
output stays under ignored `artifacts/`.
