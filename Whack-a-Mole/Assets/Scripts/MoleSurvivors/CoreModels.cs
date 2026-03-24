using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoleSurvivors
{
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    [Flags]
    public enum MoleTrait
    {
        None = 0,
        Fast = 1 << 0,
        Tank = 1 << 1,
        Bomb = 1 << 2,
        Chest = 1 << 3,
        Chain = 1 << 4,
        Shield = 1 << 5,
        Elite = 1 << 6,
    }

    public enum HoleState
    {
        Idle,
        Warning,
        HitWindow,
        HitFlash,
        Retreat,
        Cooldown,
        OccupiedByEvent,
    }

    public enum UpgradeEffectType
    {
        AddDamage,
        AttackIntervalMultiplier,
        AddRange,
        AddCritChance,
        AddCritDamage,
        AddChainCount,
        AddSplash,
        AddGoldMultiplier,
        AddExpMultiplier,
        UnlockAutoHammer,
        AutoHammerIntervalMultiplier,
        UnlockAutoAim,
        AddDroneCount,
        AddMagnetRadius,
        AddMaxDurability,
        AddBossDamageMultiplier,
        DeployAutoHammerTower,
        DeploySensorHammer,
        DeployGoldMagnet,
        DeployBountyMarker,
        FacilityCooldownMultiplier,
        FacilityPowerMultiplier,
        FacilityOverloadThresholdMultiplier,
        FacilityGoldMultiplier,
    }

    public enum MetaEffectType
    {
        AddStartDamage,
        AttackIntervalMultiplier,
        AddStartRange,
        AddMaxDurability,
        AddGoldMultiplier,
        AddExpMultiplier,
        AddStartingGold,
        UnlockLightningWeapon,
        UnlockDroneWeapon,
        UnlockEngineerCharacter,
    }

    public enum AchievementTrigger
    {
        KillCountInRun,
        ComboInRun,
        GoldInRun,
        BossWin,
        AutoKillsInRun,
        CodexDiscoveries,
        LifetimeRuns,
        LifetimeWins,
        MetaNodesPurchased,
        CoreShardsInRun,
    }

    public enum RunEventType
    {
        MerchantBoost,
        TreasureRush,
        CurseAltar,
        RepairStation,
        BountyContract,
        RogueHoleZone,
    }

    public enum DropType
    {
        Gold,
        Experience,
        Core,
    }

    public enum AttackSource
    {
        Manual,
        AutoHammer,
        Drone,
        Chain,
        Facility,
    }

    public enum FacilityType
    {
        AutoHammerTower,
        SensorHammer,
        GoldMagnet,
        BountyMarker,
    }

    public enum FacilityState
    {
        Idle,
        Trigger,
        Cooldown,
        Overload,
    }

    [Serializable]
    public sealed class TagRequirement
    {
        public string Tag;
        public int Level;
    }

    public sealed class MoleDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public Rarity Rarity;
        public MoleTrait Traits;
        public float BaseHp;
        public float WarningSeconds;
        public float UpSeconds;
        public float CooldownSeconds;
        public int GoldReward;
        public int ExpReward;
        public int CoreReward;
        public float ThreatCost;
        public float MinTime;
        public float MaxTime;
        public float SpawnWeight;
        public Color TintColor;
    }

    public sealed class WeaponDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public float Damage;
        public float AttackInterval;
        public float AttackRadius;
        public float CritChance;
        public float CritDamage;
        public int ChainCount;
        public float SplashRadius;
        public bool AutoAim;
        public float AutoHammerInterval;
        public int DroneCount;
        public string EvolutionId;
        public List<TagRequirement> EvolutionRequirements = new List<TagRequirement>();
    }

    public sealed class UpgradeDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public Rarity Rarity;
        public string Category;
        public List<string> Tags = new List<string>();
        public UpgradeEffectType EffectType;
        public float Value;
        public int MaxStacks;
        public int UnlockAtSecond;
        public float BaseWeight;
        public bool IsAutomation;
        public bool IsLegendary;
    }

    public sealed class BossDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public float Hp;
        public float AttackInterval;
        public int DurabilityDamage;
        public int RewardGold;
        public int RewardCore;
        public Color TintColor;
    }

    public sealed class BossEncounterDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string BossId;
        public float SpawnAtSecond;
        public bool IsFinalBoss;
        public float HpMultiplier = 1f;
        public float SpawnScale = 0.58f;
        public float ShieldCycleSeconds = 11f;
        public float ShieldDuration = 3.2f;
        public float RogueHoleInterval = 9f;
        public float RogueHoleDuration = 4.2f;
        public int RogueHoleCount = 4;
    }

    public sealed class FacilityDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public FacilityType Type;
        public float BaseInterval;
        public float BasePower;
        public float BaseRadius;
        public float TriggerDelay;
        public int UnlockAtSecond;
        public float OverloadThreshold;
        public float OverloadDuration;
        public List<string> Tags = new List<string>();
    }

    public sealed class MetaNodeDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public MetaEffectType EffectType;
        public string TargetId;
        public float Value;
        public int Cost;
        public int MaxLevel;
        public List<string> Requires = new List<string>();
    }

    public sealed class CodexDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Category;
        public string Description;
    }

    public sealed class AchievementDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public AchievementTrigger Trigger;
        public int TargetInt;
        public float TargetFloat;
        public int RewardChips;
    }

    public sealed class RunEventDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public RunEventType Type;
        public float MinTime;
        public float MaxTime;
        public int GoldCost;
        public float Value;
    }

    public sealed class CharacterDef : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public float DamageMultiplier;
        public float RangeBonus;
        public float AutomationMultiplier;
    }

    public sealed class GameContent : ScriptableObject
    {
        public List<MoleDef> Moles = new List<MoleDef>();
        public List<WeaponDef> Weapons = new List<WeaponDef>();
        public List<UpgradeDef> Upgrades = new List<UpgradeDef>();
        public List<BossDef> Bosses = new List<BossDef>();
        public List<BossEncounterDef> BossEncounters = new List<BossEncounterDef>();
        public List<FacilityDef> Facilities = new List<FacilityDef>();
        public List<MetaNodeDef> MetaNodes = new List<MetaNodeDef>();
        public List<CodexDef> CodexEntries = new List<CodexDef>();
        public List<AchievementDef> Achievements = new List<AchievementDef>();
        public List<RunEventDef> Events = new List<RunEventDef>();
        public List<CharacterDef> Characters = new List<CharacterDef>();
        public float RunDurationSeconds = 600f;
        public float BossGraceSeconds = 60f;
        public float InitialEventCooldownSeconds = 55f;
        public float EventRetryCooldownSeconds = 30f;
        public float InitialEventUnlockSeconds = 55f;
        public float ComboWindowSeconds = 2.5f;
        public float ComboMissWindowSeconds = 0.8f;
        public float ComboDecayTickSeconds = 0.4f;
        public float BossWarningSeconds = 3f;
        public float LegendWarningSeconds = 1.2f;
        public float AutoPickupRange = 1.5f;
        public float HitStopSeconds = 0.03f;
        public float CritHitStopSeconds = 0.05f;
        public float BossHitStopSeconds = 0.045f;
        public float CameraShakeSeconds = 0.12f;
        public float CameraShakeAmplitude = 0.07f;
        public float CameraShakeFrequency = 40f;
        public float RareHintCooldownSeconds = 0.9f;
        public float MidBossWarningLeadSeconds = 15f;
        public float FinalBossWarningLeadSeconds = 30f;
        public int StartingGold;
        public int StartingExperience;
        public int StartingDurability = 12;
        public int StartingEventTickets = 1;
        public string DefaultWeaponId;
        public string DefaultCharacterId;
        public string SecondaryWeaponUnlockId;
        public string TertiaryWeaponUnlockId;
        public string SecondaryCharacterUnlockId;
        public List<string> StartupUnlockedWeaponIds = new List<string>();
        public List<string> StartupUnlockedCharacterIds = new List<string>();
    }

    [Serializable]
    public sealed class PlayerCombatStats
    {
        public float Damage = 10f;
        public float AttackInterval = 0.45f;
        public float AttackRadius = 0.35f;
        public float CritChance = 0.05f;
        public float CritDamage = 1.6f;
        public int ChainCount;
        public float SplashRadius;
        public bool AutoAim;
        public float AutoHammerInterval;
        public int DroneCount;
        public float GoldMultiplier = 1f;
        public float ExpMultiplier = 1f;
        public float MagnetRadius;
        public float BossDamageMultiplier = 1f;

        public PlayerCombatStats Clone()
        {
            return new PlayerCombatStats
            {
                Damage = Damage,
                AttackInterval = AttackInterval,
                AttackRadius = AttackRadius,
                CritChance = CritChance,
                CritDamage = CritDamage,
                ChainCount = ChainCount,
                SplashRadius = SplashRadius,
                AutoAim = AutoAim,
                AutoHammerInterval = AutoHammerInterval,
                DroneCount = DroneCount,
                GoldMultiplier = GoldMultiplier,
                ExpMultiplier = ExpMultiplier,
                MagnetRadius = MagnetRadius,
                BossDamageMultiplier = BossDamageMultiplier,
            };
        }
    }

    [Serializable]
    public sealed class RunState
    {
        public float ElapsedSeconds;
        public int Level = 1;
        public float Experience;
        public float NextExperience = 30f;
        public int Gold;
        public int CoreShards;
        public int Durability = 12;
        public int MaxDurability = 12;
        public int Combo;
        public int HighestCombo;
        public float ComboTimer;
        public int TotalKills;
        public int ManualKills;
        public int AutoKills;
        public int BossDamageDone;
        public bool BossSpawned;
        public bool BossDefeated;
        public bool MidBossSpawned;
        public bool MidBossDefeated;
        public bool RunEnded;
        public bool RunWon;
        public bool AutomationMilestoneReached;
        public bool BuildMilestoneReached;
        public int PendingLevelUps;
        public int LowQualityOfferStreak;
        public int TotalDamageEvents;
        public float EventCooldown;
        public float TreasureRushRemaining;
        public float CurseRemaining;
        public float BountyContractRemaining;
        public float RogueZoneRemaining;
        public bool FacilityMilestoneReached;
        public bool FacilityOverdriveReached;
        public int FacilityTriggerCount;
        public int FacilityOverloadCount;
        public int FacilityOverloadThresholdCurrent = 18;
        public float FacilityOverloadTimer;
        public int ActiveFacilityCount;
        public float FacilityCooldownMultiplier = 1f;
        public float FacilityPowerMultiplier = 1f;
        public float FacilityGoldMultiplier = 1f;
        public int ManualGoldCollected;
        public int AutomationGoldCollected;
        public int PeakSingleIncome;
        public int RareKillCount;
        public int EventParticipationCount;
        public int EventTickets;
        public int RogueZoneBurstCount;
        public int BountyContractCount;
        public string BuildIdentity = "未成型";
        public string LastUpgradeDisplayName = string.Empty;
        public string LastUpgradeDeltaSummary = string.Empty;
        public Dictionary<FacilityType, int> FacilityLevels = new Dictionary<FacilityType, int>();
        public string WeaponId;
        public string CharacterId;
        public PlayerCombatStats Stats = new PlayerCombatStats();
        public Dictionary<string, int> UpgradeStacks = new Dictionary<string, int>();
        public Dictionary<string, int> UpgradePickCounts = new Dictionary<string, int>();
        public List<string> RecentUpgradePicks = new List<string>();
        public Dictionary<string, int> TagLevels = new Dictionary<string, int>();
        public HashSet<string> BuildTags = new HashSet<string>();
        public HashSet<string> Evolutions = new HashSet<string>();
        public HashSet<string> CodexUnlockedThisRun = new HashSet<string>();
        public HashSet<string> AchievementsUnlockedThisRun = new HashSet<string>();
    }

    [Serializable]
    public sealed class SpawnerState
    {
        public float ThreatBudget;
        public float SpawnCooldown;
        public int SpawnedCount;
        public int EscapedCount;
    }

    [Serializable]
    public sealed class AutomationState
    {
        public float AutoHammerTimer;
        public float DroneTimer;
    }

    [Serializable]
    public sealed class FacilityRuntime
    {
        public FacilityDef Def;
        public FacilityType Type;
        public FacilityState State;
        public int Level = 1;
        public int TriggerCount;
        public float CooldownTimer;
        public float TriggerTimer;
        public bool LastHoleHadTarget;
    }

    [Serializable]
    public sealed class BossEncounterRuntime
    {
        public BossEncounterDef Def;
        public BossDef Boss;
        public bool Spawned;
        public bool Defeated;
        public bool ShieldActive;
        public float ShieldCycleTimer;
        public float ShieldDurationTimer;
        public float RogueHoleTimer;
    }

    [Serializable]
    public sealed class StringIntEntry
    {
        public string Key;
        public int Value;
    }

    [Serializable]
    public sealed class MetaProgressState
    {
        public int SaveVersion = 1;
        public int WorkshopChips;
        public int LegendaryGears;
        public int TotalRuns;
        public int TotalWins;
        public int LifetimeGold;
        public int LifetimeKills;
        public string ActiveWeaponId;
        public string ActiveCharacterId;
        public List<string> UnlockedWeapons = new List<string>();
        public List<string> UnlockedCharacters = new List<string>();
        public List<string> CodexEntries = new List<string>();
        public List<string> AchievementIds = new List<string>();
        public List<StringIntEntry> MetaNodeLevels = new List<StringIntEntry>();
    }

    [Serializable]
    public sealed class SaveEnvelope
    {
        public int Version = 1;
        public MetaProgressState Meta = new MetaProgressState();
    }

    [Serializable]
    public sealed class RunSummary
    {
        public bool Won;
        public int Gold;
        public int CoreShards;
        public int Kills;
        public int RareKills;
        public int EventParticipations;
        public int HighestCombo;
        public bool MidBossDefeated;
        public bool FinalBossDefeated;
        public float AutomationContribution;
        public int PeakIncome;
        public int DurationSeconds;
        public int WorkshopGain;
    }

    public interface ISaveRepository
    {
        string SavePath { get; }
        MetaProgressState LoadOrCreate();
        void Save(MetaProgressState state);
    }

    public interface IUpgradeOfferService
    {
        List<UpgradeDef> BuildOffer(GameContent content, RunState runState, System.Random random);
    }

    public interface ISpawnDirector
    {
        bool TrySpawn(
            GameContent content,
            RunState runState,
            SpawnerState spawnerState,
            List<HoleRuntime> holes,
            float deltaTime,
            System.Random random,
            out HoleRuntime targetHole,
            out MoleDef selectedMole);
    }

    public interface IAutomationService
    {
        void Tick(
            GameContent content,
            RunState runState,
            AutomationState automationState,
            List<HoleRuntime> holes,
            float deltaTime,
            Action<HoleRuntime, float, AttackSource> damageCallback,
            Func<bool> hasBoss,
            Action<float, AttackSource> bossDamageCallback);
    }

    public interface IFacilityService
    {
        bool TryDeployFacility(
            GameContent content,
            RunState runState,
            List<HoleRuntime> holes,
            FacilityType type,
            out HoleRuntime deployedHole);

        void Tick(
            GameContent content,
            RunState runState,
            List<HoleRuntime> holes,
            float deltaTime,
            Func<bool> hasBoss,
            Action<HoleRuntime, float, AttackSource> holeDamageCallback,
            Action<float, AttackSource> bossDamageCallback);

        bool BoostFacilitiesForRepair(List<HoleRuntime> holes, float cooldownReductionScale);
    }

    public interface IBossEncounterService
    {
        List<BossEncounterRuntime> CreateTimeline(GameContent content);

        BossEncounterRuntime FindEncounterToSpawn(List<BossEncounterRuntime> timeline, float elapsedSeconds);

        float ResolveSpawnScale(BossEncounterRuntime activeEncounter);

        void TickActiveEncounter(
            RunState runState,
            BossEncounterRuntime activeEncounter,
            float deltaTime,
            List<HoleRuntime> holes,
            Action<HoleRuntime, float> rogueHoleCallback,
            Action<bool> shieldStateCallback);
    }

    public static class MetaStateUtils
    {
        public static int GetNodeLevel(MetaProgressState state, string nodeId)
        {
            for (int i = 0; i < state.MetaNodeLevels.Count; i++)
            {
                if (state.MetaNodeLevels[i].Key == nodeId)
                {
                    return state.MetaNodeLevels[i].Value;
                }
            }

            return 0;
        }

        public static void SetNodeLevel(MetaProgressState state, string nodeId, int level)
        {
            for (int i = 0; i < state.MetaNodeLevels.Count; i++)
            {
                if (state.MetaNodeLevels[i].Key == nodeId)
                {
                    state.MetaNodeLevels[i].Value = level;
                    return;
                }
            }

            state.MetaNodeLevels.Add(new StringIntEntry { Key = nodeId, Value = level });
        }

        public static bool HasAchievement(MetaProgressState state, string achievementId)
        {
            return state.AchievementIds.Contains(achievementId);
        }

        public static int PurchasedNodeCount(MetaProgressState state)
        {
            int count = 0;
            for (int i = 0; i < state.MetaNodeLevels.Count; i++)
            {
                count += Mathf.Max(0, state.MetaNodeLevels[i].Value);
            }

            return count;
        }
    }

    public readonly struct DamageResult
    {
        public readonly bool Killed;
        public readonly bool ShieldBroken;
        public readonly float RemainingHp;

        public DamageResult(bool killed, bool shieldBroken, float remainingHp)
        {
            Killed = killed;
            ShieldBroken = shieldBroken;
            RemainingHp = remainingHp;
        }
    }
}
