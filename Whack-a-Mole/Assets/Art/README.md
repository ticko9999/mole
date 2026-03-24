# Art Directory Guide

This is the canonical art directory for the project.

## Runtime-facing folders

- `Characters/`
- `Environment/`
- `Drops/`
- `VFX/`
- `UI/`
- `Weapons/`
- `Skills/`
- `Meta/`

## Non-runtime / production folders

- `Promo/` for store capsules, key art, screenshots.
- `Temp/` for imported raw packs and review handoff.

## Current migration status

- Existing external packs from `Assets/ArtDrop` are mirrored into:
  - `Assets/Art/Temp/Round4_Nano`
  - `Assets/Art/Temp/FreeUI`
- Structured copies are also mirrored into the corresponding runtime folders.

## Loader compatibility

- Runtime loaders now include `Assets/Art/Temp/...` as fallback search roots.
- Legacy `Assets/ArtDrop/...` remains supported as fallback.
