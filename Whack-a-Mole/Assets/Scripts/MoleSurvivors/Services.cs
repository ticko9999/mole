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

            if (state.RecentRuns == null)
            {
                state.RecentRuns = new List<RunSummary>();
            }

            if (string.IsNullOrWhiteSpace(state.SelectedDifficultyId))
            {
                state.SelectedDifficultyId = "DIFF_NORMAL";
            }

            if (state.SelectedChallengeId == null)
            {
                state.SelectedChallengeId = string.Empty;
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

    public sealed class JsonSettingsRepository : ISettingsRepository
    {
        private const int CurrentVersion = 1;

        public JsonSettingsRepository(string savePath = null)
        {
            SavePath = string.IsNullOrWhiteSpace(savePath)
                ? Path.Combine(Application.persistentDataPath, "mole_survivors_client_settings_v1.json")
                : savePath;
        }

        public string SavePath { get; }

        public ClientSettingsState LoadOrCreate()
        {
            if (!File.Exists(SavePath))
            {
                ClientSettingsState created = CreateDefault();
                Save(created);
                return created;
            }

            try
            {
                string json = File.ReadAllText(SavePath);
                ClientSettingsState loaded = JsonUtility.FromJson<ClientSettingsState>(json);
                if (loaded == null)
                {
                    ClientSettingsState fallback = CreateDefault();
                    Save(fallback);
                    return fallback;
                }

                loaded.SaveVersion = CurrentVersion;
                EnsureDefaults(loaded);
                return loaded;
            }
            catch
            {
                ClientSettingsState fallback = CreateDefault();
                Save(fallback);
                return fallback;
            }
        }

        public void Save(ClientSettingsState state)
        {
            if (state == null)
            {
                state = CreateDefault();
            }

            state.SaveVersion = CurrentVersion;
            EnsureDefaults(state);
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(state, true);
            File.WriteAllText(SavePath, json);
        }

        private static ClientSettingsState CreateDefault()
        {
            ClientSettingsState state = new ClientSettingsState();
            EnsureDefaults(state);
            return state;
        }

        private static void EnsureDefaults(ClientSettingsState state)
        {
            if (state == null)
            {
                return;
            }

            state.BgmVolume = Mathf.Clamp01(state.BgmVolume);
            state.SfxVolume = Mathf.Clamp01(state.SfxVolume);
            state.EffectsQuality = Mathf.Clamp(state.EffectsQuality, 0, 2);
            state.ResolutionWidth = Mathf.Max(1280, state.ResolutionWidth);
            state.ResolutionHeight = Mathf.Max(720, state.ResolutionHeight);
            if (string.IsNullOrWhiteSpace(state.DifficultyId))
            {
                state.DifficultyId = "DIFF_NORMAL";
            }

            if (state.ChallengeId == null)
            {
                state.ChallengeId = string.Empty;
            }
        }
    }

    public sealed class GameFlowService : IGameFlowService
    {
        public bool CanOpenPauseMenu(bool upgradeOpen, bool eventOpen, bool metaOpen, bool endOpen)
        {
            return !upgradeOpen && !eventOpen && !metaOpen && !endOpen;
        }
    }

    public sealed class CombatCueService : ICombatCueService
    {
        private readonly Action<AudioCueDef> _audioDispatch;
        private readonly Action<VfxCueDef> _vfxDispatch;
        private readonly Action<string> _warningLogger;
        private readonly List<AudioCueDef> _audioCues = new List<AudioCueDef>();
        private readonly List<VfxCueDef> _vfxCues = new List<VfxCueDef>();
        private readonly HashSet<string> _missingWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CombatCueService(
            GameContent content,
            Action<AudioCueDef> audioDispatch,
            Action<VfxCueDef> vfxDispatch,
            Action<string> warningLogger)
        {
            _audioDispatch = audioDispatch;
            _vfxDispatch = vfxDispatch;
            _warningLogger = warningLogger;

            if (content?.AudioCues != null)
            {
                _audioCues.AddRange(content.AudioCues.Where(cue => cue != null));
            }

            if (content?.VfxCues != null)
            {
                _vfxCues.AddRange(content.VfxCues.Where(cue => cue != null));
            }
        }

        public void OnHit(bool crit, bool killed, bool boss)
        {
            if (boss)
            {
                DispatchAudio("Boss预警", "Boss阶段");
                DispatchVfx("Boss", "警示");
                return;
            }

            if (crit)
            {
                DispatchAudio("暴击");
                DispatchVfx("暴击");
                return;
            }

            if (killed)
            {
                DispatchAudio("爆燃", "普通命中");
                DispatchVfx("爆燃", "锤击");
                return;
            }

            DispatchAudio("普通命中");
            DispatchVfx("锤击", "Hit");
        }

        public void OnEvent(RunEventType type)
        {
            switch (type)
            {
                case RunEventType.MerchantBoost:
                    DispatchAudio("商店打开", "事件触发");
                    DispatchVfx("波次转场", "State");
                    break;
                case RunEventType.TreasureRush:
                    DispatchAudio("狂热触发", "事件触发");
                    DispatchVfx("金币喷发", "狂热");
                    break;
                case RunEventType.CurseAltar:
                    DispatchAudio("事件触发");
                    DispatchVfx("地图危险波", "State");
                    break;
                case RunEventType.RepairStation:
                    DispatchAudio("设施触发", "事件触发");
                    DispatchVfx("设施激活", "State");
                    break;
                case RunEventType.BountyContract:
                    DispatchAudio("稀有鼠出现", "事件触发");
                    DispatchVfx("传奇鼠高亮", "State");
                    break;
                case RunEventType.RogueHoleZone:
                    DispatchAudio("狂热触发", "事件触发");
                    DispatchVfx("地图危险波", "State");
                    break;
            }
        }

        private void DispatchAudio(params string[] keywords)
        {
            AudioCueDef cue = FindBestAudioCue(keywords);
            if (cue != null)
            {
                _audioDispatch?.Invoke(cue);
                return;
            }

            WarnMissing($"Audio cue not found for keywords: {string.Join("/", keywords ?? Array.Empty<string>())}");
        }

        private void DispatchVfx(params string[] keywords)
        {
            VfxCueDef cue = FindBestVfxCue(keywords);
            if (cue != null)
            {
                _vfxDispatch?.Invoke(cue);
                return;
            }

            WarnMissing($"VFX cue not found for keywords: {string.Join("/", keywords ?? Array.Empty<string>())}");
        }

        private AudioCueDef FindBestAudioCue(params string[] keywords)
        {
            if (_audioCues.Count == 0 || keywords == null || keywords.Length == 0)
            {
                return _audioCues.FirstOrDefault();
            }

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                AudioCueDef match = _audioCues.FirstOrDefault(cue =>
                    ContainsKeyword(cue.Id, keyword) ||
                    ContainsKeyword(cue.Name, keyword) ||
                    ContainsKeyword(cue.TriggerDescription, keyword));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private VfxCueDef FindBestVfxCue(params string[] keywords)
        {
            if (_vfxCues.Count == 0 || keywords == null || keywords.Length == 0)
            {
                return _vfxCues.FirstOrDefault();
            }

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                VfxCueDef match = _vfxCues.FirstOrDefault(cue =>
                    ContainsKeyword(cue.Id, keyword) ||
                    ContainsKeyword(cue.Name, keyword) ||
                    ContainsKeyword(cue.Type, keyword) ||
                    ContainsKeyword(cue.Description, keyword));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static bool ContainsKeyword(string source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void WarnMissing(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (_missingWarnings.Add(message))
            {
                _warningLogger?.Invoke(message);
            }
        }
    }

    public sealed class UpgradeOfferService : IUpgradeOfferService
    {
        private const float OpeningFunctionalWindowSeconds = 120f;
        private const int OpeningPrimaryPoolTarget = 36;
        private const string OpeningRouteAutoTower = "auto_tower";
        private const string OpeningRouteChainGrid = "chain_grid";
        private const string OpeningRouteBountyFactory = "bounty_factory";

        private enum ProgressionBand
        {
            Opening,
            Growth,
            Midgame,
            Endgame,
        }

        public List<UpgradeDef> BuildOffer(GameContent content, RunState runState, System.Random random)
        {
            if (content == null || runState == null)
            {
                return new List<UpgradeDef>();
            }

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
            List<UpgradeDef> pool = qualityPool.Count > 0 ? qualityPool : allEligible;
            float elapsedSeconds = Mathf.Max(0f, runState != null ? runState.ElapsedSeconds : 0f);
            ProgressionBand band = ResolveProgressionBand(content, elapsedSeconds);
            List<UpgradeDef> progressionPool = pool
                .Where(def => IsUpgradeAllowedForBand(def, runState, band, elapsedSeconds))
                .ToList();
            if (progressionPool.Count >= 3)
            {
                pool = progressionPool;
            }

            List<UpgradeDef> openingFocusedPool = BuildOpeningFocusedPool(pool, runState, elapsedSeconds);
            if (openingFocusedPool.Count >= 3)
            {
                pool = openingFocusedPool;
            }

            if (pool.Count < 3)
            {
                List<UpgradeDef> fallbackPool = qualityPool.Count > 0 ? qualityPool : allEligible;
                List<UpgradeDef> quickOffer = BuildGuaranteedOffer(pool, fallbackPool, runState, random);
                if (!HasUsefulOption(quickOffer, runState))
                {
                    UpgradeDef useful = FindBestUseful(fallbackPool, runState, quickOffer);
                    if (useful != null && quickOffer.Count > 0)
                    {
                        quickOffer[0] = useful;
                    }
                }

                EnsureAutomationReliefWindow(content, allEligible, quickOffer, runState, elapsedSeconds);
                EnsureEarlyFunctionalGuarantee(allEligible, quickOffer, runState, elapsedSeconds);
                NormalizeCoreTags(quickOffer);
                EnsureBaselineCombatOption(allEligible, quickOffer);
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

                if (def.EffectType == UpgradeEffectType.AddActiveHole)
                {
                    bool canExpand = runState.ActiveHoleCount <= 0 || runState.ActiveHoleCount < runState.MaxHoleCount;
                    if (!canExpand)
                    {
                        weight *= 0.2f;
                    }
                    else if (elapsedSeconds <= 120f)
                    {
                        weight *= 2.2f;
                    }
                    else
                    {
                        weight *= 1.15f;
                    }
                }

                if (elapsedSeconds <= OpeningFunctionalWindowSeconds)
                {
                    if (IsOpeningFunctionalUpgrade(def))
                    {
                        weight *= 1.65f;
                    }
                    else if (IsOpeningPureNumericUpgrade(def))
                    {
                        weight *= 0.62f;
                    }
                }

                weight *= GetOpeningRouteWeight(runState, def, elapsedSeconds);

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

            if (offer.Count < 3)
            {
                List<UpgradeDef> fallback = BuildGuaranteedOffer(offer, pool, runState, random);
                offer.Clear();
                offer.AddRange(fallback);
            }

            UpgradeDef guaranteedUseful = FindBestUseful(pool, runState, offer);
            if (guaranteedUseful != null && !ContainsId(offer, guaranteedUseful.Id))
            {
                if (!HasUsefulOption(offer, runState))
                {
                    offer[0] = guaranteedUseful;
                }
            }

            EnsureAutomationReliefWindow(content, pool, offer, runState, elapsedSeconds);
            EnsureEarlyFunctionalGuarantee(pool, offer, runState, elapsedSeconds);

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

            NormalizeCoreTags(offer);
            EnsureBaselineCombatOption(pool, offer);
            return offer;
        }

        private static List<UpgradeDef> BuildGuaranteedOffer(
            List<UpgradeDef> preferredPool,
            List<UpgradeDef> fallbackPool,
            RunState runState,
            System.Random random)
        {
            List<UpgradeDef> offer = new List<UpgradeDef>(3);
            if (preferredPool != null)
            {
                for (int i = 0; i < preferredPool.Count && offer.Count < 3; i++)
                {
                    UpgradeDef def = preferredPool[i];
                    if (def == null || ContainsId(offer, def.Id))
                    {
                        continue;
                    }

                    offer.Add(def);
                }
            }

            if (fallbackPool != null && offer.Count < 3)
            {
                List<UpgradeDef> candidates = fallbackPool
                    .Where(def => def != null && !ContainsId(offer, def.Id))
                    .OrderByDescending(def => GetSynergyScore(runState, def))
                    .ThenByDescending(def => def.BaseWeight)
                    .ThenBy(def => def.UnlockAtSecond)
                    .ToList();

                for (int i = 0; i < candidates.Count && offer.Count < 3; i++)
                {
                    offer.Add(candidates[i]);
                }
            }

            if (offer.Count >= 3)
            {
                return offer;
            }

            List<UpgradeDef> pool = new List<UpgradeDef>();
            if (preferredPool != null)
            {
                pool.AddRange(preferredPool);
            }

            if (fallbackPool != null)
            {
                for (int i = 0; i < fallbackPool.Count; i++)
                {
                    UpgradeDef def = fallbackPool[i];
                    if (def != null && !pool.Contains(def))
                    {
                        pool.Add(def);
                    }
                }
            }

            while (offer.Count < 3 && pool.Count > 0)
            {
                int index = random != null ? random.Next(0, pool.Count) : UnityEngine.Random.Range(0, pool.Count);
                UpgradeDef def = pool[index];
                if (def != null && !ContainsId(offer, def.Id))
                {
                    offer.Add(def);
                }

                pool.RemoveAt(index);
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
                            return elapsedSeconds >= 95f;
                        case UpgradeEffectType.UnlockAutoAim:
                            return elapsedSeconds >= 70f;
                        case UpgradeEffectType.DeployAutoHammerTower:
                        case UpgradeEffectType.DeploySensorHammer:
                        case UpgradeEffectType.DeployGoldMagnet:
                        case UpgradeEffectType.DeployBountyMarker:
                        case UpgradeEffectType.DeployTeslaCoupler:
                        case UpgradeEffectType.DeployExecutionPlate:
                            return elapsedSeconds >= 40f;
                        case UpgradeEffectType.FacilityCooldownMultiplier:
                        case UpgradeEffectType.FacilityPowerMultiplier:
                        case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                        case UpgradeEffectType.FacilityGoldMultiplier:
                            return elapsedSeconds >= 90f && hasFacility;
                        case UpgradeEffectType.AddActiveHole:
                            return true;
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
                            return elapsedSeconds >= 120f;
                        case UpgradeEffectType.AddBossDamageMultiplier:
                            return elapsedSeconds >= 240f;
                        case UpgradeEffectType.FacilityCooldownMultiplier:
                        case UpgradeEffectType.FacilityPowerMultiplier:
                        case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                        case UpgradeEffectType.FacilityGoldMultiplier:
                            return hasFacility || elapsedSeconds >= 180f;
                        case UpgradeEffectType.AddActiveHole:
                            return runState == null || runState.MaxHoleCount <= 0 || runState.ActiveHoleCount < runState.MaxHoleCount;
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
                if (elapsedSeconds < 60f)
                {
                    weight *= 1.28f;
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

            if (def.EffectType == UpgradeEffectType.AddActiveHole)
            {
                if (runState != null && runState.MaxHoleCount > 0 && runState.ActiveHoleCount >= runState.MaxHoleCount)
                {
                    weight *= 0.22f;
                }
                else if (elapsedSeconds <= 120f)
                {
                    weight *= 2.1f;
                }
                else if (elapsedSeconds <= 220f)
                {
                    weight *= 1.45f;
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
            GameContent content,
            List<UpgradeDef> sourcePool,
            List<UpgradeDef> offer,
            RunState runState,
            float elapsedSeconds)
        {
            if (sourcePool == null || offer == null || runState == null || offer.Count == 0)
            {
                return;
            }

            float minWindow = content != null ? Mathf.Clamp(content.AutomationGuaranteeMinSeconds, 10f, 180f) : 35f;
            float maxWindow = content != null ? Mathf.Clamp(content.AutomationGuaranteeMaxSeconds, minWindow, 220f) : 45f;
            float offerWindowMax = maxWindow + 28f;
            if (elapsedSeconds < minWindow || elapsedSeconds > offerWindowMax)
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

        private static List<UpgradeDef> BuildOpeningFocusedPool(List<UpgradeDef> sourcePool, RunState runState, float elapsedSeconds)
        {
            if (sourcePool == null || sourcePool.Count == 0)
            {
                return sourcePool ?? new List<UpgradeDef>();
            }

            if (elapsedSeconds > OpeningFunctionalWindowSeconds)
            {
                return sourcePool;
            }

            int targetCount = Mathf.Min(OpeningPrimaryPoolTarget, sourcePool.Count);
            if (sourcePool.Count <= targetCount)
            {
                return sourcePool;
            }

            List<UpgradeDef> ordered = sourcePool
                .Where(def => def != null && !IsPlaceholderUpgrade(def))
                .OrderByDescending(def =>
                {
                    float score = def.BaseWeight + GetSynergyScore(runState, def);
                    if (IsOpeningFunctionalUpgrade(def))
                    {
                        score += 1.1f;
                    }
                    else if (IsOpeningPureNumericUpgrade(def))
                    {
                        score -= 0.55f;
                    }

                    score *= GetOpeningRouteWeight(runState, def, elapsedSeconds);
                    score += def.Rarity switch
                    {
                        Rarity.Common => 0.4f,
                        Rarity.Rare => 0.55f,
                        Rarity.Epic => 0.25f,
                        _ => 0f,
                    };
                    return score;
                })
                .ThenBy(def => def.UnlockAtSecond)
                .ToList();
            if (ordered.Count < 3)
            {
                return sourcePool;
            }

            List<UpgradeDef> selected = new List<UpgradeDef>(targetCount);
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<UpgradeEffectType, int> effectCounts = new Dictionary<UpgradeEffectType, int>();

            for (int i = 0; i < ordered.Count && selected.Count < targetCount; i++)
            {
                UpgradeDef def = ordered[i];
                if (!IsOpeningFunctionalUpgrade(def))
                {
                    continue;
                }

                TryAddToOpeningPool(selected, selectedIds, effectCounts, def, 3);
            }

            for (int i = 0; i < ordered.Count && selected.Count < targetCount; i++)
            {
                UpgradeDef def = ordered[i];
                if (GetOpeningRouteWeight(runState, def, elapsedSeconds) < 1.12f)
                {
                    continue;
                }

                TryAddToOpeningPool(selected, selectedIds, effectCounts, def, 3);
            }

            for (int i = 0; i < ordered.Count && selected.Count < targetCount; i++)
            {
                UpgradeDef def = ordered[i];
                if (!HasCoreCombatIdentity(def))
                {
                    continue;
                }

                TryAddToOpeningPool(selected, selectedIds, effectCounts, def, 2);
            }

            for (int i = 0; i < ordered.Count && selected.Count < targetCount; i++)
            {
                TryAddToOpeningPool(selected, selectedIds, effectCounts, ordered[i], 2);
            }

            if (selected.Count < targetCount)
            {
                for (int i = 0; i < ordered.Count && selected.Count < targetCount; i++)
                {
                    UpgradeDef def = ordered[i];
                    if (def == null || selectedIds.Contains(def.Id))
                    {
                        continue;
                    }

                    selected.Add(def);
                    selectedIds.Add(def.Id);
                }
            }

            return selected.Count >= 3 ? selected : sourcePool;
        }

        private static void TryAddToOpeningPool(
            List<UpgradeDef> selected,
            HashSet<string> selectedIds,
            Dictionary<UpgradeEffectType, int> effectCounts,
            UpgradeDef def,
            int maxPerEffect)
        {
            if (def == null || selectedIds.Contains(def.Id))
            {
                return;
            }

            int count = effectCounts.TryGetValue(def.EffectType, out int current) ? current : 0;
            if (count >= Mathf.Max(1, maxPerEffect))
            {
                return;
            }

            selected.Add(def);
            selectedIds.Add(def.Id);
            effectCounts[def.EffectType] = count + 1;
        }

        private static void EnsureEarlyFunctionalGuarantee(
            List<UpgradeDef> sourcePool,
            List<UpgradeDef> offer,
            RunState runState,
            float elapsedSeconds)
        {
            if (sourcePool == null || offer == null || offer.Count == 0 || runState == null)
            {
                return;
            }

            if (elapsedSeconds > OpeningFunctionalWindowSeconds || HasAutomationNow(runState))
            {
                return;
            }

            bool hasAutomationOrExpansion = offer.Any(def => def != null && IsAutomationOrExpansionOption(def));
            if (hasAutomationOrExpansion)
            {
                return;
            }

            UpgradeDef candidate = sourcePool
                .Where(def => def != null && !ContainsId(offer, def.Id) && IsAutomationOrExpansionOption(def))
                .OrderByDescending(def => GetOpeningRouteWeight(runState, def, elapsedSeconds))
                .ThenByDescending(def => def.BaseWeight + GetSynergyScore(runState, def))
                .ThenByDescending(def => def.Rarity)
                .FirstOrDefault();
            if (candidate == null)
            {
                return;
            }

            int replaceIndex = -1;
            float weakestScore = float.MaxValue;
            for (int i = 0; i < offer.Count; i++)
            {
                UpgradeDef current = offer[i];
                if (current == null)
                {
                    replaceIndex = i;
                    break;
                }

                float score = GetSynergyScore(runState, current) + (int)current.Rarity * 0.42f;
                if (IsAutomationOrExpansionOption(current))
                {
                    score += 2.8f;
                }

                if (IsOpeningPureNumericUpgrade(current))
                {
                    score -= 1.9f;
                }

                if (score < weakestScore)
                {
                    weakestScore = score;
                    replaceIndex = i;
                }
            }

            if (replaceIndex >= 0)
            {
                offer[replaceIndex] = candidate;
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
                if (IsUsefulOption(runState, offer[i]))
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
                if (candidate.Tags.Contains("Damage") || candidate.EffectType == UpgradeEffectType.AddDamage)
                {
                    score += 4.5f;
                }
                if (candidate.Tags.Contains("Range") || candidate.EffectType == UpgradeEffectType.AddRange || candidate.EffectType == UpgradeEffectType.AddSplash)
                {
                    score += 4f;
                }
                if (candidate.Tags.Contains("Crit") ||
                    candidate.EffectType == UpgradeEffectType.AddCritChance ||
                    candidate.EffectType == UpgradeEffectType.AddCritDamage)
                {
                    score += 3.6f;
                }
                if (score > best)
                {
                    best = score;
                    bestDef = candidate;
                }
            }

            return bestDef;
        }

        private static bool IsUsefulOption(RunState runState, UpgradeDef def)
        {
            if (def == null)
            {
                return false;
            }

            switch (def.EffectType)
            {
                case UpgradeEffectType.AddDamage:
                case UpgradeEffectType.AddRange:
                case UpgradeEffectType.AddSplash:
                case UpgradeEffectType.AddCritChance:
                case UpgradeEffectType.AddCritDamage:
                case UpgradeEffectType.UnlockAutoHammer:
                case UpgradeEffectType.AddDroneCount:
                case UpgradeEffectType.DeployAutoHammerTower:
                case UpgradeEffectType.DeploySensorHammer:
                case UpgradeEffectType.AddActiveHole:
                    return true;
            }

            if (def.Tags.Contains("Damage") || def.Tags.Contains("Range") || def.Tags.Contains("Crit"))
            {
                return true;
            }

            return GetSynergyScore(runState, def) >= 1.2f;
        }

        private static void EnsureBaselineCombatOption(List<UpgradeDef> sourcePool, List<UpgradeDef> offer)
        {
            if (offer == null || offer.Count == 0)
            {
                return;
            }

            if (offer.Any(HasCoreCombatIdentity))
            {
                return;
            }

            UpgradeDef candidate = sourcePool?
                .FirstOrDefault(def => def != null && HasCoreCombatIdentity(def));
            if (candidate != null && !ContainsId(offer, candidate.Id))
            {
                offer[0] = candidate;
                NormalizeCoreTags(offer);
            }

            if (!offer.Any(HasCoreCombatIdentity))
            {
                EnsureTag(offer[0], "Damage");
            }
        }

        private static bool HasCoreCombatIdentity(UpgradeDef def)
        {
            if (def == null)
            {
                return false;
            }

            return def.Tags.Contains("Damage") ||
                   def.Tags.Contains("Range") ||
                   def.Tags.Contains("Crit") ||
                   def.EffectType == UpgradeEffectType.AddDamage ||
                   def.EffectType == UpgradeEffectType.AddRange ||
                   def.EffectType == UpgradeEffectType.AddSplash ||
                   def.EffectType == UpgradeEffectType.AddCritChance ||
                   def.EffectType == UpgradeEffectType.AddCritDamage;
        }

        private static bool HasAutomationNow(RunState runState)
        {
            if (runState == null || runState.Stats == null)
            {
                return false;
            }

            return runState.Stats.AutoHammerInterval > 0f ||
                   runState.Stats.DroneCount > 0 ||
                   runState.ActiveFacilityCount > 0 ||
                   runState.FacilityTriggerCount > 0;
        }

        private static bool IsAutomationOrExpansionOption(UpgradeDef def)
        {
            if (def == null)
            {
                return false;
            }

            return def.IsAutomation ||
                   def.EffectType == UpgradeEffectType.AddActiveHole ||
                   (def.Tags != null && def.Tags.Contains("Expansion"));
        }

        private static bool IsOpeningFunctionalUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return false;
            }

            switch (def.EffectType)
            {
                case UpgradeEffectType.UnlockAutoHammer:
                case UpgradeEffectType.AutoHammerIntervalMultiplier:
                case UpgradeEffectType.UnlockAutoAim:
                case UpgradeEffectType.AddDroneCount:
                case UpgradeEffectType.DeployAutoHammerTower:
                case UpgradeEffectType.DeploySensorHammer:
                case UpgradeEffectType.DeployGoldMagnet:
                case UpgradeEffectType.DeployBountyMarker:
                case UpgradeEffectType.DeployTeslaCoupler:
                case UpgradeEffectType.DeployExecutionPlate:
                case UpgradeEffectType.FacilityCooldownMultiplier:
                case UpgradeEffectType.FacilityPowerMultiplier:
                case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                case UpgradeEffectType.FacilityGoldMultiplier:
                case UpgradeEffectType.AddActiveHole:
                case UpgradeEffectType.AddGoldMultiplier:
                case UpgradeEffectType.AddExpMultiplier:
                case UpgradeEffectType.AddMagnetRadius:
                case UpgradeEffectType.AddMaxDurability:
                    return true;
            }

            if (def.Tags == null)
            {
                return false;
            }

            return def.Tags.Contains("Automation") ||
                   def.Tags.Contains("Facility") ||
                   def.Tags.Contains("Expansion") ||
                   def.Tags.Contains("Economy");
        }

        private static bool IsOpeningPureNumericUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return false;
            }

            switch (def.EffectType)
            {
                case UpgradeEffectType.AddDamage:
                case UpgradeEffectType.AttackIntervalMultiplier:
                case UpgradeEffectType.AddRange:
                case UpgradeEffectType.AddCritChance:
                case UpgradeEffectType.AddCritDamage:
                case UpgradeEffectType.AddSplash:
                case UpgradeEffectType.AddChainCount:
                case UpgradeEffectType.AddBossDamageMultiplier:
                    return true;
                default:
                    return false;
            }
        }

        private static float GetOpeningRouteWeight(RunState runState, UpgradeDef def, float elapsedSeconds)
        {
            if (runState == null || def == null || elapsedSeconds > OpeningFunctionalWindowSeconds)
            {
                return 1f;
            }

            string route = runState.OpeningRoute ?? string.Empty;
            if (string.IsNullOrWhiteSpace(route))
            {
                return 1f;
            }

            bool routeMatch = route switch
            {
                OpeningRouteAutoTower =>
                    def.EffectType == UpgradeEffectType.UnlockAutoHammer ||
                    def.EffectType == UpgradeEffectType.AutoHammerIntervalMultiplier ||
                    def.EffectType == UpgradeEffectType.DeployAutoHammerTower ||
                    def.EffectType == UpgradeEffectType.AddActiveHole ||
                    def.EffectType == UpgradeEffectType.FacilityCooldownMultiplier ||
                    def.EffectType == UpgradeEffectType.FacilityPowerMultiplier ||
                    (def.Tags != null && (def.Tags.Contains("Automation") || def.Tags.Contains("Expansion"))),
                OpeningRouteChainGrid =>
                    def.EffectType == UpgradeEffectType.DeploySensorHammer ||
                    def.EffectType == UpgradeEffectType.DeployTeslaCoupler ||
                    def.EffectType == UpgradeEffectType.AddChainCount ||
                    def.EffectType == UpgradeEffectType.UnlockAutoAim ||
                    (def.Tags != null && (def.Tags.Contains("Chain") || def.Tags.Contains("Shock"))),
                OpeningRouteBountyFactory =>
                    def.EffectType == UpgradeEffectType.DeployBountyMarker ||
                    def.EffectType == UpgradeEffectType.DeployGoldMagnet ||
                    def.EffectType == UpgradeEffectType.AddGoldMultiplier ||
                    def.EffectType == UpgradeEffectType.AddExpMultiplier ||
                    def.EffectType == UpgradeEffectType.AddMagnetRadius ||
                    def.EffectType == UpgradeEffectType.AddActiveHole ||
                    (def.Tags != null && (def.Tags.Contains("Bounty") || def.Tags.Contains("Economy") || def.Tags.Contains("Gold"))),
                _ => false,
            };

            if (routeMatch)
            {
                return IsOpeningFunctionalUpgrade(def) ? 1.55f : 1.25f;
            }

            if (IsOpeningPureNumericUpgrade(def))
            {
                return elapsedSeconds <= 70f ? 0.72f : 0.84f;
            }

            if (def.IsAutomation)
            {
                return 1.2f;
            }

            return 1f;
        }

        private static void NormalizeCoreTags(List<UpgradeDef> offer)
        {
            if (offer == null)
            {
                return;
            }

            for (int i = 0; i < offer.Count; i++)
            {
                UpgradeDef def = offer[i];
                if (def == null)
                {
                    continue;
                }

                switch (def.EffectType)
                {
                    case UpgradeEffectType.AddDamage:
                        EnsureTag(def, "Damage");
                        break;
                    case UpgradeEffectType.AddRange:
                    case UpgradeEffectType.AddSplash:
                        EnsureTag(def, "Range");
                        break;
                    case UpgradeEffectType.AddCritChance:
                    case UpgradeEffectType.AddCritDamage:
                        EnsureTag(def, "Crit");
                        break;
                    case UpgradeEffectType.AddActiveHole:
                        EnsureTag(def, "Expansion");
                        EnsureTag(def, "Automation");
                        break;
                }
            }
        }

        private static void EnsureTag(UpgradeDef def, string tag)
        {
            if (def == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            if (def.Tags == null)
            {
                def.Tags = new List<string>();
            }

            if (!def.Tags.Contains(tag))
            {
                def.Tags.Add(tag);
            }
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

                if (tag == "Expansion")
                {
                    bool canExpand = runState.MaxHoleCount <= 0 || runState.ActiveHoleCount < runState.MaxHoleCount;
                    score += canExpand ? 1.1f : -0.4f;
                    if (runState.ElapsedSeconds <= 140f)
                    {
                        score += 0.7f;
                    }
                }

                if (tag == "Bounty" && runState.FacilityLevels.TryGetValue(FacilityType.BountyMarker, out int bountyLevel))
                {
                    score += 0.3f + bountyLevel * 0.35f;
                }

                if ((tag == "Shock" || tag == "Chain") &&
                    runState.FacilityLevels.TryGetValue(FacilityType.TeslaCoupler, out int teslaLevel))
                {
                    score += 0.22f + teslaLevel * 0.28f;
                }

                if ((tag == "Execute" || tag == "Boss") &&
                    runState.FacilityLevels.TryGetValue(FacilityType.ExecutionPlate, out int executeLevel))
                {
                    score += 0.22f + executeLevel * 0.3f;
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

    public sealed class UpgradeVisualizationService : IUpgradeVisualizationService
    {
        public string BuildOptionText(UpgradeDef def, RunState runState)
        {
            if (def == null)
            {
                return "无效升级";
            }

            string category = string.IsNullOrWhiteSpace(def.Category) ? "通用" : def.Category;
            string desc = UpgradePresentationFormatter.BuildReadableDescription(def, runState);
            string preview = UpgradePresentationFormatter.BuildPreviewLine(def, runState);
            return $"{def.DisplayName}\n<size=24>{desc}</size>\n<size=20>选择后: {preview}</size>\n<size=18>[{category}]</size>";
        }

        public string BuildPreviewLine(UpgradeDef def, RunState runState)
        {
            return UpgradePresentationFormatter.BuildPreviewLine(def, runState);
        }

        public string BuildAppliedDeltaLine(UpgradeDef def, UpgradeStatsSnapshot before, UpgradeStatsSnapshot after, RunState runState)
        {
            return UpgradePresentationFormatter.BuildAppliedDeltaLine(def, before, after, runState);
        }

        public UpgradeVisualDelta BuildVisualDelta(UpgradeDef def, UpgradeStatsSnapshot before, UpgradeStatsSnapshot after, RunState runState)
        {
            return UpgradePresentationFormatter.BuildVisualDelta(def, before, after, runState);
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

            threatMultiplier *= Mathf.Clamp(runState.DifficultyThreatMultiplier, 0.5f, 3f);
            threatMultiplier *= Mathf.Clamp(1f + runState.WaveThreatBonus * 0.35f, 0.45f, 2.4f);

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
                (runState.RogueZoneRemaining > 0f ? 1.25f : 1f) *
                (1f + Mathf.Max(0f, runState.WaveRareBonus) * 0.65f) *
                (1f + Mathf.Max(0f, runState.DifficultyLegendBonus) * 0.45f));
            if (selectedMole == null)
            {
                return false;
            }

            spawnerState.ThreatBudget = Mathf.Max(0f, spawnerState.ThreatBudget - selectedMole.ThreatCost);
            spawnerState.SpawnedCount++;
            float spawnSpeed = Mathf.Max(0.13f, 0.62f - runState.ElapsedSeconds * 0.0005f);
            if (runState.ElapsedSeconds < 120f)
            {
                spawnSpeed += 0.14f;
            }

            if (runState.ElapsedSeconds < 60f)
            {
                spawnSpeed += 0.18f;
            }

            if (runState.ActiveFacilityCount <= 0 && runState.ElapsedSeconds < 180f)
            {
                spawnSpeed += 0.05f;
            }

            spawnSpeed /= Mathf.Clamp(1f + runState.WaveSpeedBonus * 0.28f, 0.55f, 1.8f);

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
                rampThreat *= 0.6f;
            }

            if (elapsedSeconds < 75f)
            {
                rampThreat *= 0.45f;
            }

            float earlyEase = 1f;
            if (elapsedSeconds < 45f)
            {
                earlyEase = 0.72f;
            }
            else if (elapsedSeconds < 90f)
            {
                earlyEase = 0.82f;
            }
            else if (elapsedSeconds < 140f)
            {
                earlyEase = 0.92f;
            }

            float threatPerSecond = (baseThreat + rampThreat) * earlyEase;
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
                case FacilityType.TeslaCoupler:
                {
                    int chainHits = Mathf.Clamp(1 + Mathf.Max(0, facility.Level - 1) / 2 + (overloadActive ? 1 : 0), 1, 4);
                    List<HoleRuntime> targets = holes
                        .Where(h => h.HasLiveMole && Vector2.Distance(h.Position, anchorHole.Position) <= range * 1.25f)
                        .OrderByDescending(h => h.CurrentMole.Def.Rarity)
                        .ThenByDescending(h => h.CurrentMole.Def.GoldReward)
                        .ThenBy(h => h.CurrentMole.RemainingHp)
                        .Take(chainHits)
                        .ToList();
                    if (targets.Count > 0)
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            float strike = i == 0
                                ? damage * 0.92f
                                : damage * Mathf.Clamp(0.62f - i * 0.08f, 0.3f, 0.62f);
                            holeDamageCallback?.Invoke(targets[i], strike, AttackSource.Facility);
                        }

                        triggered = true;
                    }
                    else if (hasBoss)
                    {
                        bossDamageCallback?.Invoke(damage * 0.44f, AttackSource.Facility);
                        triggered = true;
                    }

                    break;
                }
                case FacilityType.ExecutionPlate:
                {
                    HoleRuntime target = holes
                        .Where(h => h.HasLiveMole && Vector2.Distance(h.Position, anchorHole.Position) <= range * 1.15f)
                        .OrderBy(h => h.CurrentMole.RemainingHp)
                        .ThenByDescending(h => h.CurrentMole.Def.Rarity)
                        .FirstOrDefault();
                    if (target != null)
                    {
                        float executeThreshold = Mathf.Max(6f, damage * (0.66f + facility.Level * 0.08f));
                        bool execute = target.CurrentMole.RemainingHp <= executeThreshold;
                        float strike = execute ? damage * 6f : damage * 1.45f;
                        holeDamageCallback?.Invoke(target, strike, AttackSource.Facility);
                        triggered = true;
                    }
                    else if (hasBoss && runState.ElapsedSeconds >= 360f)
                    {
                        bossDamageCallback?.Invoke(damage * 0.58f, AttackSource.Facility);
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
                case FacilityType.TeslaCoupler:
                    rareMultiplier = 1f + (0.12f + Mathf.Max(0f, facility.Level - 1) * 0.05f) * overdrive;
                    break;
                case FacilityType.ExecutionPlate:
                    goldMultiplier = 1f + (0.11f + Mathf.Max(0f, facility.Level - 1) * 0.06f)
                        * runState.FacilityGoldMultiplier
                        * overdrive;
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
                case FacilityType.TeslaCoupler:
                    score += hole.DangerLevel * 0.58f + hole.SpawnWeight * 0.3f;
                    break;
                case FacilityType.ExecutionPlate:
                    score += hole.DangerLevel * 0.7f + hole.RareWeightMultiplier * 0.45f;
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

    public sealed class FtueService : IFtueService
    {
        private readonly List<FtueStepDef> _steps = new List<FtueStepDef>();
        private RunState _runState;
        private bool _enabled;
        private int _nextIndex;
        private float _nextAllowedSecond;

        public void ResetForRun(GameContent content, RunState runState, bool enabled)
        {
            _steps.Clear();
            _runState = runState;
            _nextIndex = 0;
            _nextAllowedSecond = 6f;
            _enabled = enabled;

            if (!_enabled || content?.FtueSteps == null || content.FtueSteps.Count == 0)
            {
                if (_runState != null)
                {
                    _runState.FtueCompleted = true;
                }

                return;
            }

            _steps.AddRange(content.FtueSteps
                .Where(step => step != null && !string.IsNullOrWhiteSpace(step.Description))
                .OrderBy(step => step.Order)
                .ThenBy(step => step.Id, StringComparer.OrdinalIgnoreCase)
                .Take(8));

            if (_steps.Count == 0)
            {
                _enabled = false;
                if (_runState != null)
                {
                    _runState.FtueCompleted = true;
                }
            }
        }

        public void Tick(float elapsedSeconds, Action<string, float, int> messageCallback)
        {
            if (!_enabled || messageCallback == null)
            {
                return;
            }

            if (_nextIndex >= _steps.Count)
            {
                _enabled = false;
                if (_runState != null)
                {
                    _runState.FtueCompleted = true;
                }

                return;
            }

            float triggerSecond = ResolveTriggerSecond(_nextIndex, _steps.Count);
            if (elapsedSeconds < triggerSecond || elapsedSeconds < _nextAllowedSecond)
            {
                return;
            }

            FtueStepDef step = _steps[_nextIndex];
            string text = $"新手引导 {_nextIndex + 1}/{_steps.Count}：{step.Description}";
            float duration = Mathf.Clamp(2.4f + Mathf.Min(40, step.Description.Length) * 0.03f, 2.4f, 4.2f);
            int priority = _nextIndex <= 1 ? 3 : 2;
            messageCallback(text, duration, priority);

            _nextIndex++;
            _nextAllowedSecond = elapsedSeconds + Mathf.Clamp(duration + 3.6f, 6f, 12f);
            if (_runState != null)
            {
                _runState.FtueNextIndex = _nextIndex;
                _runState.FtueShownCount = Mathf.Max(_runState.FtueShownCount, _nextIndex);
                _runState.FtueCompleted = _nextIndex >= _steps.Count;
            }
        }

        private static float ResolveTriggerSecond(int index, int total)
        {
            if (total <= 1)
            {
                return 8f;
            }

            float t = Mathf.Clamp01(index / Mathf.Max(1f, total - 1f));
            return Mathf.Lerp(8f, 120f, t);
        }
    }

    public sealed class EventChoiceService : IEventChoiceService
    {
        public string ResolveChoiceLabel(RunEventDef runEvent, int choiceIndex)
        {
            if (runEvent == null)
            {
                return choiceIndex == 0 ? "方案A" : (choiceIndex == 1 ? "方案B" : "跳过");
            }

            string token = ResolveChoiceToken(runEvent, choiceIndex);
            if (choiceIndex >= 2 || string.Equals(token, "Skip", StringComparison.OrdinalIgnoreCase))
            {
                return "跳过";
            }

            bool risky = choiceIndex == 1 ||
                         token.IndexOf("Risk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         token.IndexOf("B", StringComparison.OrdinalIgnoreCase) >= 0;
            return runEvent.Type switch
            {
                RunEventType.MerchantBoost => risky ? "豪赌采购" : "标准采购",
                RunEventType.TreasureRush => risky ? "超载采掘" : "稳健采掘",
                RunEventType.CurseAltar => risky ? "高压赌注" : "低压祭礼",
                RunEventType.RepairStation => risky ? "深度检修" : "快速检修",
                RunEventType.BountyContract => risky ? "高压合约" : "稳健合约",
                RunEventType.RogueHoleZone => risky ? "全线暴走" : "局部暴走",
                _ => risky ? "冒险方案" : "稳健方案",
            };
        }

        private static string ResolveChoiceToken(RunEventDef runEvent, int choiceIndex)
        {
            if (runEvent?.Choices != null && choiceIndex >= 0 && choiceIndex < runEvent.Choices.Count)
            {
                return runEvent.Choices[choiceIndex] ?? string.Empty;
            }

            return choiceIndex switch
            {
                0 => "ChoiceA",
                1 => "ChoiceB",
                _ => "Skip",
            };
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

    public sealed class ConfigCoverageService : IConfigCoverageService
    {
        public ConfigCoverageSnapshot BuildSnapshot(Dictionary<string, int> allCsvTableRows)
        {
            ConfigCoverageSnapshot snapshot = new ConfigCoverageSnapshot();
            if (allCsvTableRows == null || allCsvTableRows.Count == 0)
            {
                return snapshot;
            }

            HashSet<string> consumedTables = new HashSet<string>(
                ConfigDrivenContentLoader.GetRuntimeConsumedTables(),
                StringComparer.OrdinalIgnoreCase);
            snapshot.TotalTables = allCsvTableRows.Count;

            foreach (KeyValuePair<string, int> pair in allCsvTableRows)
            {
                int rows = Mathf.Max(0, pair.Value);
                snapshot.TotalRows += rows;
                if (consumedTables.Contains(pair.Key))
                {
                    snapshot.ConsumedTables++;
                    snapshot.ConsumedRows += rows;
                    snapshot.ConsumedTableNames.Add(pair.Key);
                }
                else
                {
                    snapshot.UnusedTableNames.Add(pair.Key);
                }
            }

            snapshot.CoverageRate = snapshot.TotalTables > 0
                ? (float)snapshot.ConsumedTables / snapshot.TotalTables
                : 0f;
            snapshot.ConsumedTableNames.Sort(StringComparer.OrdinalIgnoreCase);
            snapshot.UnusedTableNames.Sort(StringComparer.OrdinalIgnoreCase);
            return snapshot;
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

            AddUpgrade(content, "up_hole_unlock_1", "扩洞许可 I", "解锁 1 个洞位，刷新效率提升", Rarity.Common, "Expansion", UpgradeEffectType.AddActiveHole, 1f, 1, 12, 1.28f, false, false, "Expansion", "Automation", "Economy");
            AddUpgrade(content, "up_hole_unlock_2", "扩洞许可 II", "再解锁 1 个洞位，扩展自动化部署空间", Rarity.Rare, "Expansion", UpgradeEffectType.AddActiveHole, 1f, 2, 48, 1.04f, false, false, "Expansion", "Automation", "Economy");

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
            AddUpgrade(content, "up_facility_tesla", "电网耦合器部署", "部署电网设施，强化连锁覆盖", Rarity.Epic, "Facility", UpgradeEffectType.DeployTeslaCoupler, 1f, 2, 205, 0.66f, true, false, "Facility", "Chain", "Shock");
            AddUpgrade(content, "up_facility_execute", "处决压板部署", "部署处决压板，补刀低血目标", Rarity.Epic, "Facility", UpgradeEffectType.DeployExecutionPlate, 1f, 2, 225, 0.62f, true, false, "Facility", "Execute", "Automation");
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

            content.Facilities.Add(Facility(
                "facility_tesla_coupler",
                "电网耦合器",
                "对目标洞区产生连锁电击，协同清理。",
                FacilityType.TeslaCoupler,
                1.28f,
                8.6f,
                2.15f,
                0.14f,
                185,
                19f,
                6.2f,
                "Facility",
                "Chain",
                "Shock"));

            content.Facilities.Add(Facility(
                "facility_execution_plate",
                "处决压板",
                "优先处决低血目标并提高终结效率。",
                FacilityType.ExecutionPlate,
                1.42f,
                9.8f,
                1.95f,
                0.08f,
                205,
                20f,
                6.4f,
                "Facility",
                "Execute",
                "Automation"));
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
            content.Events.Add(Event("event_merchant", "流动商人", "支付金币，随机获得一项立即生效强化。", RunEventType.MerchantBoost, 42f, 999f, 10, 1f));
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
            float value,
            float rewardMult = 1f,
            float riskMult = 0.1f,
            params string[] choices)
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
            def.RewardMult = rewardMult;
            def.RiskMult = riskMult;
            if (choices != null && choices.Length > 0)
            {
                for (int i = 0; i < choices.Length; i++)
                {
                    string token = choices[i];
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        def.Choices.Add(token.Trim());
                    }
                }
            }
            if (def.Choices.Count == 0)
            {
                def.Choices.Add("ChoiceA");
                def.Choices.Add("ChoiceB");
                def.Choices.Add("Skip");
            }
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
