# Futufon AutoWorker for My Winter Car

Version **1.1.0 for MSCLoader** automates factory packaging with four selectable
modes, a compact HUD, soft pause and Russian/English interface.
The default F8 run assembles 44 complete packages, fills a shipping carton,
delivers it to a player pallet and stops.

**[Download 1.1.0](dist/FutufonAutoWorker-1.1.0.zip)** ·
**[Installation and controls](INSTALL.md)** ·
**[Инструкция на русском](FutufonAutoWorkerMscModLoader/README.md)**

## Settings and controls

Open **MSCLoader → Futufon AutoWorker**. The mode slider defaults to Full cycle.

| Mode | Supplies | Small packages | Shipping carton |
| --- | --- | --- | --- |
| 1. Full cycle | Automatic pickup/refill | Automatically packed | Delivered to pallet |
| 2. Manual delivery | Automatic pickup/refill | Automatically packed | Carry it yourself |
| 3. Loose packages | Automatic pickup/refill | Four stacks of up to 11 | Pack and deliver manually |
| 4. Manual supplies | Bring and open them yourself | Four stacks of up to 11 | Pack and deliver manually |

Mode 4 leaves supply containers where the player placed them. Empty/missing
supplies pause production until the player brings replacements to the same
table. Loose-package modes wait when the output stacks are full. Packages
in stacks remain ordinary pickable game objects.

The volume slider selects **one carton (44)** or **fill one pallet**: the
initially free slots on the nearest available player pallet, up to four
cartons/176 packages. Full cycle uses that same pallet throughout the run.
Manual delivery waits for the current carton to be delivered before starting
another; loose-package modes wait for output space. Manual actions stay manual
at both volumes.

| Control | Action |
| --- | --- |
| F8 | Start/resume; request a soft pause while running |
| F6 | Show/hide the compact HUD |
| F7 | Toggle diagnostic recording |
| F9 | Save a detailed factory snapshot |
| Speed slider | 100% original pace; 10–99% slower |
| Language | Russian or English; updates immediately |

Soft pause finishes the current small package and packs/stacks it before
stopping. F8 resumes the same run with retained progress when mode/volume are
unchanged and the player is back at the same workstation. Mode and volume
are captured at start; language, speed and HUD visibility remain live settings.

The HUD shows progress, component stocks, free player pallet slots, current
operation and native supervisor checks. Optional notifications announce lunch
at 11:00 and shift finish at 16:00, once per game day while working at the factory.
They remain visible with the HUD hidden and do not automatically pause production.
The small cardboard package faces the player, rotated 180 degrees from 1.0.4.

## Installation

Requires My Winter Car and [MSCLoader for My Winter Car](https://www.nexusmods.com/mywintercar/mods/3).
Close the game, copy the DLL from the ZIP's `Mods` directory into your MSCLoader
Mods folder, restart and load a save. Clock in at Futufon, stand at a clear
factory table, look down at the tabletop and press F8. The panel displays 1.1.0.
Leave floor space beside the table for carton modes and table space alongside
the assembly area for loose-package stacks.

The game performs consumption, assembly, folding, packing and job accounting
through its existing PlayMaker actions. The mod checks complete contents before
packing and verifies delivery against native counters. Supervisor monitoring
reads the game's decision and idle-time counter; attendance, pay and supervisor
behavior are not overridden. Moving away during assembly requests a soft pause.
Players can walk away while the worker waits for manual supplies/delivery.
Runtime errors stop execution; clear any unfinished parts before restarting.

Logs: `Mods/AutoWorkerLogs/`, beside the DLL. Stack/run progress is retained during
the current loaded session; after a save reload, loose-package runs start anew.
Partial shipping-carton contents are read from the game.

## Validation

In-game logs verify three full 1.0.4 cycles: 44 packages each, native stock refills,
pallet delivery, `PackagesTotal=802→934`, `PackagesEmpty=0` and supervisor work
recognition. Version 1.1.0 builds against the installed game and passes tests for
four mode boundaries, pause/resume progress, exact 44/176 limits, pallet targets,
RU/EN texts, shift reminders and native FSM assumptions.
**The new stack layout, manual modes and 1.1.0 display still need in-game testing.**

## Building

Open `FutufonAutoWorkers.slnx` in Visual Studio. It builds the active
`FutufonAutoWorkerMscModLoader` project. Install .NET Framework 3.5 reference
assemblies and MSCLoader into your game first. Or use PowerShell 7:

```powershell
.\FutufonAutoWorkerMscModLoader\build.ps1 -GamePath 'H:\SteamLibrary\steamapps\common\My Winter Car'
.\FutufonAutoWorkerMscModLoader\tests\run-tests.ps1
```

If the loader injects its assembly at runtime, obtain the MWC reference DLL from
the loader development files and place it in `lib/MSCLoader.dll`, or pass its
path with `-MSCLoaderPath` when building. The loader must also be installed in game.

Only `FutufonAutoWorkerMscModLoader/bin/Release/FutufonAutoWorkerMscModLoader.dll`
goes into Mods. Native-flow/shift verification scripts accept JSON extracted
locally from an installed game; game data and reference DLLs are not distributed.

The `FutufonAutoWorkers` folder retains earlier BepInEx experiments for reference;
the active root solution and download use MSCLoader.

License: [MIT](LICENSE). AI-assisted code is disclosed in assembly metadata.
