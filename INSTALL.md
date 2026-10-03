# Futufon AutoWorker 1.0.4

Requires My Winter Car with MSCLoader for My Winter Car installed:
https://www.nexusmods.com/mywintercar/mods/3

1. Close the game.
2. Copy `Mods/FutufonAutoWorkerMscModLoader.dll` into your MSCLoader Mods folder.
3. Start the game and load your save. The HUD should display `AutoWorker 1.0.4`.
4. Clock in at Futufon, stand at a clear factory table and look down at its surface.
5. Press F8 for one shipping box: 44 complete packages, automatic refills and pallet delivery.
6. Press F8 again to stop. Packed progress is retained; clear unfinished parts before resuming.

F7 toggles diagnostics; F9 records a detailed factory snapshot. Keys are configurable.
The MSCLoader mod settings include `Work speed (%)`: 100% is the original pace;
lower values slow the steps down, to a minimum of 10%.

Leave approximately 2 x 0.75 m of clear table space, floor space beside the table,
and an available player pallet slot. Moving more than 2 m from the starting
position stops the worker. The mod stops automatically after one shipping box.

The supervisor indicator reads the game's native decision and idle counter.
It does not override attendance, supervisor behavior, pay or idle time.
Green means the game recognizes work; red means idle time was detected.
Waiting for inspection is displayed separately from accepted work.

Logs: `Mods/AutoWorkerLogs/`. Error reports are more useful with the newest
session log, mod version, game version and other installed mods listed.

Validation: the full 44-package cycle in 1.0.3 was tested in game, including
refills, pallet delivery and native supervisor work recognition. Version 1.0.4
adds the corrected layout and on-screen supervisor monitoring. It builds
against the installed game and passes the local checks; the new layout,
monitor display and reduced speed still need in-game verification.

Russian instructions are included in `README-RU.md`. Source code is at:
https://github.com/sektor102/FutufonAutoWorkers

License: MIT. Code is AI-assisted; see the assembly trademark disclosure.
