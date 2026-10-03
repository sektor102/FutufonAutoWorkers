# Changes

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
