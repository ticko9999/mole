# Mole Survivors Demo Guide

## Run
- Open the Unity project with `2022.3.62f3c1`.
- Open `Assets/Scenes/SampleScene.unity`.
- Click `Play`.

The demo auto-bootstraps all runtime content:
- 6x4 holes grid
- HUD, upgrade panel, event panel, end panel, meta panel
- 10-minute run flow with boss phase
- local save and meta progression

## Controls
- Left click: attack and pickup nearby drops
- `M` (after run ends): open/close meta workshop

## Built-in Milestones
- ~90s: automation milestone expected
- ~300s: build identity milestone expected
- 10:00: boss spawn

## Test Runner
- EditMode tests:
  - upgrade offer quality
  - threat budget accumulation and budget-safe spawn selection
  - save load/migration
  - achievement unlock checks
- PlayMode test:
  - fast-forward run to boss and run-end

## Round2 Notes
- Handfeel feedback is now enabled:
  - short hit-stop on manual hit/crit/boss impact
  - camera shake on impact and boss attack
- Formal art/audio replacement entry:
  - [`Assets/Docs/ROUND2_SKIN_PIPELINE.md`](ROUND2_SKIN_PIPELINE.md)
  - use `PresentationSkin` in `Resources` to replace visuals/audio without scene wiring
