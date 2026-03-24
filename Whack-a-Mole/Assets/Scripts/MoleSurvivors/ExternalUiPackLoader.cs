using System;
using System.IO;
using UnityEngine;

namespace MoleSurvivors
{
    public sealed class ExternalUiSkin
    {
        public bool Loaded;
        public string PackDirectory;
        public int LoadedSpriteCount;

        public Sprite PanelBackgroundSprite;
        public Sprite ButtonNormalSprite;
        public Sprite ButtonHighlightedSprite;
        public Sprite ButtonPressedSprite;
        public Sprite ButtonDisabledSprite;
        public Sprite AcceptIconSprite;
        public Sprite SkipIconSprite;
        public Sprite MetaIconSprite;
        public Sprite RestartIconSprite;

        public Sprite BossHpBackgroundSprite;
        public Sprite BossHpFillSprite;
        public Sprite BossHpFrameSprite;
        public Sprite BossHpShieldFillSprite;
        public Sprite BossHpWarnGlowSprite;

        public Sprite DurabilityFillSprite;
        public Sprite DurabilityFrameSprite;
        public Sprite DurabilityDangerOverlaySprite;

        public Sprite ExpBarFillSprite;
        public Sprite ExpBarFrameSprite;
        public Sprite ExpLevelFlashSprite;

        public Sprite ComboBarFillSprite;
        public Sprite ComboBarFrameSprite;
        public Sprite ComboBarMaxSprite;
    }

    public static class ExternalUiPackLoader
    {
        private static readonly string[] FallbackRelativeFolders =
        {
            "Art/Temp/FreeUI",
            "Art/Temp/Round4_Nano",
            "Art/Temp",
            "ArtDrop/FreeUI",
            "ArtDrop/Round4_Nano",
            "ArtDrop",
        };

        public static ExternalUiSkin TryLoad(string preferredRelativeFolder)
        {
            ExternalUiSkin skin = new ExternalUiSkin();
            string packDirectory = ResolvePackDirectory(preferredRelativeFolder);
            if (string.IsNullOrWhiteSpace(packDirectory))
            {
                return skin;
            }

            skin.PackDirectory = packDirectory;
            string artRoot = Path.Combine(Application.dataPath, "Art");
            string uiRoot = Path.Combine(artRoot, "UI");

            skin.PanelBackgroundSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_panel_bg.png"));
            skin.ButtonNormalSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_button_normal.png"));
            skin.ButtonHighlightedSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_button_highlight.png"));
            skin.ButtonPressedSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_button_pressed.png"));
            skin.ButtonDisabledSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_button_disabled.png"));
            skin.AcceptIconSprite = TryLoadSpriteCandidates(
                0.18f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_icon_accept.png"));
            skin.SkipIconSprite = TryLoadSpriteCandidates(
                0.18f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_icon_skip.png"));
            skin.MetaIconSprite = TryLoadSpriteCandidates(
                0.18f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_icon_meta.png"));
            skin.RestartIconSprite = TryLoadSpriteCandidates(
                0.18f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "ui_icon_restart.png"));

            skin.BossHpBackgroundSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "boss_hp_bg.png"),
                Path.Combine(uiRoot, "Panels", "Boss", "boss_hp_bg.png"));
            skin.BossHpFillSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "boss_hp_fill.png"),
                Path.Combine(uiRoot, "Panels", "Boss", "boss_hp_fill.png"));
            skin.BossHpFrameSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "boss_hp_frame.png"),
                Path.Combine(uiRoot, "Panels", "Boss", "boss_hp_frame.png"));
            skin.BossHpShieldFillSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "boss_hp_shield_fill.png"),
                Path.Combine(uiRoot, "Panels", "Boss", "boss_hp_shield_fill.png"));
            skin.BossHpWarnGlowSprite = TryLoadSpriteCandidates(
                0.34f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "boss_hp_warn_glow.png"),
                Path.Combine(uiRoot, "Panels", "Boss", "boss_hp_warn_glow.png"));

            skin.DurabilityFillSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "farm_durability_fill.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "farm_durability_fill.png"));
            skin.DurabilityFrameSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "farm_durability_frame.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "farm_durability_frame.png"));
            skin.DurabilityDangerOverlaySprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "farm_danger_overlay.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "farm_danger_overlay.png"));

            skin.ExpBarFillSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "exp_bar_fill.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "exp_bar_fill.png"));
            skin.ExpBarFrameSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "exp_bar_frame.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "exp_bar_frame.png"));
            skin.ExpLevelFlashSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "exp_level_up_flash.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "exp_level_up_flash.png"));

            skin.ComboBarFillSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "combo_bar_fill.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "combo_bar_fill.png"));
            skin.ComboBarFrameSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "combo_bar_frame.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "combo_bar_frame.png"));
            skin.ComboBarMaxSprite = TryLoadSpriteCandidates(
                0.32f,
                ref skin.LoadedSpriteCount,
                Path.Combine(packDirectory, "combo_bar_max_state.png"),
                Path.Combine(uiRoot, "Panels", "HUD", "combo_bar_max_state.png"));

            skin.Loaded = skin.LoadedSpriteCount > 0;
            return skin;
        }

        private static string ResolvePackDirectory(string preferredRelativeFolder)
        {
            string preferred = string.IsNullOrWhiteSpace(preferredRelativeFolder)
                ? null
                : preferredRelativeFolder.Trim().Replace("\\", "/");

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                string preferredPath = Path.Combine(Application.dataPath, preferred);
                if (Directory.Exists(preferredPath))
                {
                    return preferredPath;
                }
            }

            for (int i = 0; i < FallbackRelativeFolders.Length; i++)
            {
                string path = Path.Combine(Application.dataPath, FallbackRelativeFolders[i]);
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static Sprite TryLoadSpriteCandidates(float targetWorldSize, ref int loadedCount, params string[] candidatePaths)
        {
            if (candidatePaths == null || candidatePaths.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string candidatePath = candidatePaths[i];
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                Sprite sprite = TryLoadSpriteFile(candidatePath, targetWorldSize, ref loadedCount);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return null;
        }

        private static Sprite TryLoadSpriteFile(string filePath, float targetWorldSize, ref int loadedCount)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = $"MS_UI_{Path.GetFileNameWithoutExtension(filePath)}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                float maxDimension = Mathf.Max(texture.width, texture.height);
                float clampedTarget = Mathf.Clamp(targetWorldSize, 0.08f, 4f);
                float ppu = Mathf.Max(16f, maxDimension / clampedTarget);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    ppu,
                    0,
                    SpriteMeshType.FullRect);
                sprite.name = $"MS_UI_{Path.GetFileNameWithoutExtension(filePath)}";
                loadedCount++;
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MoleSurvivors] Failed to load UI sprite '{filePath}': {ex.Message}");
                return null;
            }
        }
    }
}
