# Futufon AutoWorker 1.2.0

Requires My Winter Car and
[MSCLoader for My Winter Car](https://www.nexusmods.com/mywintercar/mods/3).
Built against MSCLoader 1.4.2 for MWC; other loaders have not been tested.

1. Close the game.
2. Copy `Mods/FutufonAutoWorkerMscModLoader.dll` from the archive into your
   active MSCLoader Mods folder, replacing the previous mod DLL when updating.
3. Restart and load a save. The HUD displays version `1.2.0`.
4. Clock in at Futufon, stand by a clear work table and look down at the tabletop.
5. Open **MSCLoader → Futufon AutoWorker**, choose settings and press **F8**.

The interface defaults to Russian. Select English in the Language setting.

## Modes

1. **Full cycle:** automatic supplies, assembly, carton packing and pallet delivery.
2. **Manual delivery:** automatic supplies, assembly and packing; carry the carton yourself.
3. **Loose packages:** automatic supplies and assembly; four stacks of up to 11.
   Pack and deliver them manually.
4. **Manual supplies:** bring and open supply containers; assembly into stacks is
   automatic. Replenishing supplies, carton packing and delivery remain manual.

In mode 4, place all four supply containers on the same table within about
1.6 m of the assembly area. Open the charger/manual boxes. If supplies are
missing or empty, the worker waits for the replacements and your return to
the workstation. Leave clear assembly and stack space on the table and floor
space for carton modes. Full stacks pause work until packages are removed.

## Controls and settings

| Control | Action |
| --- | --- |
| F8 | Start / soft pause / resume |
| F6 | Show / hide HUD |
| Next automation mode — assign a key | Cycle 1 → 2 → 3 → 4 → 1 |
| Diagnostic recording — unassigned | Toggle detailed logging |
| State snapshot — unassigned | Append one full snapshot |

Assign or change keys in MSCLoader keybindings. Optional diagnostics have new
setting IDs; previous F7/F9 bindings are not imported. HUD hints use the actual
assigned bindings.

Volume is one batch of 44, or the free slots on the initial target pallet,
up to four batches/176 packages. Manual tasks remain manual at either volume.
Speed is adjustable from 10% to 100%.

Pause and mode changes finish the current small package first. Mode changes
retain progress, stacks and carton contents; old stacks are not automatically
packed. Selecting a mode while paused does not resume work: press F8.
Volume is fixed at run start. Language, speed and HUD visibility update live.

Moving more than 2 m away during assembly requests a soft pause. You can walk
away while waiting for manual supplies/delivery. Return before assembly resumes.
A runtime error stops work; unfinished parts may need manual cleanup.

## HUD and persistence

The HUD shows progress, task, supplies, pallet slots and native supervisor status.
It also shows current/last clock-in/out times, signed overtime balance, and a
Tuesday-only meeting reminder for 13:10. Last-shift times are reconstructed from
the game's saved clock-in and duration after loading.

Optional lunch and shift-end notifications appear at 11:00 and 16:00, once per
game day while working at the factory. They remain visible with the HUD hidden.
The shift-end notice is the scheduled time; clock-out shows the actual card event.

Stack/run progress is kept within the current loaded session; start a new run
after reloading. Saved partial shipping-carton contents can be continued.
Visual-only meshes restore missing lower filled layers without changing counts,
collision, mass or delivery accounting. The mod does not override pay,
attendance or supervisor decisions.

## Feedback and removal

Version 1.2.0 is a public test release. All four modes in 1.1.1 were tested
in-game by the user; the new transitions, carton graphics and expanded HUD need
wider in-game testing. Automated scheduler/model checks pass. Compatibility with
other factory-job mods is not fully tested.

Logs: `Mods/AutoWorkerLogs/`, beside the DLL. Assign an optional diagnostic
key if detailed logging is needed, reproduce the issue and turn recording off.
Include mod/game/MSCLoader versions, mode/volume/speed, reproduction steps and
other installed factory mods in a report.

To uninstall, pause with F8, close the game and remove the mod DLL.

Russian instructions: [README](FutufonAutoWorkerMscModLoader/README.md).
License: [MIT](LICENSE). Codex-generated code is disclosed in assembly metadata.
