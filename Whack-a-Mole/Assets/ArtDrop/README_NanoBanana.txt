MoleSurvivors External Art Pack Drop Folder
==========================================

Put Nano Banana Pro exported PNG files here:

Assets/ArtDrop/Round4_Nano/

Required file names:
- bg_factory_main.png
- hole_factory.png
- mole_common.png
- mole_swift.png
- mole_tank.png
- mole_bomb.png
- mole_chest.png
- mole_chain.png
- mole_shield.png
- mole_elite.png
- boss_foreman.png
- boss_rat_king.png
- drop_gold.png
- drop_exp.png
- drop_core.png

Optional event icon file names:
- event_merchant.png
- event_treasure.png
- event_curse.png
- event_repair.png
- event_bounty.png
- event_rogue.png (or event_rogue_zone.png)

Notes:
- Keep alpha transparency for all files except background.
- The game auto-loads this folder at runtime; no Unity manual hookup required.
- Current temporary drop icons are filled from CC0 UI assets and can be replaced anytime.

Round5 Editor Tools:
- Tools/MoleFactory/Import Art Zips (Nano Banana)
  - auto imports default zip paths from Downloads
  - auto normalizes names into this folder
  - auto clamps PNG max size to 2048
  - keeps legacy single-frame fallback names from idle frames
- Tools/MoleFactory/Validate Config v5.2
- Tools/MoleFactory/Hot Reload Config (Play Mode)
