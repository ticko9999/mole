# Round2 Skin Pipeline (Unity)

## Goal
- Keep the demo **zero-click runnable** (`open project -> Play`) while allowing formal art/audio replacement.

## Where To Put Skin Asset
- Create a `PresentationSkin` asset from:
  - `Create -> MoleSurvivors -> Presentation Skin`
- Save it at:
  - `Assets/Resources/MoleSurvivors/DefaultPresentationSkin.asset`

Runtime load order:
1. `Resources/MoleSurvivors/DefaultPresentationSkin`
2. `Resources/PresentationSkin`
3. fallback to procedural placeholder visuals (always runnable)

## Replaceable Visual Slots
- Base sprites:
  - `BackgroundSprite`
  - `HoleSprite`
  - `MoleDefaultSprite`
  - `DropDefaultSprite`
  - `BossSprite`
- Base colors:
  - `BackgroundColor`
  - `HoleIdleColor`
  - `HoleWarningColor`
  - `HoleActiveColor`
  - `HoleHitColor`
  - `HoleCooldownColor`
- Boss:
  - `OverrideBossTint`
  - `BossTint`

## Mole ID Mapping (Per Type Override)
Fill `MoleVisuals` list with these `MoleId` values:
- `mole_common`
- `mole_swift`
- `mole_tank`
- `mole_bomb`
- `mole_chest`
- `mole_chain`
- `mole_shield`
- `mole_elite`

## Drop Mapping
Fill `DropVisuals` by `DropType`:
- `Gold`
- `Experience`
- `Core`

## Audio Slots
- `HitSfx`
- `CritSfx`
- `KillSfx`
- `BossHitSfx`
- `BossDefeatSfx`

## Handfeel Tuning (No Code Change)
Tune these multipliers in the same `PresentationSkin`:
- `HitStopMultiplier`
- `CameraShakeMultiplier`
- `SfxVolumeMultiplier`

They scale runtime feedback behavior while keeping gameplay logic unchanged.

