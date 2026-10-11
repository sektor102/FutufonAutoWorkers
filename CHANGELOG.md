# Changes

## 1.2.4 — 11 October 2026

- Fix false "No floor space behind the player for the shipping box" failures
  on clear factory floor, affecting carton placement and manual output towers.
- Test actual triangles of readable non-convex mesh colliders instead of treating
  the combined floor/wall bounding box as solid. Walls and real obstacles still block.
- Reserve the area below native tabletop colliders for their visible supports;
  static-batched renderer bounds no longer reject unrelated floor space.
- Opt-in diagnostic logs now explain rejected floor candidates.
- Build and automated regressions pass. Offline checks against the installed
  factory collision meshes reproduce and resolve 27 false collisions while retaining
  wall rejection. This is not a live-game physics test.

## 1.2.3 — 11 October 2026

- File logging is off by default. Enable Write logs (diagnostics) in the mod's
  MSCLoader settings when needed; turning it off closes the file immediately.
- No AutoWorkerLogs directory or session file is created while opted out,
  including for snapshots or errors. Enabling it records full diagnostics.
- Settings names, language choices, mode/volume values, help and keybinding
  names follow the selected Russian/English language without bilingual labels.
- Language changes retain saved setting IDs, values and assigned keys.
- Build and checks pass for file creation/disposal, opt-in/out, session reopening,
  the actual language callback and retained UI values. Live menu validation remains needed.

## 1.2.2 — 11 October 2026

- Modes 3/4 place finished packages in eleven towers of up to four on the floor
  to the player's left, retaining room for all 44 packages.
- Move the shipping carton farther back on the right. Check its full footprint,
  including open flaps, obstacles and visible table supports before placement.
- Turn the assembly package 90 degrees from its previous orientation so its
  lid opens left, clear of the supply containers.
- Reject table surfaces, loose items, uneven floor and obstructed footprints;
  show a floor-space hint when no suitable placement is available.
- Build, scheduling regressions and controlled geometry checks pass.
  The new placement and physical stacking still need in-game validation.

## 1.2.1 — 8 October 2026

- Mode hotkeys work before F8 and while paused; the HUD shows the selected mode.
- Add an unassigned one-carton/pallet hotkey and a selected-volume HUD line.
- Include manually inserted complete packages before assembly and packing,
  including after soft pause; a 17/44 carton needs only 27 new packages.
- Preserve current-run stack credits when the player packs them manually.
- Label the accumulated game idle counter with minutes.
- Regression checks cover actual hotkeys, manual additions, 0/1/17/43/44,
  own-stack transfers, all 16 mode transitions and 44/176 limits.

Build and automated checks pass. The fixes still need live in-game validation.

## 1.2.0 — 8 October 2026

- Diagnostic shortcuts are unassigned; previous F7/F9 defaults are not imported.
- Assignable next-mode hotkey and live menu switching at complete-package boundaries.
- Retain batch progress, existing stacks and carton contents across mode changes.
- Restore visual-only lower filled carton layers, including after save loading.
- HUD clock-in/out times, saved last-shift reconstruction and native signed overtime.
- Tuesday-only reminder for the 13:10 meeting; hints show assigned keybindings.
- Checks cover all 16 mode transitions, mixed outputs, pause/resume, partial cartons,
  44/176 limits, shift data, lower-row counts and default logging.

Public test release. All four modes in 1.1.1 were tested in-game by the user.
New 1.2.0 transitions, visuals and HUD still need wider in-game testing.

## 1.1.1

- Detailed diagnostics are OFF on load; F8 no longer enables recording.
- Continuous factory clock ticks are excluded; F9 still captures the full clock.
- Per-package progress, supervisor changes and debug snapshots go to file only.
- F7 enables optional file-only diagnostics; the HUD shows ON/OFF.
- Regression checks cover normal-cycle console volume, 2000 clock ticks and F9.


## 1.1.0

- Rotate the small cardboard package 180 degrees toward the player.
- Four MSCLoader slider modes: full cycle, manual carton delivery, loose-package
  stacks and manual supply containers. Full cycle remains the default.
- One 44-package carton or the initially free slots on one pallet, up to 176.
- Soft pause completes the current package and retains progress for F8 resume.
- Four output stacks of up to 11 ordinary pickable packages; wait when full.
- Wait for missing/opened manual supplies or manual carton delivery as needed.
- Compact HUD with progress, stock, pallet space and native supervisor status;
  toggle through F6 or settings.
- Live Russian/English interface and optional lunch/shift-end notifications
  based on the installed game's factory clock and states.
- Tests for mode boundaries, batch capacity, pause progress, reminders and texts.

Build/local checks pass. New modes, stacks and display need in-game testing.

## 1.0.4

- Arrange supplies along the table, openings up, with separate assembly space.
- Read-only native supervisor/idle-counter monitor.
- Full 44-package cycles verified in game with stock refill and pallet delivery.
