using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoleSurvivors
{
    public sealed class JsonSaveRepository : ISaveRepository
    {
        private const int CurrentVersion = 1;
        private readonly List<string> _defaultUnlockedWeapons;
        private readonly List<string> _defaultUnlockedCharacters;
        private readonly string _defaultWeaponId;
        private readonly string _defaultCharacterId;

        public JsonSaveRepository(
            string savePath = null,
            IEnumerable<string> defaultUnlockedWeapons = null,
            IEnumerable<string> defaultUnlockedCharacters = null,
            string defaultWeaponId = null,
            string defaultCharacterId = null)
        {
            SavePath = string.IsNullOrWhiteSpace(savePath)
                ? Path.Combine(Application.persistentDataPath, "mole_survivors_save_v1.json")
                : savePath;
            _defaultUnlockedWeapons = NormalizeDefaults(defaultUnlockedWeapons);
            _defaultUnlockedCharacters = NormalizeDefaults(defaultUnlockedCharacters);
            _defaultWeaponId = defaultWeaponId?.Trim() ?? string.Empty;
            _defaultCharacterId = defaultCharacterId?.Trim() ?? string.Empty;
        }

        public string SavePath { get; }

        public MetaProgressState LoadOrCreate()
        {
            if (!File.Exists(SavePath))
            {
                MetaProgressState created = CreateDefaultState();
                Save(created);
                return created;
            }

            try
            {
                string json = File.ReadAllText(SavePath);
                SaveEnvelope envelope = JsonUtility.FromJson<SaveEnvelope>(json);
                if (envelope == null || envelope.Meta == null)
                {
                    MetaProgressState fallback = CreateDefaultState();
                    Save(fallback);
                    return fallback;
                }

                MetaProgressState migrated = Migrate(envelope);
                EnsureDefaults(migrated);
                return migrated;
            }
            catch
            {
                MetaProgressState fallback = CreateDefaultState();
                Save(fallback);
                return fallback;
            }
        }

        public void Save(MetaProgressState state)
        {
            EnsureDefaults(state);
            SaveEnvelope envelope = new SaveEnvelope
            {
                Version = CurrentVersion,
                Meta = state,
            };

            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(envelope, true);
            File.WriteAllText(SavePath, json);
        }

        private static MetaProgressState Migrate(SaveEnvelope envelope)
        {
            MetaProgressState state = envelope.Meta ?? new MetaProgressState();
            state.SaveVersion = CurrentVersion;
            return state;
        }

        private MetaProgressState CreateDefaultState()
        {
            MetaProgressState state = new MetaProgressState
            {
                SaveVersion = CurrentVersion,
            };
            EnsureDefaults(state);
            return state;
        }

        private void EnsureDefaults(MetaProgressState state)
        {
            if (state.UnlockedWeapons == null)
            {
                state.UnlockedWeapons = new List<string>();
            }

            if (state.UnlockedCharacters == null)
            {
                state.UnlockedCharacters = new List<string>();
            }

            if (state.CodexEntries == null)
            {
                state.CodexEntries = new List<string>();
            }

            if (state.AchievementIds == null)
            {
                state.AchievementIds = new List<string>();
            }

            if (state.MetaNodeLevels == null)
            {
                state.MetaNodeLevels = new List<StringIntEntry>();
            }

            for (int i = 0; i < _defaultUnlockedWeapons.Count; i++)
            {
                if (!state.UnlockedWeapons.Contains(_defaultUnlockedWeapons[i]))
                {
                    state.UnlockedWeapons.Add(_defaultUnlockedWeapons[i]);
                }
            }

            for (int i = 0; i < _defaultUnlockedCharacters.Count; i++)
            {
                if (!state.UnlockedCharacters.Contains(_defaultUnlockedCharacters[i]))
                {
                    state.UnlockedCharacters.Add(_defaultUnlockedCharacters[i]);
                }
            }

            if (state.UnlockedWeapons.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(_defaultWeaponId))
                {
                    state.UnlockedWeapons.Add(_defaultWeaponId);
                }
                else if (!string.IsNullOrWhiteSpace(state.ActiveWeaponId))
                {
                    state.UnlockedWeapons.Add(state.ActiveWeaponId);
                }
            }

            if (state.UnlockedCharacters.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(_defaultCharacterId))
                {
                    state.UnlockedCharacters.Add(_defaultCharacterId);
                }
                else if (!string.IsNullOrWhiteSpace(state.ActiveCharacterId))
                {
                    state.UnlockedCharacters.Add(state.ActiveCharacterId);
                }
            }

            if (string.IsNullOrWhiteSpace(state.ActiveWeaponId) || !state.UnlockedWeapons.Contains(state.ActiveWeaponId))
            {
                state.ActiveWeaponId =
                    (!string.IsNullOrWhiteSpace(_defaultWeaponId) && state.UnlockedWeapons.Contains(_defaultWeaponId))
                        ? _defaultWeaponId
                        : (state.UnlockedWeapons.Count > 0 ? state.UnlockedWeapons[0] : string.Empty);
            }

            if (string.IsNullOrWhiteSpace(state.ActiveCharacterId) || !state.UnlockedCharacters.Contains(state.ActiveCharacterId))
            {
                state.ActiveCharacterId =
                    (!string.IsNullOrWhiteSpace(_defaultCharacterId) && state.UnlockedCharacters.Contains(_defaultCharacterId))
                        ? _defaultCharacterId
                        : (state.UnlockedCharacters.Count > 0 ? state.UnlockedCharacters[0] : string.Empty);
            }
        }

        private static List<string> NormalizeDefaults(IEnumerable<string> ids)
        {
            List<string> normalized = new List<string>();
            if (ids == null)
            {
                return normalized;
            }

            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string trimmed = id.Trim();
                if (!normalized.Contains(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }

            return normalized;
        }
    }

    public sealed class UpgradeOfferService : IUpgradeOfferService
    {
        private enum ProgressionBand
        {
            Opening,
            Growth,
            Midgame,
            Endgame,
        }

        public List<UpgradeDef> BuildOffer(GameContent content, RunState runState, System.Random random)
        {
            List<UpgradeDef> allEligible = new List<UpgradeDef>();
            for (int i = 0; i < content.Upgrades.Count; i++)
            {
                UpgradeDef def = content.Upgrades[i];
                int stack = GetStack(runState, def.Id);
                if (stack < def.MaxStacks && runState.ElapsedSeconds >= def.UnlockAtSecond)
                {
                    allEligible.Add(def);
                }
            }

            if (allEligible.Count == 0)
            {
                return allEligible;
            }

            List<UpgradeDef> qualityPool = allEligible.Where(def => !IsPlaceholderUpgrade(def)).ToList();
            // Safety fallback: never block level-up because of over-aggressive quality filtering.
            List<UpgradeDef> pool = qualityPool.Count >= 3 ? qualityPool : allEligible;
            float elapsedSeconds = Mathf.Max(0f, runState != null ? runState.ElapsedSeconds : 0f);
            ProgressionBand band = ResolveProgressionBand(content, elapsedSeconds);
            List<UpgradeDef> progressionPool = pool
                .Where(def => IsUpgradeAllowedForBand(def, runState, band, elapsedSeconds))
                .ToList();
            if (progressionPool.Count >= 3)
            {
                pool = progressionPool;
            }

            if (pool.Count <= 3)
            {
                List<UpgradeDef> quickOffer = new List<UpgradeDef>(pool);
                EnsureAutomationReliefWindow(allEligible, quickOffer, runState, elapsedSeconds);
                return quickOffer;
            }

            Dictionary<string, float> weights = new Dictionary<string, float>();
            for (int i = 0; i < pool.Count; i++)
            {
                UpgradeDef def = pool[i];
                float weight = def.BaseWeight;
                if (def.IsAutomation)
                {
                    weight *= 1f + Mathf.Clamp01((runState.ElapsedSeconds - 45f) / 200f) * 2.4f;
                    if (runState.ElapsedSeconds >= 80f && runState.ElapsedSeconds <= 150f)
                    {
                        weight *= 1.35f;
                    }
                }

                if (def.Tags.Contains("Facility"))
                {
                    float facilityRamp = Mathf.Clamp01((runState.ElapsedSeconds - 120f) / 260f);
                    weight *= 1f + facilityRamp * 2f;
                    if (runState.ActiveFacilityCount <= 0 && runState.ElapsedSeconds >= 150f)
                    {
                        weight *= 1.35f;
                    }
                }

                if (def.EffectType == UpgradeEffectType.AddMaxDurability && elapsedSeconds <= 210f)
                {
                    weight *= 1.28f;
                }

                weight *= EvolutionWeightMultiplier(runState, def);
                weight *= Mathf.Clamp(1f + GetSynergyScore(runState, def) * 0.2f, 0.7f, 2.5f);
                weight *= GetProgressionWeight(def, runState, band, elapsedSeconds);
                weights[def.Id] = Mathf.Max(0.01f, weight);
            }

            List<UpgradeDef> offer = new List<UpgradeDef>();
            for (int pick = 0; pick < 3; pick++)
            {
                UpgradeDef selected = PickWeighted(pool, weights, offer, random);
                if (selected != null)
                {
                    offer.Add(selected);
                }
            }

            UpgradeDef guaranteedUseful = FindBestUseful(pool, runState, offer);
            if (guaranteedUseful != null && !ContainsId(offer, guaranteedUseful.Id))
            {
                if (!HasUsefulOption(offer, runState))
                {
                    offer[0] = guaranteedUseful;
                }
            }

            EnsureAutomationReliefWindow(pool, offer, runState, elapsedSeconds);

            bool lowQuality = !HasUsefulOption(offer, runState) && offer.All(u => u.Rarity == Rarity.Common);
            runState.LowQualityOfferStreak = lowQuality ? runState.LowQualityOfferStreak + 1 : 0;
            if (runState.LowQualityOfferStreak >= 2)
            {
                UpgradeDef highestRarity = pool
                    .Where(u => !ContainsId(offer, u.Id))
                    .OrderByDescending(u => u.Rarity)
                    .ThenByDescending(u => GetSynergyScore(runState, u))
                    .FirstOrDefault();

                if (highestRarity != null)
                {
                    offer[UnityEngine.Random.Range(0, offer.Count)] = highestRarity;
                    runState.LowQualityOfferStreak = 0;
                }
            }

            return offer;
        }

        private static ProgressionBand ResolveProgressionBand(GameContent content, float elapsedSeconds)
        {
            float runDuration = content != null ? Mathf.Max(120f, content.RunDurationSeconds) : 600f;
            if (elapsedSeconds < runDuration * 0.16f)
            {
                return ProgressionBand.Opening;
            }

            if (elapsedSeconds < runDuration * 0.42f)
            {
                return ProgressionBand.Growth;
            }

            if (elapsedSeconds < runDuration * 0.7f)
            {
                return ProgressionBand.Midgame;
            }

            return ProgressionBand.Endgame;
        }

        private static bool IsUpgradeAllowedForBand(
            UpgradeDef def,
            RunState runState,
            ProgressionBand band,
            float elapsedSeconds)
        {
            if (def == null)
            {
                return false;
            }

            bool hasFacility = runState != null && runState.ActiveFacilityCount > 0;
            switch (band)
            {
                case ProgressionBand.Opening:
                    if (def.Rarity == Rarity.Legendary)
                    {
                        return false;
                    }

                    if (def.Rarity == Rarity.Epic && elapsedSeconds < 125f)
                    {
                        return false;
                    }

                    switch (def.EffectType)
                    {
                        case UpgradeEffectType.AddDroneCount:
                            return elapsedSeconds >= 135f;
                        case UpgradeEffectType.UnlockAutoAim:
                            return elapsedSeconds >= 85f;
                        case UpgradeEffectType.DeployAutoHammerTower:
                        case UpgradeEffectType.DeploySensorHammer:
                        case UpgradeEffectType.DeployGoldMagnet:
                        case UpgradeEffectType.DeployBountyMarker:
                            return elapsedSeconds >= 78f;
                        case UpgradeEffectType.FacilityCooldownMultiplier:
                        case UpgradeEffectType.FacilityPowerMultiplier:
                        case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                        case UpgradeEffectType.FacilityGoldMultiplier:
                            return elapsedSeconds >= 105f && hasFacility;
                        case UpgradeEffectType.AddBossDamageMultiplier:
                            return false;
                        default:
                            return true;
                    }

                case ProgressionBand.Growth:
                    if (def.Rarity == Rarity.Epic && elapsedSeconds < 150f)
                    {
                        return false;
                    }

                    if (def.Rarity == Rarity.Legendary && elapsedSeconds < 330f)
                    {
                        return false;
                    }

                    switch (def.EffectType)
                    {
                        case UpgradeEffectType.AddDroneCount:
                            return elapsedSeconds >= 150f;
                        case UpgradeEffectType.AddBossDamageMultiplier:
                            return elapsedSeconds >= 240f;
                        case UpgradeEffectType.FacilityCooldownMultiplier:
                        case UpgradeEffectType.FacilityPowerMultiplier:
                        case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                        case UpgradeEffectType.FacilityGoldMultiplier:
                            return hasFacility || elapsedSeconds >= 180f;
                        default:
                            return true;
                    }

                case ProgressionBand.Midgame:
                    if (def.Rarity == Rarity.Legendary && elapsedSeconds < 390f)
                    {
                        return false;
                    }

                    return true;

                default:
                    return true;
            }
        }

        private static float GetProgressionWeight(
            UpgradeDef def,
            RunState runState,
            ProgressionBand band,
            float elapsedSeconds)
        {
            float weight = def.Rarity switch
            {
                Rarity.Common => band switch
                {
                    ProgressionBand.Opening => 1.35f,
                    ProgressionBand.Growth => 1.05f,
                    ProgressionBand.Midgame => 0.9f,
                    _ => 0.75f,
                },
                Rarity.Rare => band switch
                {
                    ProgressionBand.Opening => 0.62f,
                    ProgressionBand.Growth => 1.0f,
                    ProgressionBand.Midgame => 1.12f,
                    _ => 1.08f,
                },
                Rarity.Epic => band switch
                {
                    ProgressionBand.Opening => 0.28f,
                    ProgressionBand.Growth => 0.62f,
                    ProgressionBand.Midgame => 1.0f,
                    _ => 1.25f,
                },
                Rarity.Legendary => band switch
                {
                    ProgressionBand.Opening => 0.08f,
                    ProgressionBand.Growth => 0.2f,
                    ProgressionBand.Midgame => 0.6f,
                    _ => 1.38f,
                },
                _ => band switch
                {
                    ProgressionBand.Opening => 1.12f,
                    ProgressionBand.Growth => 1.08f,
                    ProgressionBand.Midgame => 1f,
                    _ => 0.9f,
                },
            };

            if (def.IsAutomation)
            {
                if (elapsedSeconds < 80f)
                {
                    weight *= 0.85f;
                }
                else if (elapsedSeconds <= 150f)
                {
                    weight *= 1.9f;
                }
                else if (elapsedSeconds < 240f)
                {
                    weight *= 1.32f;
                }
            }

            if (def.EffectType == UpgradeEffectType.AddBossDamageMultiplier)
            {
                if (elapsedSeconds < 240f)
                {
                    weight *= 0.28f;
                }
                else if (elapsedSeconds < 420f)
                {
                    weight *= 0.88f;
                }
                else
                {
                    weight *= 1.18f;
                }
            }

            if (def.EffectType == UpgradeEffectType.AddMaxDurability && band == ProgressionBand.Opening)
            {
                weight *= 1.12f;
            }

            return Mathf.Clamp(weight, 0.05f, 3.2f);
        }

        private static void EnsureAutomationReliefWindow(
            List<UpgradeDef> sourcePool,
            List<UpgradeDef> offer,
            RunState runState,
            float elapsedSeconds)
        {
            if (sourcePool == null || offer == null || runState == null || offer.Count == 0)
            {
                return;
            }

            if (elapsedSeconds < 80f || elapsedSeconds > 140f)
            {
                return;
            }

            bool alreadyHasAutomation = offer.Any(def => def != null && def.IsAutomation);
            bool alreadyReducedManualLoad = runState.Stats != null &&
                                            (runState.Stats.AutoHammerInterval > 0f ||
                                             runState.Stats.AutoAim ||
                                             runState.Stats.DroneCount > 0);
            if (alreadyHasAutomation || alreadyReducedManualLoad)
            {
                return;
            }

            UpgradeDef automationCandidate = sourcePool
                .Where(def => def != null && def.IsAutomation && !ContainsId(offer, def.Id))
                .OrderByDescending(def => def.Rarity)
                .ThenByDescending(def => def.BaseWeight + GetSynergyScore(runState, def))
                .FirstOrDefault();
            if (automationCandidate == null)
            {
                return;
            }

            int replaceIndex = -1;
            float minScore = float.MaxValue;
            for (int i = 0; i < offer.Count; i++)
            {
                UpgradeDef current = offer[i];
                if (current == null)
                {
                    replaceIndex = i;
                    break;
                }

                float score = GetSynergyScore(runState, current) +
                              (current.IsAutomation ? 2f : 0f) +
                              (int)current.Rarity * 0.55f;
                if (score < minScore)
                {
                    minScore = score;
                    replaceIndex = i;
                }
            }

            if (replaceIndex >= 0)
            {
                offer[replaceIndex] = automationCandidate;
            }
        }

        private static UpgradeDef PickWeighted(
            List<UpgradeDef> pool,
            Dictionary<string, float> weights,
            List<UpgradeDef> selected,
            System.Random random)
        {
            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                if (ContainsId(selected, pool[i].Id))
                {
                    continue;
                }

                total += weights[pool[i].Id];
            }

            if (total <= 0f)
            {
                return null;
            }

            double roll = random.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                UpgradeDef candidate = pool[i];
                if (ContainsId(selected, candidate.Id))
                {
                    continue;
                }

                acc += weights[candidate.Id];
                if (roll <= acc)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool HasUsefulOption(List<UpgradeDef> offer, RunState runState)
        {
            for (int i = 0; i < offer.Count; i++)
            {
                if (GetSynergyScore(runState, offer[i]) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static UpgradeDef FindBestUseful(List<UpgradeDef> pool, RunState runState, List<UpgradeDef> selected)
        {
            float best = float.MinValue;
            UpgradeDef bestDef = null;
            for (int i = 0; i < pool.Count; i++)
            {
                UpgradeDef candidate = pool[i];
                if (ContainsId(selected, candidate.Id))
                {
                    continue;
                }

                float score = GetSynergyScore(runState, candidate);
                if (score > best)
                {
                    best = score;
                    bestDef = candidate;
                }
            }

            return bestDef;
        }

        private static bool ContainsId(List<UpgradeDef> defs, string id)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetStack(RunState runState, string upgradeId)
        {
            return runState.UpgradeStacks.TryGetValue(upgradeId, out int value) ? value : 0;
        }

        private static float EvolutionWeightMultiplier(RunState runState, UpgradeDef upgrade)
        {
            int range = GetTagLevel(runState, "Range");
            int crit = GetTagLevel(runState, "Crit");
            if (range >= 3 && (upgrade.Tags.Contains("Range") || upgrade.Tags.Contains("Crit")))
            {
                return 1.4f;
            }

            if (crit >= 2 && upgrade.Tags.Contains("Range"))
            {
                return 1.3f;
            }

            int chain = GetTagLevel(runState, "Chain");
            int speed = GetTagLevel(runState, "AttackSpeed");
            if (chain >= 3 && (upgrade.Tags.Contains("Chain") || upgrade.Tags.Contains("AttackSpeed")))
            {
                return 1.4f;
            }

            if (speed >= 2 && upgrade.Tags.Contains("Chain"))
            {
                return 1.25f;
            }

            int auto = GetTagLevel(runState, "Automation");
            int gold = GetTagLevel(runState, "Gold");
            if (auto >= 3 && (upgrade.Tags.Contains("Automation") || upgrade.Tags.Contains("Gold")))
            {
                return 1.45f;
            }

            if (gold >= 2 && upgrade.Tags.Contains("Automation"))
            {
                return 1.25f;
            }

            return 1f;
        }

        private static int GetTagLevel(RunState runState, string tag)
        {
            return runState.TagLevels.TryGetValue(tag, out int level) ? level : 0;
        }

        private static float GetSynergyScore(RunState runState, UpgradeDef def)
        {
            float score = 0f;
            if (def.Tags.Count == 0)
            {
                return 0.5f;
            }

            for (int i = 0; i < def.Tags.Count; i++)
            {
                string tag = def.Tags[i];
                if (runState.BuildTags.Contains(tag))
                {
                    score += 1.2f;
                }

                if (tag == "Damage" || tag == "AttackSpeed" || tag == "Range")
                {
                    score += 0.5f;
                }

                if (tag == "Automation" && runState.ElapsedSeconds > 90f)
                {
                    score += 0.5f;
                }

                if (tag == "Facility")
                {
                    score += runState.ElapsedSeconds >= 120f ? 0.9f : 0.2f;
                    score += runState.ActiveFacilityCount > 0 ? 0.8f : 0f;
                }

                if (tag == "Bounty" && runState.FacilityLevels.TryGetValue(FacilityType.BountyMarker, out int bountyLevel))
                {
                    score += 0.3f + bountyLevel * 0.35f;
                }
            }

            return score;
        }

        private static bool IsPlaceholderUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(def.Id) &&
                def.Id.StartsWith("UPG_MISC_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string name = def.DisplayName ?? string.Empty;
            string desc = def.Description ?? string.Empty;
            if (desc.IndexOf("向泛用条目", StringComparison.Ordinal) >= 0 ||
                (name.IndexOf("升级", StringComparison.Ordinal) >= 0 && desc.IndexOf("向泛用", StringComparison.Ordinal) >= 0))
            {
                return true;
            }

            return false;
        }
    }

    public sealed class SpawnDirector : ISpawnDirector
    {
        public bool TrySpawn(
            GameContent content,
            RunState runState,
            SpawnerState spawnerState,
            List<HoleRuntime> holes,
            float deltaTime,
            System.Random random,
            out HoleRuntime targetHole,
            out MoleDef selectedMole)
        {
            targetHole = null;
            selectedMole = null;

            spawnerState.SpawnCooldown -= deltaTime;
            float threatMultiplier = runState.CurseRemaining > 0f ? 1.45f : 1f;
            if (runState.RogueZoneRemaining > 0f)
            {
                threatMultiplier *= 1.28f;
            }

            if (runState.BountyContractRemaining > 0f)
            {
                threatMultiplier *= 1.12f;
            }

            spawnerState.ThreatBudget = AccumulateThreat(
                spawnerState.ThreatBudget,
                runState.ElapsedSeconds,
                deltaTime,
                threatMultiplier);

            if (spawnerState.SpawnCooldown > 0f || spawnerState.ThreatBudget < 0.85f)
            {
                return false;
            }

            List<HoleRuntime> availableHoles = holes.Where(h => h.CanSpawn).ToList();
            if (availableHoles.Count == 0)
            {
                return false;
            }

            targetHole = SelectHole(availableHoles, random);
            if (targetHole == null)
            {
                return false;
            }

            selectedMole = SelectMoleForBudget(
                content.Moles,
                runState.ElapsedSeconds,
                spawnerState.ThreatBudget,
                random,
                targetHole.RareWeightMultiplier *
                (runState.BountyContractRemaining > 0f ? 1.8f : 1f) *
                (runState.RogueZoneRemaining > 0f ? 1.25f : 1f));
            if (selectedMole == null)
            {
                return false;
            }

            spawnerState.ThreatBudget = Mathf.Max(0f, spawnerState.ThreatBudget - selectedMole.ThreatCost);
            spawnerState.SpawnedCount++;
            float spawnSpeed = Mathf.Max(0.13f, 0.62f - runState.ElapsedSeconds * 0.0005f);
            if (runState.ElapsedSeconds < 120f)
            {
                spawnSpeed += 0.06f;
            }

            if (runState.ElapsedSeconds < 60f)
            {
                spawnSpeed += 0.09f;
            }

            if (runState.ActiveFacilityCount <= 0 && runState.ElapsedSeconds < 180f)
            {
                spawnSpeed += 0.03f;
            }

            spawnerState.SpawnCooldown = spawnSpeed;
            return true;
        }

        public static float AccumulateThreat(float currentBudget, float elapsedSeconds, float deltaTime, float multiplier)
        {
            float easing = Mathf.Clamp01(elapsedSeconds / 180f);
            float baseThreat = Mathf.Lerp(0.92f, 1.35f, easing);
            float rampThreat = Mathf.Clamp(elapsedSeconds / 300f, 0f, 2.8f);
            if (elapsedSeconds < 120f)
            {
                rampThreat *= 0.72f;
            }

            if (elapsedSeconds < 75f)
            {
                rampThreat *= 0.52f;
            }

            float threatPerSecond = baseThreat + rampThreat;
            return currentBudget + (threatPerSecond * multiplier * deltaTime);
        }

        public static MoleDef SelectMoleForBudget(
            IList<MoleDef> moles,
            float elapsedSeconds,
            float budget,
            System.Random random,
            float rareWeightMultiplier = 1f)
        {
            List<MoleDef> candidates = new List<MoleDef>();
            float totalWeight = 0f;
            for (int i = 0; i < moles.Count; i++)
            {
                MoleDef mole = moles[i];
                if (elapsedSeconds < mole.MinTime || elapsedSeconds > mole.MaxTime)
                {
                    continue;
                }

                if (mole.ThreatCost > budget + 0.2f)
                {
                    continue;
                }

                float stageBonus = 1f + (int)mole.Rarity * Mathf.Clamp01(elapsedSeconds / 420f);
                float rarityBoost = 1f + Mathf.Max(0f, rareWeightMultiplier - 1f) * (int)mole.Rarity * 0.35f;
                float weight = mole.SpawnWeight * stageBonus * rarityBoost;
                if (weight <= 0f)
                {
                    continue;
                }

                candidates.Add(mole);
                totalWeight += weight;
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
            {
                return null;
            }

            double roll = random.NextDouble() * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                MoleDef mole = candidates[i];
                float stageBonus = 1f + (int)mole.Rarity * Mathf.Clamp01(elapsedSeconds / 420f);
                float rarityBoost = 1f + Mathf.Max(0f, rareWeightMultiplier - 1f) * (int)mole.Rarity * 0.35f;
                cumulative += mole.SpawnWeight * stageBonus * rarityBoost;
                if (roll <= cumulative)
                {
                    return mole;
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static HoleRuntime SelectHole(IList<HoleRuntime> availableHoles, System.Random random)
        {
            float total = 0f;
            for (int i = 0; i < availableHoles.Count; i++)
            {
                HoleRuntime hole = availableHoles[i];
                float holeWeight = hole.SpawnWeight * (1f + hole.DangerLevel * 0.15f);
                holeWeight *= Mathf.Sqrt(Mathf.Max(1f, hole.RareWeightMultiplier));
                holeWeight *= Mathf.Sqrt(Mathf.Max(1f, hole.GoldRewardMultiplier));
                total += holeWeight;
            }

            if (total <= 0f)
            {
                return null;
            }

            double roll = random.NextDouble() * total;
            float cumulative = 0f;
            for (int i = 0; i < availableHoles.Count; i++)
            {
                HoleRuntime hole = availableHoles[i];
                float holeWeight = hole.SpawnWeight * (1f + hole.DangerLevel * 0.15f);
                holeWeight *= Mathf.Sqrt(Mathf.Max(1f, hole.RareWeightMultiplier));
                holeWeight *= Mathf.Sqrt(Mathf.Max(1f, hole.GoldRewardMultiplier));
                cumulative += holeWeight;
                if (roll <= cumulative)
                {
                    return hole;
                }
            }

            return availableHoles[availableHoles.Count - 1];
        }
    }

    public sealed class AutomationService : IAutomationService
    {
        public void Tick(
            GameContent content,
            RunState runState,
            AutomationState automationState,
            List<HoleRuntime> holes,
            float deltaTime,
            Action<HoleRuntime, float, AttackSource> damageCallback,
            Func<bool> hasBoss,
            Action<float, AttackSource> bossDamageCallback)
        {
            if (runState.Stats.AutoHammerInterval > 0.01f)
            {
                automationState.AutoHammerTimer -= deltaTime;
                if (automationState.AutoHammerTimer <= 0f)
                {
                    HoleRuntime target = holes
                        .Where(h => h.HasLiveMole)
                        .OrderByDescending(h => h.CurrentMole.Def.GoldReward)
                        .ThenBy(h => h.CurrentMole.RemainingHp)
                        .FirstOrDefault();

                    if (target != null)
                    {
                        damageCallback(target, runState.Stats.Damage * 0.9f, AttackSource.AutoHammer);
                    }
                    else if (hasBoss())
                    {
                        bossDamageCallback(runState.Stats.Damage * 0.75f * runState.Stats.BossDamageMultiplier, AttackSource.AutoHammer);
                    }

                    automationState.AutoHammerTimer = Mathf.Max(0.14f, runState.Stats.AutoHammerInterval);
                }
            }

            if (runState.Stats.DroneCount > 0)
            {
                automationState.DroneTimer -= deltaTime;
                float droneInterval = Mathf.Max(0.12f, 0.85f - runState.Stats.DroneCount * 0.08f);
                if (automationState.DroneTimer <= 0f)
                {
                    List<HoleRuntime> live = holes.Where(h => h.HasLiveMole).ToList();
                    for (int i = 0; i < runState.Stats.DroneCount; i++)
                    {
                        if (live.Count > 0)
                        {
                            int pick = UnityEngine.Random.Range(0, live.Count);
                            damageCallback(live[pick], runState.Stats.Damage * 0.46f, AttackSource.Drone);
                        }
                        else if (hasBoss())
                        {
                            bossDamageCallback(runState.Stats.Damage * 0.36f * runState.Stats.BossDamageMultiplier, AttackSource.Drone);
                        }
                    }

                    automationState.DroneTimer = droneInterval;
                }
            }
        }
    }

    public sealed class FacilityService : IFacilityService
    {
        public bool TryDeployFacility(
            GameContent content,
            RunState runState,
            List<HoleRuntime> holes,
            FacilityType type,
            out HoleRuntime deployedHole)
        {
            deployedHole = null;
            if (content == null || runState == null || holes == null || holes.Count == 0)
            {
                return false;
            }

            HoleRuntime existing = holes.FirstOrDefault(h => h.Facility != null && h.Facility.Type == type);
            int level = Mathf.Max(1, GetFacilityLevel(runState, type));
            if (existing != null)
            {
                existing.Facility.Level = level;
                deployedHole = existing;
                return true;
            }

            FacilityDef def = ResolveDef(content, type);
            if (def == null)
            {
                return false;
            }

            List<HoleRuntime> candidates = holes.Where(h => h.CanInstallFacility).ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            deployedHole = candidates
                .OrderByDescending(h => ScoreForDeployment(h, type))
                .FirstOrDefault();
            if (deployedHole == null)
            {
                return false;
            }

            FacilityRuntime runtime = new FacilityRuntime
            {
                Def = def,
                Type = type,
                State = FacilityState.Idle,
                Level = level,
                TriggerCount = 0,
                CooldownTimer = 0f,
                TriggerTimer = 0f,
            };

            deployedHole.InstallFacility(runtime);
            ApplyPassives(runState, deployedHole, runtime, false, false);
            return true;
        }

        public void Tick(
            GameContent content,
            RunState runState,
            List<HoleRuntime> holes,
            float deltaTime,
            Func<bool> hasBoss,
            Action<HoleRuntime, float, AttackSource> holeDamageCallback,
            Action<float, AttackSource> bossDamageCallback)
        {
            if (runState == null || holes == null)
            {
                return;
            }

            if (runState.FacilityOverloadTimer > 0f)
            {
                runState.FacilityOverloadTimer = Mathf.Max(0f, runState.FacilityOverloadTimer - deltaTime);
            }

            bool overloadActive = runState.FacilityOverloadTimer > 0f;
            bool hasLiveBoss = hasBoss != null && hasBoss();
            bool curseActive = runState.CurseRemaining > 0f;
            int activeCount = 0;

            for (int i = 0; i < holes.Count; i++)
            {
                HoleRuntime hole = holes[i];
                FacilityRuntime facility = hole.Facility;
                if (facility == null)
                {
                    continue;
                }

                facility.Def ??= ResolveDef(content, facility.Type);
                if (facility.Def == null)
                {
                    continue;
                }

                facility.Level = Mathf.Max(1, GetFacilityLevel(runState, facility.Type));
                ApplyPassives(runState, hole, facility, overloadActive, curseActive);
                TickSingleFacility(
                    runState,
                    holes,
                    hole,
                    facility,
                    deltaTime,
                    overloadActive,
                    hasLiveBoss,
                    holeDamageCallback,
                    bossDamageCallback);
                activeCount++;
            }

            runState.ActiveFacilityCount = activeCount;
            if (!overloadActive &&
                activeCount > 0 &&
                runState.FacilityTriggerCount >= runState.FacilityOverloadThresholdCurrent)
            {
                runState.FacilityOverloadTimer = 6f + Mathf.Min(3f, activeCount * 0.4f);
                runState.FacilityOverloadCount++;
                runState.FacilityTriggerCount = 0;
                runState.FacilityOverloadThresholdCurrent = Mathf.Clamp(
                    Mathf.RoundToInt(runState.FacilityOverloadThresholdCurrent * 1.22f),
                    14,
                    70);
            }
        }

        public bool BoostFacilitiesForRepair(List<HoleRuntime> holes, float cooldownReductionScale)
        {
            if (holes == null || holes.Count == 0)
            {
                return false;
            }

            bool boosted = false;
            FacilityRuntime lowest = null;
            for (int i = 0; i < holes.Count; i++)
            {
                FacilityRuntime facility = holes[i].Facility;
                if (facility == null)
                {
                    continue;
                }

                boosted = true;
                float reduction = Mathf.Clamp01(cooldownReductionScale);
                facility.CooldownTimer *= 1f - reduction;
                facility.TriggerTimer *= 1f - reduction * 0.7f;
                if (facility.CooldownTimer <= 0f && facility.TriggerTimer <= 0f)
                {
                    facility.State = FacilityState.Idle;
                }

                if (lowest == null || facility.Level < lowest.Level)
                {
                    lowest = facility;
                }
            }

            if (lowest != null)
            {
                lowest.Level += 1;
            }

            return boosted;
        }

        private static void TickSingleFacility(
            RunState runState,
            List<HoleRuntime> holes,
            HoleRuntime hole,
            FacilityRuntime facility,
            float deltaTime,
            bool overloadActive,
            bool hasBoss,
            Action<HoleRuntime, float, AttackSource> holeDamageCallback,
            Action<float, AttackSource> bossDamageCallback)
        {
            float triggerDelay = Mathf.Max(0.01f, facility.Def.TriggerDelay * (overloadActive ? 0.65f : 1f));
            float interval = ResolveInterval(runState, facility, overloadActive);

            if (facility.TriggerTimer > 0f)
            {
                facility.TriggerTimer -= deltaTime;
                facility.State = overloadActive ? FacilityState.Overload : FacilityState.Trigger;
                if (facility.TriggerTimer <= 0f)
                {
                    bool triggered = ExecuteTrigger(runState, holes, hole, facility, overloadActive, hasBoss, holeDamageCallback, bossDamageCallback);
                    facility.CooldownTimer = interval;
                    facility.State = overloadActive ? FacilityState.Overload : FacilityState.Cooldown;
                    if (triggered)
                    {
                        runState.FacilityTriggerCount++;
                    }
                }

                return;
            }

            if (facility.CooldownTimer > 0f)
            {
                facility.CooldownTimer -= deltaTime;
                facility.State = overloadActive ? FacilityState.Overload : FacilityState.Cooldown;
                return;
            }

            if (ShouldTrigger(holes, hole, facility, hasBoss))
            {
                facility.TriggerTimer = triggerDelay;
                facility.State = FacilityState.Trigger;
                return;
            }

            facility.State = overloadActive ? FacilityState.Overload : FacilityState.Idle;
            if (facility.Type == FacilityType.GoldMagnet)
            {
                facility.CooldownTimer = interval;
                facility.TriggerTimer = triggerDelay;
            }
        }

        private static bool ExecuteTrigger(
            RunState runState,
            List<HoleRuntime> holes,
            HoleRuntime anchorHole,
            FacilityRuntime facility,
            bool overloadActive,
            bool hasBoss,
            Action<HoleRuntime, float, AttackSource> holeDamageCallback,
            Action<float, AttackSource> bossDamageCallback)
        {
            float range = facility.Def.BaseRadius + (facility.Level - 1) * 0.45f;
            float damage = ResolveDamage(runState, facility, overloadActive);
            bool triggered = false;

            switch (facility.Type)
            {
                case FacilityType.AutoHammerTower:
                {
                    HoleRuntime target = SelectTargetHole(holes, anchorHole, range);
                    if (target != null)
                    {
                        holeDamageCallback?.Invoke(target, damage, AttackSource.Facility);
                        triggered = true;
                    }
                    else if (hasBoss)
                    {
                        bossDamageCallback?.Invoke(damage * 0.55f, AttackSource.Facility);
                        triggered = true;
                    }

                    break;
                }
                case FacilityType.SensorHammer:
                {
                    HoleRuntime target = anchorHole.HasLiveMole
                        ? anchorHole
                        : SelectTargetHole(holes, anchorHole, range * 1.2f);
                    if (target != null)
                    {
                        holeDamageCallback?.Invoke(target, damage * 1.15f, AttackSource.Facility);
                        triggered = true;
                    }
                    else if (hasBoss)
                    {
                        bossDamageCallback?.Invoke(damage * 0.48f, AttackSource.Facility);
                        triggered = true;
                    }

                    break;
                }
                case FacilityType.GoldMagnet:
                {
                    if (anchorHole.HasLiveMole)
                    {
                        holeDamageCallback?.Invoke(anchorHole, damage * 0.42f, AttackSource.Facility);
                        triggered = true;
                    }

                    break;
                }
                case FacilityType.BountyMarker:
                {
                    if (anchorHole.HasLiveMole)
                    {
                        holeDamageCallback?.Invoke(anchorHole, damage * 0.72f, AttackSource.Facility);
                        triggered = true;
                    }
                    else if (hasBoss && runState.ElapsedSeconds > 540f)
                    {
                        bossDamageCallback?.Invoke(damage * 0.42f, AttackSource.Facility);
                        triggered = true;
                    }

                    break;
                }
            }

            if (triggered)
            {
                facility.TriggerCount++;
            }

            return triggered;
        }

        private static bool ShouldTrigger(List<HoleRuntime> holes, HoleRuntime hole, FacilityRuntime facility, bool hasBoss)
        {
            if (facility.Type == FacilityType.GoldMagnet)
            {
                return true;
            }

            if (hole.HasLiveMole)
            {
                return true;
            }

            float radius = facility.Def.BaseRadius + Mathf.Max(0f, facility.Level - 1) * 0.4f;
            for (int i = 0; i < holes.Count; i++)
            {
                HoleRuntime other = holes[i];
                if (!other.HasLiveMole)
                {
                    continue;
                }

                if (Vector2.Distance(other.Position, hole.Position) <= radius)
                {
                    return true;
                }
            }

            return hasBoss;
        }

        private static HoleRuntime SelectTargetHole(List<HoleRuntime> holes, HoleRuntime anchor, float radius)
        {
            return holes
                .Where(h => h.HasLiveMole && Vector2.Distance(h.Position, anchor.Position) <= Mathf.Max(0.1f, radius))
                .OrderByDescending(h => h.CurrentMole.Def.Rarity)
                .ThenByDescending(h => h.CurrentMole.Def.GoldReward)
                .ThenBy(h => h.CurrentMole.RemainingHp)
                .FirstOrDefault();
        }

        private static float ResolveInterval(RunState runState, FacilityRuntime facility, bool overloadActive)
        {
            float interval = Mathf.Max(0.08f, facility.Def.BaseInterval);
            interval /= 1f + Mathf.Max(0, facility.Level - 1) * 0.18f;
            interval *= Mathf.Clamp(runState.FacilityCooldownMultiplier, 0.45f, 1.2f);
            if (runState.CurseRemaining > 0f)
            {
                interval *= 0.92f;
            }

            if (overloadActive)
            {
                interval *= 0.5f;
            }

            return Mathf.Max(0.08f, interval);
        }

        private static float ResolveDamage(RunState runState, FacilityRuntime facility, bool overloadActive)
        {
            float damage = facility.Def.BasePower * (1f + Mathf.Max(0, facility.Level - 1) * 0.28f);
            damage *= Mathf.Clamp(runState.FacilityPowerMultiplier, 0.6f, 4f);
            if (runState.CurseRemaining > 0f)
            {
                damage *= 1.08f;
            }

            if (overloadActive)
            {
                damage *= 1.35f;
            }

            return Mathf.Max(1f, damage);
        }

        private static void ApplyPassives(
            RunState runState,
            HoleRuntime hole,
            FacilityRuntime facility,
            bool overloadActive,
            bool curseActive)
        {
            float overdrive = overloadActive ? 1.18f : 1f;
            float curse = curseActive ? 1.08f : 1f;

            float rareMultiplier = 1f;
            float goldMultiplier = 1f;
            float magnetRadius = 0f;
            switch (facility.Type)
            {
                case FacilityType.GoldMagnet:
                    magnetRadius = (facility.Def.BaseRadius + (facility.Level - 1) * 0.35f) * overdrive * curse;
                    goldMultiplier = 1f + (0.08f + (facility.Level - 1) * 0.03f)
                        * runState.FacilityGoldMultiplier
                        * overdrive;
                    break;
                case FacilityType.BountyMarker:
                    rareMultiplier = 1f + (0.2f + (facility.Level - 1) * 0.06f)
                        * runState.FacilityPowerMultiplier
                        * overdrive
                        * curse;
                    goldMultiplier = 1f + (0.12f + (facility.Level - 1) * 0.04f)
                        * runState.FacilityGoldMultiplier
                        * overdrive;
                    break;
                case FacilityType.AutoHammerTower:
                    goldMultiplier = 1f + Mathf.Max(0f, facility.Level - 1) * 0.05f * runState.FacilityGoldMultiplier;
                    break;
                case FacilityType.SensorHammer:
                    rareMultiplier = 1f + Mathf.Max(0f, facility.Level - 1) * 0.08f * overdrive;
                    break;
            }

            hole.ApplyFacilityPassives(rareMultiplier, goldMultiplier, magnetRadius);
        }

        private static int GetFacilityLevel(RunState runState, FacilityType type)
        {
            if (runState.FacilityLevels != null &&
                runState.FacilityLevels.TryGetValue(type, out int level))
            {
                return level;
            }

            return 1;
        }

        private static FacilityDef ResolveDef(GameContent content, FacilityType type)
        {
            if (content?.Facilities == null)
            {
                return null;
            }

            return content.Facilities.FirstOrDefault(f => f != null && f.Type == type);
        }

        private static float ScoreForDeployment(HoleRuntime hole, FacilityType type)
        {
            float score = hole.DangerLevel * 1.9f + hole.SpawnWeight * 2.1f;
            score += hole.RareWeightMultiplier * 1.6f;
            score += hole.GoldRewardMultiplier * 1.25f;
            if (hole.HasLiveMole)
            {
                score += 0.8f;
            }

            switch (type)
            {
                case FacilityType.AutoHammerTower:
                    score += hole.DangerLevel * 0.5f;
                    break;
                case FacilityType.SensorHammer:
                    score += hole.DangerLevel * 0.35f;
                    break;
                case FacilityType.GoldMagnet:
                    score += hole.SpawnWeight * 0.6f;
                    break;
                case FacilityType.BountyMarker:
                    score += hole.DangerLevel * 0.65f + hole.SpawnWeight * 0.5f;
                    break;
            }

            return score;
        }
    }

    public sealed class BossEncounterService : IBossEncounterService
    {
        public List<BossEncounterRuntime> CreateTimeline(GameContent content)
        {
            List<BossEncounterRuntime> timeline = new List<BossEncounterRuntime>();
            if (content == null)
            {
                return timeline;
            }

            if (content.BossEncounters != null && content.BossEncounters.Count > 0)
            {
                for (int i = 0; i < content.BossEncounters.Count; i++)
                {
                    BossEncounterDef def = content.BossEncounters[i];
                    if (def == null)
                    {
                        continue;
                    }

                    BossDef boss = content.Bosses.FirstOrDefault(b => b != null && b.Id == def.BossId);
                    if (boss == null)
                    {
                        continue;
                    }

                    timeline.Add(new BossEncounterRuntime
                    {
                        Def = def,
                        Boss = boss,
                        Spawned = false,
                        Defeated = false,
                        ShieldActive = false,
                        ShieldCycleTimer = Mathf.Max(0.5f, def.ShieldCycleSeconds),
                        ShieldDurationTimer = 0f,
                        RogueHoleTimer = Mathf.Max(0.8f, def.RogueHoleInterval),
                    });
                }
            }

            if (timeline.Count == 0 && content.Bosses.Count > 0)
            {
                BossDef fallbackBoss = content.Bosses[0];
                BossEncounterDef fallback = ScriptableObject.CreateInstance<BossEncounterDef>();
                fallback.Id = "fallback_final";
                fallback.DisplayName = fallbackBoss.DisplayName;
                fallback.BossId = fallbackBoss.Id;
                fallback.SpawnAtSecond = 600f;
                fallback.IsFinalBoss = true;
                timeline.Add(new BossEncounterRuntime
                {
                    Def = fallback,
                    Boss = fallbackBoss,
                    ShieldCycleTimer = Mathf.Max(0.5f, fallback.ShieldCycleSeconds),
                    RogueHoleTimer = Mathf.Max(0.8f, fallback.RogueHoleInterval),
                });
            }

            return timeline
                .OrderBy(t => t.Def.SpawnAtSecond)
                .ThenBy(t => t.Def.Id)
                .ToList();
        }

        public BossEncounterRuntime FindEncounterToSpawn(List<BossEncounterRuntime> timeline, float elapsedSeconds)
        {
            if (timeline == null || timeline.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < timeline.Count; i++)
            {
                BossEncounterRuntime runtime = timeline[i];
                if (runtime == null || runtime.Def == null || runtime.Boss == null)
                {
                    continue;
                }

                if (runtime.Spawned)
                {
                    continue;
                }

                if (elapsedSeconds >= runtime.Def.SpawnAtSecond)
                {
                    return runtime;
                }
            }

            return null;
        }

        public float ResolveSpawnScale(BossEncounterRuntime activeEncounter)
        {
            if (activeEncounter == null || activeEncounter.Def == null || activeEncounter.Defeated)
            {
                return 1f;
            }

            return Mathf.Clamp(activeEncounter.Def.SpawnScale, 0.35f, 1f);
        }

        public void TickActiveEncounter(
            RunState runState,
            BossEncounterRuntime activeEncounter,
            float deltaTime,
            List<HoleRuntime> holes,
            Action<HoleRuntime, float> rogueHoleCallback,
            Action<bool> shieldStateCallback)
        {
            if (runState == null || activeEncounter == null || activeEncounter.Def == null || activeEncounter.Defeated)
            {
                return;
            }

            TickShield(activeEncounter, deltaTime, shieldStateCallback);
            TickRogueZone(runState, activeEncounter, deltaTime, holes, rogueHoleCallback);
        }

        private static void TickShield(
            BossEncounterRuntime activeEncounter,
            float deltaTime,
            Action<bool> shieldStateCallback)
        {
            if (activeEncounter.Def.ShieldDuration <= 0f || activeEncounter.Def.ShieldCycleSeconds <= 0f)
            {
                if (activeEncounter.ShieldActive)
                {
                    activeEncounter.ShieldActive = false;
                    shieldStateCallback?.Invoke(false);
                }

                return;
            }

            if (activeEncounter.ShieldActive)
            {
                activeEncounter.ShieldDurationTimer -= deltaTime;
                if (activeEncounter.ShieldDurationTimer <= 0f)
                {
                    activeEncounter.ShieldActive = false;
                    activeEncounter.ShieldCycleTimer = Mathf.Max(0.5f, activeEncounter.Def.ShieldCycleSeconds);
                    shieldStateCallback?.Invoke(false);
                }

                return;
            }

            activeEncounter.ShieldCycleTimer -= deltaTime;
            if (activeEncounter.ShieldCycleTimer <= 0f)
            {
                activeEncounter.ShieldActive = true;
                activeEncounter.ShieldDurationTimer = Mathf.Max(0.4f, activeEncounter.Def.ShieldDuration);
                shieldStateCallback?.Invoke(true);
            }
        }

        private static void TickRogueZone(
            RunState runState,
            BossEncounterRuntime activeEncounter,
            float deltaTime,
            List<HoleRuntime> holes,
            Action<HoleRuntime, float> rogueHoleCallback)
        {
            if (holes == null || holes.Count == 0)
            {
                return;
            }

            if (activeEncounter.Def.RogueHoleInterval <= 0f || activeEncounter.Def.RogueHoleDuration <= 0f || activeEncounter.Def.RogueHoleCount <= 0)
            {
                return;
            }

            activeEncounter.RogueHoleTimer -= deltaTime;
            if (activeEncounter.RogueHoleTimer > 0f)
            {
                return;
            }

            List<HoleRuntime> selected = holes
                .OrderByDescending(h => h.DangerLevel * 2f + h.SpawnWeight)
                .ThenBy(h => h.Index)
                .Take(Mathf.Clamp(activeEncounter.Def.RogueHoleCount, 1, Mathf.Max(1, holes.Count)))
                .ToList();
            for (int i = 0; i < selected.Count; i++)
            {
                rogueHoleCallback?.Invoke(selected[i], activeEncounter.Def.RogueHoleDuration);
            }

            runState.RogueZoneBurstCount++;
            runState.RogueZoneRemaining = Mathf.Max(runState.RogueZoneRemaining, activeEncounter.Def.RogueHoleDuration);
            activeEncounter.RogueHoleTimer = Mathf.Max(0.8f, activeEncounter.Def.RogueHoleInterval);
        }
    }

    public sealed class AchievementService
    {
        public List<AchievementDef> Evaluate(GameContent content, RunState runState, MetaProgressState metaState)
        {
            List<AchievementDef> unlocked = new List<AchievementDef>();
            for (int i = 0; i < content.Achievements.Count; i++)
            {
                AchievementDef def = content.Achievements[i];
                if (MetaStateUtils.HasAchievement(metaState, def.Id))
                {
                    continue;
                }

                if (!IsComplete(def, runState, metaState))
                {
                    continue;
                }

                metaState.AchievementIds.Add(def.Id);
                metaState.WorkshopChips += def.RewardChips;
                unlocked.Add(def);
            }

            return unlocked;
        }

        public bool IsComplete(AchievementDef def, RunState runState, MetaProgressState metaState)
        {
            switch (def.Trigger)
            {
                case AchievementTrigger.KillCountInRun:
                    return runState.TotalKills >= def.TargetInt;
                case AchievementTrigger.ComboInRun:
                    return runState.HighestCombo >= def.TargetInt;
                case AchievementTrigger.GoldInRun:
                    return runState.Gold >= def.TargetInt;
                case AchievementTrigger.BossWin:
                    return runState.RunWon;
                case AchievementTrigger.AutoKillsInRun:
                    return runState.AutoKills >= def.TargetInt;
                case AchievementTrigger.CodexDiscoveries:
                    return metaState.CodexEntries.Count >= def.TargetInt;
                case AchievementTrigger.LifetimeRuns:
                    return metaState.TotalRuns >= def.TargetInt;
                case AchievementTrigger.LifetimeWins:
                    return metaState.TotalWins >= def.TargetInt;
                case AchievementTrigger.MetaNodesPurchased:
                    return MetaStateUtils.PurchasedNodeCount(metaState) >= def.TargetInt;
                case AchievementTrigger.CoreShardsInRun:
                    return runState.CoreShards >= def.TargetInt;
                default:
                    return false;
            }
        }
    }

    public static class DefaultContentFactory
    {
        public static bool LastLoadFromConfig { get; private set; }
        public static string LastLoadSummary { get; private set; } = "Not loaded yet.";

        public static GameContent CreateDefault()
        {
            if (ConfigDrivenContentLoader.TryLoad(out GameContent configured, out string sourceSummary))
            {
                LastLoadFromConfig = true;
                LastLoadSummary = sourceSummary;
                Debug.Log($"[MoleSurvivors] Loaded config-driven content: {sourceSummary}");
                return configured;
            }

            LastLoadFromConfig = false;
            LastLoadSummary = sourceSummary;
            Debug.LogWarning($"[MoleSurvivors] Falling back to built-in content. Reason: {sourceSummary}");
            GameContent content = ScriptableObject.CreateInstance<GameContent>();
            BuildMoles(content);
            BuildWeapons(content);
            BuildCharacters(content);
            BuildFacilities(content);
            BuildUpgrades(content);
            BuildBosses(content);
            BuildBossEncounters(content);
            BuildMetaNodes(content);
            BuildCodex(content);
            BuildAchievements(content);
            BuildEvents(content);
            content.DefaultWeaponId = content.Weapons.FirstOrDefault()?.Id ?? string.Empty;
            content.DefaultCharacterId = content.Characters.FirstOrDefault()?.Id ?? string.Empty;
            content.SecondaryWeaponUnlockId = content.Weapons.Skip(1).FirstOrDefault()?.Id ?? string.Empty;
            content.TertiaryWeaponUnlockId = content.Weapons.Skip(2).FirstOrDefault()?.Id ?? string.Empty;
            content.SecondaryCharacterUnlockId = content.Characters.Skip(1).FirstOrDefault()?.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(content.DefaultWeaponId))
            {
                content.StartupUnlockedWeaponIds.Add(content.DefaultWeaponId);
            }

            if (!string.IsNullOrWhiteSpace(content.DefaultCharacterId))
            {
                content.StartupUnlockedCharacterIds.Add(content.DefaultCharacterId);
            }
            return content;
        }

        private static void BuildMoles(GameContent content)
        {
            content.Moles.Add(Mole("mole_common", "普通鼠", Rarity.Common, MoleTrait.None, 10f, 0.22f, 1.2f, 0.55f, 5, 6, 0, 1f, 0f, 999f, 8f, new Color(0.6f, 0.45f, 0.3f)));
            content.Moles.Add(Mole("mole_swift", "迅捷鼠", Rarity.Rare, MoleTrait.Fast, 8f, 0.18f, 0.78f, 0.5f, 8, 8, 0, 1.3f, 40f, 999f, 4.5f, new Color(0.45f, 0.65f, 0.95f)));
            content.Moles.Add(Mole("mole_tank", "坦克鼠", Rarity.Rare, MoleTrait.Tank, 30f, 0.3f, 1.45f, 0.8f, 14, 12, 1, 2.4f, 75f, 999f, 2.8f, new Color(0.35f, 0.55f, 0.7f)));
            content.Moles.Add(Mole("mole_bomb", "炸弹鼠", Rarity.Rare, MoleTrait.Bomb, 15f, 0.2f, 0.95f, 0.6f, 10, 10, 1, 1.7f, 60f, 999f, 3.2f, new Color(0.95f, 0.45f, 0.35f)));
            content.Moles.Add(Mole("mole_chest", "宝箱鼠", Rarity.Epic, MoleTrait.Chest, 18f, 0.2f, 0.86f, 0.7f, 30, 18, 3, 2.9f, 90f, 999f, 1.8f, new Color(0.92f, 0.8f, 0.3f)));
            content.Moles.Add(Mole("mole_chain", "连锁鼠", Rarity.Epic, MoleTrait.Chain, 20f, 0.2f, 0.88f, 0.72f, 18, 16, 1, 2.5f, 140f, 999f, 2f, new Color(0.62f, 0.42f, 0.95f)));
            content.Moles.Add(Mole("mole_shield", "护盾鼠", Rarity.Epic, MoleTrait.Shield, 26f, 0.24f, 1.05f, 0.75f, 22, 18, 2, 3f, 180f, 999f, 1.7f, new Color(0.5f, 0.9f, 0.85f)));
            content.Moles.Add(Mole("mole_elite", "精英鼠", Rarity.Legendary, MoleTrait.Elite, 64f, 0.28f, 1.1f, 0.9f, 45, 30, 5, 4.5f, 240f, 999f, 0.9f, new Color(0.95f, 0.65f, 0.2f)));
        }

        private static void BuildWeapons(GameContent content)
        {
            WeaponDef wood = ScriptableObject.CreateInstance<WeaponDef>();
            wood.Id = "weapon_wood";
            wood.DisplayName = "木锤";
            wood.Description = "均衡基础，适合范围+暴击构筑";
            wood.Damage = 10f;
            wood.AttackInterval = 0.45f;
            wood.AttackRadius = 0.35f;
            wood.CritChance = 0.05f;
            wood.CritDamage = 1.6f;
            wood.EvolutionId = "evo_quake";
            wood.EvolutionRequirements.Add(new TagRequirement { Tag = "Range", Level = 5 });
            wood.EvolutionRequirements.Add(new TagRequirement { Tag = "Crit", Level = 3 });
            content.Weapons.Add(wood);

            WeaponDef lightning = ScriptableObject.CreateInstance<WeaponDef>();
            lightning.Id = "weapon_lightning";
            lightning.DisplayName = "雷电锤";
            lightning.Description = "偏连锁传导，清群更快";
            lightning.Damage = 8f;
            lightning.AttackInterval = 0.38f;
            lightning.AttackRadius = 0.34f;
            lightning.CritChance = 0.06f;
            lightning.CritDamage = 1.5f;
            lightning.ChainCount = 1;
            lightning.EvolutionId = "evo_storm";
            lightning.EvolutionRequirements.Add(new TagRequirement { Tag = "Chain", Level = 5 });
            lightning.EvolutionRequirements.Add(new TagRequirement { Tag = "AttackSpeed", Level = 4 });
            content.Weapons.Add(lightning);

            WeaponDef drone = ScriptableObject.CreateInstance<WeaponDef>();
            drone.Id = "weapon_drone";
            drone.DisplayName = "无人机锤";
            drone.Description = "自动化核心，后期清场";
            drone.Damage = 9f;
            drone.AttackInterval = 0.5f;
            drone.AttackRadius = 0.32f;
            drone.CritChance = 0.04f;
            drone.CritDamage = 1.5f;
            drone.AutoHammerInterval = 1.8f;
            drone.DroneCount = 1;
            drone.EvolutionId = "evo_reaper";
            drone.EvolutionRequirements.Add(new TagRequirement { Tag = "Automation", Level = 6 });
            drone.EvolutionRequirements.Add(new TagRequirement { Tag = "Gold", Level = 3 });
            content.Weapons.Add(drone);
        }

        private static void BuildCharacters(GameContent content)
        {
            CharacterDef hammerer = ScriptableObject.CreateInstance<CharacterDef>();
            hammerer.Id = "char_hammerer";
            hammerer.DisplayName = "锤手";
            hammerer.Description = "新手友好，基础伤害更高";
            hammerer.DamageMultiplier = 1.12f;
            hammerer.RangeBonus = 0.05f;
            hammerer.AutomationMultiplier = 1f;
            content.Characters.Add(hammerer);

            CharacterDef engineer = ScriptableObject.CreateInstance<CharacterDef>();
            engineer.Id = "char_engineer";
            engineer.DisplayName = "工程师";
            engineer.Description = "自动化强化，自动锤更快";
            engineer.DamageMultiplier = 0.95f;
            engineer.RangeBonus = 0.03f;
            engineer.AutomationMultiplier = 1.2f;
            content.Characters.Add(engineer);
        }

        private static void BuildUpgrades(GameContent content)
        {
            for (int i = 1; i <= 5; i++)
            {
                AddUpgrade(content, $"up_damage_{i}", $"重锤训练 {ToRoman(i)}", "基础伤害提高", Rarity.Common, "Damage", UpgradeEffectType.AddDamage, 1.6f + i * 0.6f, 1, 0, 1.2f, false, false, "Damage");
            }

            for (int i = 1; i <= 4; i++)
            {
                AddUpgrade(content, $"up_speed_{i}", $"疾速节奏 {ToRoman(i)}", "攻击间隔缩短", Rarity.Common, "AttackSpeed", UpgradeEffectType.AttackIntervalMultiplier, 0.94f - i * 0.02f, 1, 0, 1.15f, false, false, "AttackSpeed");
            }

            for (int i = 1; i <= 4; i++)
            {
                int maxStacks = i == 4 ? 2 : 1;
                AddUpgrade(content, $"up_range_{i}", $"扩域锤风 {ToRoman(i)}", "点击判定范围提高", Rarity.Common, "Range", UpgradeEffectType.AddRange, 0.06f + i * 0.02f, maxStacks, 10, 1.12f, false, false, "Range");
            }

            for (int i = 1; i <= 3; i++)
            {
                AddUpgrade(content, $"up_crit_{i}", $"致命压击 {ToRoman(i)}", "暴击率提升", Rarity.Rare, "Crit", UpgradeEffectType.AddCritChance, 0.03f + i * 0.01f, 1, 35, 0.9f, false, false, "Crit");
            }

            for (int i = 1; i <= 1; i++)
            {
                AddUpgrade(content, $"up_critdmg_{i}", $"穿甲回响 {ToRoman(i)}", "暴击伤害提升", Rarity.Rare, "Crit", UpgradeEffectType.AddCritDamage, 0.24f + i * 0.06f, 1, 55, 0.85f, false, false, "Crit");
            }

            for (int i = 1; i <= 3; i++)
            {
                AddUpgrade(content, $"up_chain_{i}", $"电链导体 {ToRoman(i)}", "连锁命中数量提升", Rarity.Rare, "Chain", UpgradeEffectType.AddChainCount, 1f, 2, 75, 0.82f, false, false, "Chain", "Lightning");
            }

            for (int i = 1; i <= 2; i++)
            {
                AddUpgrade(content, $"up_splash_{i}", $"震地冲击 {ToRoman(i)}", "命中后附带溅射", Rarity.Rare, "Splash", UpgradeEffectType.AddSplash, 0.38f + i * 0.08f, 1, 90, 0.8f, false, false, "Splash", "Range");
            }

            for (int i = 1; i <= 2; i++)
            {
                int maxStacks = i == 2 ? 2 : 1;
                AddUpgrade(content, $"up_gold_{i}", $"印钞协议 {ToRoman(i)}", "金币收益提升", Rarity.Rare, "Gold", UpgradeEffectType.AddGoldMultiplier, 0.09f + i * 0.03f, maxStacks, 45, 0.95f, false, false, "Gold", "Economy");
            }

            for (int i = 1; i <= 2; i++)
            {
                AddUpgrade(content, $"up_exp_{i}", $"学习加速 {ToRoman(i)}", "经验收益提升", Rarity.Common, "Exp", UpgradeEffectType.AddExpMultiplier, 0.1f + i * 0.05f, 1, 20, 1f, false, false, "Exp");
            }

            AddUpgrade(content, "up_autohammer_1", "自动锤核心", "解锁周期自动锤", Rarity.Epic, "Automation", UpgradeEffectType.UnlockAutoHammer, 1.35f, 3, 90, 0.75f, true, false, "Automation");
            AddUpgrade(content, "up_autohammer_2", "自动锤提频", "自动锤触发更频繁", Rarity.Epic, "Automation", UpgradeEffectType.AutoHammerIntervalMultiplier, 0.82f, 2, 120, 0.7f, true, false, "Automation");
            AddUpgrade(content, "up_autoaim", "价值锁定算法", "自动优先锁定高价值目标", Rarity.Epic, "Automation", UpgradeEffectType.UnlockAutoAim, 1f, 1, 145, 0.66f, true, false, "Automation", "Chain");
            AddUpgrade(content, "up_drone", "无人机扩编", "额外增加无人机单位", Rarity.Epic, "Automation", UpgradeEffectType.AddDroneCount, 1f, 2, 170, 0.62f, true, false, "Automation", "Drone");

            AddUpgrade(content, "up_facility_tower", "自动锤塔部署", "部署自动锤塔并提升同类等级", Rarity.Epic, "Facility", UpgradeEffectType.DeployAutoHammerTower, 1f, 2, 120, 0.78f, true, false, "Facility", "Automation");
            AddUpgrade(content, "up_facility_sensor", "感应雷锤部署", "部署感应雷锤并提升触发效率", Rarity.Epic, "Facility", UpgradeEffectType.DeploySensorHammer, 1f, 2, 140, 0.75f, true, false, "Facility", "Automation", "Chain");
            AddUpgrade(content, "up_facility_magnet", "金币吸附器部署", "部署局部吸金设施", Rarity.Epic, "Facility", UpgradeEffectType.DeployGoldMagnet, 1f, 2, 155, 0.77f, true, false, "Facility", "Economy", "Gold");
            AddUpgrade(content, "up_facility_bounty", "赏金标记器部署", "部署赏金设施，提升稀有刷怪", Rarity.Legendary, "Facility", UpgradeEffectType.DeployBountyMarker, 1f, 2, 175, 0.58f, true, false, "Facility", "Bounty", "Economy");
            AddUpgrade(content, "up_facility_cooldown_1", "产线提频 I", "设施冷却缩短", Rarity.Rare, "Facility", UpgradeEffectType.FacilityCooldownMultiplier, 0.88f, 1, 180, 0.88f, true, false, "Facility", "Automation");
            AddUpgrade(content, "up_facility_cooldown_2", "产线提频 II", "设施冷却进一步缩短", Rarity.Epic, "Facility", UpgradeEffectType.FacilityCooldownMultiplier, 0.84f, 1, 260, 0.68f, true, false, "Facility", "Automation");
            AddUpgrade(content, "up_facility_power_1", "锤压增幅 I", "设施打击强度提升", Rarity.Rare, "Facility", UpgradeEffectType.FacilityPowerMultiplier, 0.16f, 1, 190, 0.84f, true, false, "Facility", "Damage");
            AddUpgrade(content, "up_facility_power_2", "锤压增幅 II", "设施打击强度进一步提升", Rarity.Epic, "Facility", UpgradeEffectType.FacilityPowerMultiplier, 0.2f, 1, 280, 0.62f, true, false, "Facility", "Damage");
            AddUpgrade(content, "up_facility_overload", "超载阈值重构", "更快进入设施超载", Rarity.Epic, "Facility", UpgradeEffectType.FacilityOverloadThresholdMultiplier, 0.86f, 1, 220, 0.72f, true, false, "Facility", "Automation");
            AddUpgrade(content, "up_facility_gold", "工厂分红协议", "设施相关收益提高", Rarity.Rare, "Facility", UpgradeEffectType.FacilityGoldMultiplier, 0.16f, 2, 210, 0.8f, true, false, "Facility", "Economy", "Gold");

            if (content.Upgrades.Count < 30 || content.Upgrades.Count > 40)
            {
                Debug.LogWarning($"Upgrade count is {content.Upgrades.Count}, expected 30-40 for Round3.");
            }
        }

        private static void BuildFacilities(GameContent content)
        {
            content.Facilities.Add(Facility(
                "facility_auto_hammer",
                "自动锤塔",
                "周期打击本洞与邻洞目标。",
                FacilityType.AutoHammerTower,
                1.15f,
                8f,
                2.1f,
                0.05f,
                110,
                18f,
                6f,
                "Facility",
                "Automation"));

            content.Facilities.Add(Facility(
                "facility_sensor_hammer",
                "感应雷锤",
                "目标冒头后触发延迟重击。",
                FacilityType.SensorHammer,
                1.35f,
                9f,
                1.8f,
                0.18f,
                130,
                18f,
                6f,
                "Facility",
                "Chain",
                "Automation"));

            content.Facilities.Add(Facility(
                "facility_gold_magnet",
                "金币吸附器",
                "局部掉落自动吸附并提升结算效率。",
                FacilityType.GoldMagnet,
                1.8f,
                4f,
                1.25f,
                0.06f,
                145,
                16f,
                5.2f,
                "Facility",
                "Economy",
                "Gold"));

            content.Facilities.Add(Facility(
                "facility_bounty_marker",
                "赏金标记器",
                "提升洞口稀有目标权重与收益。",
                FacilityType.BountyMarker,
                1.55f,
                7f,
                2f,
                0.08f,
                170,
                20f,
                6.6f,
                "Facility",
                "Bounty",
                "Economy"));
        }

        private static void BuildBosses(GameContent content)
        {
            BossDef midBoss = ScriptableObject.CreateInstance<BossDef>();
            midBoss.Id = "boss_foreman";
            midBoss.DisplayName = "钻头监工鼠";
            midBoss.Hp = 980f;
            midBoss.AttackInterval = 2.8f;
            midBoss.DurabilityDamage = 1;
            midBoss.RewardGold = 180;
            midBoss.RewardCore = 8;
            midBoss.TintColor = new Color(0.93f, 0.7f, 0.24f);
            content.Bosses.Add(midBoss);

            BossDef finalBoss = ScriptableObject.CreateInstance<BossDef>();
            finalBoss.Id = "boss_rat_king";
            finalBoss.DisplayName = "巨牙鼠王";
            finalBoss.Hp = 2400f;
            finalBoss.AttackInterval = 2.2f;
            finalBoss.DurabilityDamage = 2;
            finalBoss.RewardGold = 400;
            finalBoss.RewardCore = 18;
            finalBoss.TintColor = new Color(0.9f, 0.25f, 0.22f);
            content.Bosses.Add(finalBoss);
        }

        private static void BuildBossEncounters(GameContent content)
        {
            content.BossEncounters.Add(BossEncounter(
                "enc_mid_300",
                "中期验收",
                "boss_foreman",
                300f,
                false,
                1f,
                0.6f,
                10.5f,
                2.6f,
                8f,
                4.2f,
                4));

            content.BossEncounters.Add(BossEncounter(
                "enc_final_600",
                "终局收割",
                "boss_rat_king",
                600f,
                true,
                1.12f,
                0.55f,
                9f,
                3.2f,
                7f,
                4.8f,
                5));
        }

        private static void BuildMetaNodes(GameContent content)
        {
            AddTrack(content, "meta_damage", "锻锤体术", MetaEffectType.AddStartDamage, 0.8f, 0.45f, 35, 4);
            AddTrack(content, "meta_speed", "反应神经", MetaEffectType.AttackIntervalMultiplier, 0.97f, -0.02f, 38, 4);
            AddTrack(content, "meta_range", "挥击覆盖", MetaEffectType.AddStartRange, 0.03f, 0.025f, 34, 4);
            AddTrack(content, "meta_durability", "农场耐久", MetaEffectType.AddMaxDurability, 1f, 1f, 42, 4);
            AddTrack(content, "meta_gold", "财务增幅", MetaEffectType.AddGoldMultiplier, 0.04f, 0.03f, 45, 4);
            AddTrack(content, "meta_exp", "经验萃取", MetaEffectType.AddExpMultiplier, 0.05f, 0.03f, 43, 4);

            MetaNodeDef startGold = ScriptableObject.CreateInstance<MetaNodeDef>();
            startGold.Id = "meta_startgold";
            startGold.DisplayName = "启动资金";
            startGold.Description = "开局额外获得金币";
            startGold.EffectType = MetaEffectType.AddStartingGold;
            startGold.Value = 80f;
            startGold.Cost = 85;
            startGold.MaxLevel = 3;
            startGold.Requires.Add("meta_gold_2");
            content.MetaNodes.Add(startGold);

            MetaNodeDef unlockLightning = ScriptableObject.CreateInstance<MetaNodeDef>();
            unlockLightning.Id = "meta_unlock_lightning";
            unlockLightning.DisplayName = "解锁雷电锤";
            unlockLightning.Description = "解锁武器：雷电锤";
            unlockLightning.EffectType = MetaEffectType.UnlockLightningWeapon;
            unlockLightning.Cost = 120;
            unlockLightning.MaxLevel = 1;
            unlockLightning.Requires.Add("meta_damage_2");
            content.MetaNodes.Add(unlockLightning);

            MetaNodeDef unlockDrone = ScriptableObject.CreateInstance<MetaNodeDef>();
            unlockDrone.Id = "meta_unlock_drone";
            unlockDrone.DisplayName = "解锁无人机锤";
            unlockDrone.Description = "解锁武器：无人机锤";
            unlockDrone.EffectType = MetaEffectType.UnlockDroneWeapon;
            unlockDrone.Cost = 150;
            unlockDrone.MaxLevel = 1;
            unlockDrone.Requires.Add("meta_exp_2");
            content.MetaNodes.Add(unlockDrone);

            MetaNodeDef unlockEngineer = ScriptableObject.CreateInstance<MetaNodeDef>();
            unlockEngineer.Id = "meta_unlock_engineer";
            unlockEngineer.DisplayName = "解锁角色：工程师";
            unlockEngineer.Description = "解锁自动化专精角色";
            unlockEngineer.EffectType = MetaEffectType.UnlockEngineerCharacter;
            unlockEngineer.Cost = 180;
            unlockEngineer.MaxLevel = 1;
            unlockEngineer.Requires.Add("meta_speed_3");
            content.MetaNodes.Add(unlockEngineer);
        }

        private static void BuildCodex(GameContent content)
        {
            for (int i = 0; i < content.Moles.Count; i++)
            {
                MoleDef mole = content.Moles[i];
                content.CodexEntries.Add(Codex($"codex_{mole.Id}", mole.DisplayName, "地鼠", $"遭遇并击败：{mole.DisplayName}"));
            }

            for (int i = 0; i < content.Weapons.Count; i++)
            {
                WeaponDef weapon = content.Weapons[i];
                content.CodexEntries.Add(Codex($"codex_{weapon.Id}", weapon.DisplayName, "武器", weapon.Description));
            }

            for (int i = 0; i < content.Facilities.Count; i++)
            {
                FacilityDef facility = content.Facilities[i];
                content.CodexEntries.Add(Codex(
                    $"codex_{facility.Id}",
                    facility.DisplayName,
                    "设施",
                    facility.Description));
            }

            content.CodexEntries.Add(Codex("codex_boss_foreman", "钻头监工鼠", "Boss", "在 5:00 现身的中期验收 Boss。"));
            content.CodexEntries.Add(Codex("codex_boss_rat_king", "巨牙鼠王", "Boss", "在 10:00 现身的终局 Boss。"));
            content.CodexEntries.Add(Codex("codex_boss", "巨牙鼠王", "Boss", "终局 Boss 图鉴别名条目。"));
            content.CodexEntries.Add(Codex("codex_event_merchant", "流动商人", "事件", "付费购买临时强化。"));
            content.CodexEntries.Add(Codex("codex_event_treasure", "暴富时刻", "事件", "短时间金币掉落翻倍。"));
            content.CodexEntries.Add(Codex("codex_event_curse", "诅咒祭坛", "事件", "难度提高但收益更高。"));
            content.CodexEntries.Add(Codex("codex_event_repair", "维修站", "事件", "恢复农场耐久。"));
            content.CodexEntries.Add(Codex("codex_event_bounty", "赏金合约", "事件", "短时提升稀有目标出现与收益。"));
            content.CodexEntries.Add(Codex("codex_event_rogue", "暴走洞区", "事件", "指定洞区进入高压高收益状态。"));
        }

        private static void BuildAchievements(GameContent content)
        {
            content.Achievements.Add(Achievement("ach_first_hunt", "首次狩猎", "单局击杀 50 只地鼠", AchievementTrigger.KillCountInRun, 50, 0f, 20));
            content.Achievements.Add(Achievement("ach_combo_20", "连击机器", "单局达到 20 连击", AchievementTrigger.ComboInRun, 20, 0f, 30));
            content.Achievements.Add(Achievement("ach_gold_2k", "小富即安", "单局金币达到 2000", AchievementTrigger.GoldInRun, 2000, 0f, 40));
            content.Achievements.Add(Achievement("ach_auto_80", "半自动工厂", "单局自动击杀达到 80", AchievementTrigger.AutoKillsInRun, 80, 0f, 35));
            content.Achievements.Add(Achievement("ach_boss_win", "鼠王终结者", "击败巨牙鼠王", AchievementTrigger.BossWin, 1, 0f, 60));
            content.Achievements.Add(Achievement("ach_codex_10", "收集起步", "解锁 10 个图鉴条目", AchievementTrigger.CodexDiscoveries, 10, 0f, 35));
            content.Achievements.Add(Achievement("ach_runs_3", "持续经营", "累计进行 3 局", AchievementTrigger.LifetimeRuns, 3, 0f, 20));
            content.Achievements.Add(Achievement("ach_wins_1", "第一场胜利", "累计获得 1 场胜利", AchievementTrigger.LifetimeWins, 1, 0f, 35));
            content.Achievements.Add(Achievement("ach_meta_12", "工坊达人", "购买 12 个成长节点", AchievementTrigger.MetaNodesPurchased, 12, 0f, 50));
            content.Achievements.Add(Achievement("ach_core_20", "核心收割", "单局核心碎片达到 20", AchievementTrigger.CoreShardsInRun, 20, 0f, 40));
        }

        private static void BuildEvents(GameContent content)
        {
            content.Events.Add(Event("event_merchant", "流动商人", "支付金币，随机获得一项立即生效强化。", RunEventType.MerchantBoost, 70f, 999f, 150, 1f));
            content.Events.Add(Event("event_treasure", "暴富时刻", "30 秒内金币收益翻倍。", RunEventType.TreasureRush, 95f, 999f, 0, 30f));
            content.Events.Add(Event("event_curse", "诅咒祭坛", "35 秒威胁上涨且收益更高。", RunEventType.CurseAltar, 120f, 999f, 0, 35f));
            content.Events.Add(Event("event_repair", "维修站", "恢复农场耐久。", RunEventType.RepairStation, 60f, 999f, 0, 3f));
            content.Events.Add(Event("event_bounty", "赏金合约", "25 秒内提升稀有目标权重和收益。", RunEventType.BountyContract, 160f, 999f, 0, 25f));
            content.Events.Add(Event("event_rogue_zone", "暴走洞区", "高压洞区持续 22 秒，刷新加速且收益上调。", RunEventType.RogueHoleZone, 220f, 999f, 0, 22f));
        }

        private static void AddTrack(
            GameContent content,
            string idPrefix,
            string name,
            MetaEffectType effectType,
            float startValue,
            float stepValue,
            int baseCost,
            int levels)
        {
            string previous = string.Empty;
            for (int i = 1; i <= levels; i++)
            {
                MetaNodeDef node = ScriptableObject.CreateInstance<MetaNodeDef>();
                node.Id = $"{idPrefix}_{i}";
                node.DisplayName = $"{name} {ToRoman(i)}";
                node.Description = "永久提升基础能力";
                node.EffectType = effectType;
                node.Value = startValue + stepValue * (i - 1);
                node.Cost = baseCost + i * 18;
                node.MaxLevel = 1;
                if (!string.IsNullOrEmpty(previous))
                {
                    node.Requires.Add(previous);
                }

                previous = node.Id;
                content.MetaNodes.Add(node);
            }
        }

        private static MoleDef Mole(
            string id,
            string name,
            Rarity rarity,
            MoleTrait traits,
            float hp,
            float warning,
            float up,
            float cooldown,
            int gold,
            int exp,
            int core,
            float threat,
            float minTime,
            float maxTime,
            float weight,
            Color color)
        {
            MoleDef def = ScriptableObject.CreateInstance<MoleDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Rarity = rarity;
            def.Traits = traits;
            def.BaseHp = hp;
            def.WarningSeconds = warning;
            def.UpSeconds = up;
            def.CooldownSeconds = cooldown;
            def.GoldReward = gold;
            def.ExpReward = exp;
            def.CoreReward = core;
            def.ThreatCost = threat;
            def.MinTime = minTime;
            def.MaxTime = maxTime;
            def.SpawnWeight = weight;
            def.TintColor = color;
            return def;
        }

        private static UpgradeDef AddUpgrade(
            GameContent content,
            string id,
            string name,
            string description,
            Rarity rarity,
            string category,
            UpgradeEffectType effectType,
            float value,
            int maxStacks,
            int unlockAt,
            float weight,
            bool isAutomation,
            bool isLegendary,
            params string[] tags)
        {
            UpgradeDef def = ScriptableObject.CreateInstance<UpgradeDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = description;
            def.Rarity = rarity;
            def.Category = category;
            def.EffectType = effectType;
            def.Value = value;
            def.MaxStacks = maxStacks;
            def.UnlockAtSecond = unlockAt;
            def.BaseWeight = weight;
            def.IsAutomation = isAutomation;
            def.IsLegendary = isLegendary;
            def.Tags.AddRange(tags);
            content.Upgrades.Add(def);
            return def;
        }

        private static CodexDef Codex(string id, string displayName, string category, string description)
        {
            CodexDef def = ScriptableObject.CreateInstance<CodexDef>();
            def.Id = id;
            def.DisplayName = displayName;
            def.Category = category;
            def.Description = description;
            return def;
        }

        private static AchievementDef Achievement(
            string id,
            string name,
            string description,
            AchievementTrigger trigger,
            int targetInt,
            float targetFloat,
            int rewardChips)
        {
            AchievementDef def = ScriptableObject.CreateInstance<AchievementDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = description;
            def.Trigger = trigger;
            def.TargetInt = targetInt;
            def.TargetFloat = targetFloat;
            def.RewardChips = rewardChips;
            return def;
        }

        private static RunEventDef Event(
            string id,
            string name,
            string description,
            RunEventType type,
            float minTime,
            float maxTime,
            int goldCost,
            float value)
        {
            RunEventDef def = ScriptableObject.CreateInstance<RunEventDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = description;
            def.Type = type;
            def.MinTime = minTime;
            def.MaxTime = maxTime;
            def.GoldCost = goldCost;
            def.Value = value;
            return def;
        }

        private static string ToRoman(int number)
        {
            switch (number)
            {
                case 1:
                    return "I";
                case 2:
                    return "II";
                case 3:
                    return "III";
                case 4:
                    return "IV";
                case 5:
                    return "V";
                default:
                    return number.ToString();
            }
        }

        private static FacilityDef Facility(
            string id,
            string name,
            string description,
            FacilityType type,
            float baseInterval,
            float basePower,
            float baseRadius,
            float triggerDelay,
            int unlockAtSecond,
            float overloadThreshold,
            float overloadDuration,
            params string[] tags)
        {
            FacilityDef def = ScriptableObject.CreateInstance<FacilityDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = description;
            def.Type = type;
            def.BaseInterval = baseInterval;
            def.BasePower = basePower;
            def.BaseRadius = baseRadius;
            def.TriggerDelay = triggerDelay;
            def.UnlockAtSecond = unlockAtSecond;
            def.OverloadThreshold = overloadThreshold;
            def.OverloadDuration = overloadDuration;
            def.Tags.AddRange(tags);
            return def;
        }

        private static BossEncounterDef BossEncounter(
            string id,
            string name,
            string bossId,
            float spawnAtSecond,
            bool isFinal,
            float hpMultiplier,
            float spawnScale,
            float shieldCycleSeconds,
            float shieldDuration,
            float rogueHoleInterval,
            float rogueHoleDuration,
            int rogueHoleCount)
        {
            BossEncounterDef def = ScriptableObject.CreateInstance<BossEncounterDef>();
            def.Id = id;
            def.DisplayName = name;
            def.BossId = bossId;
            def.SpawnAtSecond = spawnAtSecond;
            def.IsFinalBoss = isFinal;
            def.HpMultiplier = hpMultiplier;
            def.SpawnScale = spawnScale;
            def.ShieldCycleSeconds = shieldCycleSeconds;
            def.ShieldDuration = shieldDuration;
            def.RogueHoleInterval = rogueHoleInterval;
            def.RogueHoleDuration = rogueHoleDuration;
            def.RogueHoleCount = rogueHoleCount;
            return def;
        }
    }
}
