# Asset Sources

Current demo build uses procedural placeholder visuals generated at runtime:
- flat-color sprite primitives for holes, moles, drops, and boss
- built-in runtime font (`LegacyRuntime.ttf`, with fallback handling)

This follows the fallback rule in the plan:
- if external CC0 assets are not yet imported, keep gameplay fully runnable with placeholder assets.

Round2 art/audio replacement interface:
- create `PresentationSkin` and place it in `Assets/Resources/MoleSurvivors/DefaultPresentationSkin.asset`
- per-mole and per-drop override mappings are supported
- optional hit/crit/kill/boss SFX slots are supported

When replacing with final art/audio, add entries here:
- source URL
- license
- attribution requirement
- imported path
