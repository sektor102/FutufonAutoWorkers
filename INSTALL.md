# Futufon AutoWorker 1.1.0

Requires My Winter Car with MSCLoader for My Winter Car:
https://www.nexusmods.com/mywintercar/mods/3

1. Close the game.
2. Copy `Mods/FutufonAutoWorkerMscModLoader.dll` into your MSCLoader Mods folder.
3. Restart and load a save. At the factory the HUD displays `AutoWorker 1.1.0`.
4. Clock in at Futufon, stand at a clear table and look down at the tabletop.
5. Choose settings in MSCLoader → Futufon AutoWorker and press F8.

## Four modes

The mode slider defaults to 1, Full cycle:

1. Full cycle: automatic supply pickup/refill, assembly, packing 44 packages
   into a carton, then pallet delivery.
2. Manual delivery: automatic supplies, assembly and packing; carry the full
   carton to a pallet yourself.
3. Loose packages: automatic supplies and assembly; four stacks of up to 11
   closed complete packages on the table. Pack and deliver them yourself.
4. Manual supplies: bring and open supply containers yourself. The mod only
   takes their parts and assembles packages into stacks, without moving or
   refilling supply containers or inserting packages into shipping cartons.

For mode 4, place all four supply containers on the same work table, within
about 1.6 m of the assembly area, leaving the front of the table clear for
assembly. Open charger/manual boxes. If stock is missing or depleted, the
worker waits and the HUD tells you what to bring/open. Return to the table
before assembly resumes. Empty containers are replaced manually.

Leave floor space beside the table for carton modes and table space alongside
the assembly area for stacks. Full stacks pause production until boxes are
removed. These are ordinary game objects that can be carried and packed.

## Volume and soft pause

One carton is 44 small packages. Fill one pallet targets the initially free
slots on the nearest available pallet: up to four cartons/176 packages.
Manual delivery waits for a carton to be delivered before preparing another;
loose-package modes wait for stack space. Manual tasks remain manual.

F8 while working finishes the current small package, packs/stacks it and pauses.
F8 after pausing resumes retained progress with the same mode/volume at the same
workstation. Moving more than 2 m away during assembly also requests soft pause;
you can walk away while waiting for manual restocking or delivery. Game pause
freezes automation. A runtime error stops the worker; unfinished parts after
an error may need manual cleanup.

Mode/volume apply to a new run. Language, speed and HUD visibility update live.
Stack/run progress lasts for the current loaded session; after loading a save,
start a new loose-package run. Partial carton contents are read from the game.

## Panel and notifications

F6 shows/hides the HUD, also controlled by the Compact HUD checkbox. It shows
progress, supply stocks, free pallet slots and the native supervisor check.
Select Russian or English in Language. Settings/key labels are bilingual.

Optional shift notifications announce lunch at 11:00 and shift finish at 16:00,
once per game day while working at the factory. They work with the HUD hidden
and do not pause automation or change game time/attendance.

Work speed: 100% is the original pace; 10–99% slows the steps down.
F7 toggles diagnostic recording. F9 saves a detailed factory snapshot.
All keys are configurable in MSCLoader.

Logs: `Mods/AutoWorkerLogs/`. Include the newest session log, mod/game version
and other installed mods when reporting a problem.

Three complete 1.0.4 cycles were tested in game, including refills, delivery
and supervisor recognition. Version 1.1.0 builds and passes local tests;
new stacks, manual modes and the updated HUD still need in-game verification.

Russian instructions: `README-RU.md` in the download.
Source: https://github.com/sektor102/FutufonAutoWorkers
License: MIT. AI-assisted code is disclosed in assembly metadata.
