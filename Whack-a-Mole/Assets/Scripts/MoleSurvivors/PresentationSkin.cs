using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoleSurvivors
{
    [Serializable]
    public sealed class MoleVisualEntry
    {
        public string MoleId;
        public Sprite Sprite;
        public Sprite WarningSprite;
        public Sprite ActiveSprite;
        public Sprite IdleSprite;
        public Sprite HitSprite;
        public Sprite HitSpriteAlt;
        public Sprite RetreatSprite;
        public Sprite RecoverSprite;
        public Color Tint = Color.white;
    }

    [Serializable]
    public sealed class DropVisualEntry
    {
        public DropType DropType;
        public Sprite Sprite;
        public Color Tint = Color.white;
    }

    [CreateAssetMenu(fileName = "PresentationSkin", menuName = "MoleSurvivors/Presentation Skin")]
    public sealed class PresentationSkin : ScriptableObject
    {
        [Header("Base Sprites")]
        public Sprite BackgroundSprite;
        public Sprite HoleSprite;
        public Sprite MoleDefaultSprite;
        public Sprite DropDefaultSprite;
        public Sprite BossSprite;
        public Sprite MidBossSprite;
        public Sprite EventMerchantSprite;
        public Sprite EventTreasureSprite;
        public Sprite EventCurseSprite;
        public Sprite EventRepairSprite;
        public Sprite EventBountySprite;
        public Sprite EventRogueSprite;

        [Header("Boss")]
        public bool OverrideBossTint;
        public Color BossTint = Color.white;
        public bool OverrideMidBossTint;
        public Color MidBossTint = Color.white;

        [Header("Base Colors")]
        public Color BackgroundColor = new Color(0.1f, 0.19f, 0.14f);
        public Color HoleIdleColor = new Color(0.22f, 0.16f, 0.1f);
        public Color HoleWarningColor = new Color(0.5f, 0.38f, 0.2f);
        public Color HoleActiveColor = new Color(0.12f, 0.4f, 0.14f);
        public Color HoleHitColor = new Color(0.8f, 0.2f, 0.2f);
        public Color HoleCooldownColor = new Color(0.15f, 0.12f, 0.1f);

        [Header("Per Type Overrides")]
        public List<MoleVisualEntry> MoleVisuals = new List<MoleVisualEntry>();
        public List<DropVisualEntry> DropVisuals = new List<DropVisualEntry>();

        [Header("Optional SFX")]
        public AudioClip HitSfx;
        public AudioClip CritSfx;
        public AudioClip KillSfx;
        public AudioClip BossHitSfx;
        public AudioClip BossDefeatSfx;
        public AudioClip MidBossSpawnSfx;
        public AudioClip EventAlertSfx;

        [Header("Handfeel Multipliers")]
        [Range(0f, 2f)]
        public float HitStopMultiplier = 1f;

        [Range(0f, 2f)]
        public float CameraShakeMultiplier = 1f;

        [Range(0f, 2f)]
        public float SfxVolumeMultiplier = 1f;
    }
}
