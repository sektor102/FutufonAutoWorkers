FUTUFON AUTOWORKER 1.2.0
For My Winter Car with MSCLoader for My Winter Car.
Author: 2Baikal. MIT license.

PUBLIC TEST RELEASE
The four work modes in 1.1.1 were tested in-game by the author. 1.2.0 passes automated scheduling and model checks. The new live mode transitions, lower-layer graphics and expanded HUD still need wider in-game testing.

INSTALL
Close the game. Copy Mods/FutufonAutoWorkerMscModLoader.dll from this archive into your active MSCLoader Mods folder. Replace the previous DLL when updating. Install MSCLoader separately:
https://www.nexusmods.com/mywintercar/mods/3
Built against MSCLoader 1.4.2 for My Winter Car. Other loaders have not been tested.

START
Clock in at the factory, stand by a clear work table and look down at the tabletop. Choose settings under MSCLoader > Futufon AutoWorker, then press F8.
The default interface language is Russian. Select English in the Language setting.

MODES
1. Full cycle: automatic supplies, assembly, carton packing and pallet delivery.
2. Manual delivery: automatic supplies, assembly and carton packing; carry the full carton yourself.
3. Loose packages: automatic supplies and assembly; four output stacks of up to 11. Pack and deliver manually.
4. Manual supplies: bring and open supply containers on the same table; assembly into stacks is automatic. Replenishing, packing and delivery are manual.

Leave clear space for assembly, supply containers and output stacks, with floor space for carton modes and free player-pallet slots for automatic delivery.

CONTROLS AND SETTINGS
F8: start, soft pause, resume.
F6: show/hide HUD.
Next mode: assign a key in MSCLoader keybindings; cycles 1 > 2 > 3 > 4 > 1.
Diagnostics and snapshots: unassigned by default.
All keys can be remapped.
Volume: one batch of 44, or a run based on free pallet slots at start, up to 176 packages.
Speed: 10–100%.

Pause and mode changes finish the current small package first. Selecting a mode while paused does not start the worker. Mixed-mode runs keep old stacks and carton contents where they are; existing stacks are not automatically packed. The total volume remains fixed and can include both stacks and a partial carton.
Automation/stack progress persists within the game session. After reloading, start a new run; shipping-carton contents saved by the game can be continued.

HUD
Progress, task, supplies, free pallet slots, supervisor status, clock-in/out times and the game's signed overtime balance. Last-shift times are reconstructed from the game's saved clock-in and duration after loading. Overtime changes according to native clock-out and daily accounting.
Lunch/end-of-shift notifications and a Tuesday-only 13:10 meeting reminder are included.
Lower filled carton layers are shown using visual-only meshes; native contents and delivery accounting are retained.

FEEDBACK
Include the mod/game/MSCLoader versions, mode/volume/speed, reproduction steps, expected/actual result, other factory-related mods and whether it happened after a reload.
Logs are stored in AutoWorkerLogs beside the DLL, normally Mods/AutoWorkerLogs. Assign an optional diagnostic key if detailed recording is needed; disable it after reproducing the issue. Screenshots or videos are useful.
Compatibility with other factory-job mods is not fully tested. Manual modes need your participation and do not guarantee zero supervisor-recorded idle time.

UNINSTALL
Pause with F8, close the game and remove the mod DLL.

Thanks to piotrulos for MSCLoader. Developed with AI-assisted coding using Codex.
See README-RU.md for detailed Russian instructions and LICENSE for the MIT license.
