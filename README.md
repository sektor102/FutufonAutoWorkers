# Futufon AutoWorker for My Winter Car

The current version is **1.0.4 for MSCLoader**. Press F8 at a factory table
to assemble 44 complete packages, fill one shipping box and deliver it to
a free player pallet slot. Supplies are refilled automatically. The worker
stops after that box; another F8 starts the next cycle.

**[Download the 1.0.4 ZIP](dist/FutufonAutoWorker-1.0.4.zip)** ·
**[Installation and controls](INSTALL.md)** ·
**[Инструкция на русском](FutufonAutoWorkerMscModLoader/README.md)**

## Requirements and installation

- My Winter Car.
- [MSCLoader for My Winter Car](https://www.nexusmods.com/mywintercar/mods/3).

Close the game and copy the DLL from the archive's `Mods` directory into
your MSCLoader Mods folder. Restart, load a save and check for `AutoWorker 1.0.4`.
Clock in at Futufon, stand at a clear table and look down at the tabletop.
Leave room for the supply row, a shipping box beside the table and a free
player pallet slot.

## Controls and features

| Control | Action |
| --- | --- |
| F8 | Start one shipping cycle, or stop a running cycle |
| F7 | Toggle factory diagnostics |
| F9 | Save a detailed factory snapshot |
| Work speed slider | 100% original pace; 10–99% slower |

Keybinds and speed are configurable in the MSCLoader mod settings.
The game performs component consumption, assembly, folding, packing, closing
and delivery accounting through its existing PlayMaker actions. Packages are
checked for all three components before shipping, and delivery is confirmed
against the native job counters. The mod stops when the player leaves the
workstation or an operation fails. Packed progress in an unfinished shipping
box is retained; clear unfinished loose parts before restarting.

Version 1.0.4 places supplies with their openings facing up in a spaced row along the table,
with separate assembly positions on the tabletop. A supervisor indicator
shows the native work-check state and reads the idle-time counter. It does
not alter attendance, supervisor decisions, pay or idle-time records.

## Validation

The full 1.0.3 cycle was tested in game: 44 complete packages, automatic
refills, pallet delivery, `PackagesTotal=802→846`, `PackagesEmpty=0` and native
supervisor work recognition. Version 1.0.4 compiles against the installed
game and passes checks for runner cancellation, native assembly/packing
flows and supervisor status interpretation. The updated layout, monitor
display and reduced speeds still need in-game verification.

## Building

Open `FutufonAutoWorkers.slnx` in Visual Studio. It builds the active
`FutufonAutoWorkerMscModLoader` project. Install .NET Framework 3.5 reference
assemblies and MSCLoader into your game first. Game DLLs are referenced
from the installed game's `mywintercar_Data/Managed` directory.

Alternatively, from PowerShell 7:

```powershell
.\FutufonAutoWorkerMscModLoader\build.ps1 -GamePath 'H:\SteamLibrary\steamapps\common\My Winter Car'
.\FutufonAutoWorkerMscModLoader\tests\run-tests.ps1
```

Use your own game path. The compiled mod is
`FutufonAutoWorkerMscModLoader/bin/Release/FutufonAutoWorkerMscModLoader.dll`.
Only this DLL goes into `Mods`. The optional native-flow verification script
uses a local extraction of the installed game's FSM data; those data are
not included in the repository.

## Legacy code

The `FutufonAutoWorkers` directory contains the earlier BepInEx experiments.
It is retained for reference and is not built by the root solution. The
active implementation and download use MSCLoader.

License: [MIT](LICENSE). Code is AI-assisted, with disclosure in the assembly
trademark metadata.
