# Third-party notices

## Installer

The self-extracting installer is built with [Inno Setup](https://jrsoftware.org/),
copyright Jordan Russell and Martijn Laan. Its compiler is a build tool and is
not included in the app's source or runtime files.

## McNeel Grasshopper icons

This distribution bundles two unmodified icons from the McNeel Grasshopper
distribution:

| Bundled asset | Original asset | Use |
| --- | --- | --- |
| `assets/Grasshopper.ico` | `GH_Binary.ico` | Blue `.gh` thumbnail corner badge |
| `assets/GrasshopperXml.ico` | `GH_Xml.ico` | Purple `.ghx` thumbnail corner badge |

The installer embeds both files and places them beside the installed handler DLL.
Both were copied from the `Icons` directory in the installed Rhino 9 Grasshopper
distribution.

Grasshopper, Rhino, and these icon assets belong to Robert McNeel & Associates.
The project's MIT license applies to its original code, documentation, and
original test data; it does **not** relicense the McNeel icons or trademarks.
No separate icon redistribution permission has been verified for this project.
Attribution does not itself grant such permission.

This is an independent utility and is not affiliated with or endorsed by McNeel.
See [McNeel](https://www.mcneel.com/) and [Grasshopper](https://www.grasshopper3d.com/).

## Test fixtures

The fixtures contain original generated bitmap and archive data. No Rhino or
GH_IO libraries, tutorial definitions, or third-party geometry are distributed.
