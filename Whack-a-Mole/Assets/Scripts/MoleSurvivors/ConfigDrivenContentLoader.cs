using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MoleSurvivors
{
    public static class ConfigDrivenContentLoader
    {
        private const string ConfigRootEnvVar = "MOLE_FACTORY_CONFIG_ROOT";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private sealed class SkillInfo
        {
            public string Id;
            public float PowerA;
            public float PowerB;
            public int MaxStack;
            public List<string> Tags = new List<string>();
        }

        private sealed class RoleLoadSummary
        {
            public string DefaultCharacterId;
            public string DefaultRoleStartWeaponId;
            public string AutomationCharacterId;
        }

        private sealed class EffectMapping
        {
            public UpgradeEffectType EffectType;
            public float Value;
        }

        private sealed class BossPhaseInfo
        {
            public float ThreatLevel;
            public float HpChunk;
        }

        public static bool TryLoad(out GameContent content, out string sourceSummary)
        {
            return TryLoad(out content, out sourceSummary, null);
        }

        public static bool TryLoad(out GameContent content, out string sourceSummary, IList<string> diagnostics)
        {
            content = null;
            sourceSummary = string.Empty;
            string configRoot = FindConfigRoot();
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                sourceSummary = "Config root not found.";
                diagnostics?.Add(sourceSummary);
                return false;
            }

            string csvRoot = ResolveCsvRoot(configRoot);
            if (string.IsNullOrWhiteSpace(csvRoot) || !Directory.Exists(csvRoot))
            {
                sourceSummary = $"CSV folder missing under: {configRoot}";
                diagnostics?.Add(sourceSummary);
                return false;
            }

            try
            {
                List<Dictionary<string, string>> globalRows = ReadTable(csvRoot, "Global");
                List<Dictionary<string, string>> roleRows = ReadTable(csvRoot, "Roles");
                List<Dictionary<string, string>> weaponRows = ReadTable(csvRoot, "Weapons");
                List<Dictionary<string, string>> weaponEvoRows = ReadTable(csvRoot, "WeaponEvo");
                List<Dictionary<string, string>> weaponLvRows = ReadTable(csvRoot, "WeaponLv");
                List<Dictionary<string, string>> skillRows = ReadTable(csvRoot, "Skills");
                List<Dictionary<string, string>> upgradeRows = ReadTable(csvRoot, "Upgrades");
                List<Dictionary<string, string>> facilityRows = ReadTable(csvRoot, "Facilities");
                List<Dictionary<string, string>> moleRows = ReadTable(csvRoot, "Moles");
                List<Dictionary<string, string>> bossRows = ReadTable(csvRoot, "Bosses");
                List<Dictionary<string, string>> bossPhaseRows = ReadTable(csvRoot, "BossPhases");
                List<Dictionary<string, string>> eventRows = ReadTable(csvRoot, "Events");
                List<Dictionary<string, string>> metaRows = ReadTable(csvRoot, "MetaNodes");
                List<Dictionary<string, string>> achievementRows = ReadTable(csvRoot, "Achievements");
                List<Dictionary<string, string>> stageCurveRows = ReadTable(csvRoot, "StageCurve");
                List<Dictionary<string, string>> buildRows = ReadTable(csvRoot, "BuildArchetypes");

                if (weaponRows.Count == 0 || roleRows.Count == 0 || moleRows.Count == 0)
                {
                    sourceSummary = $"Core tables are empty under {csvRoot}";
                    diagnostics?.Add(sourceSummary);
                    return false;
                }

                content = ScriptableObject.CreateInstance<GameContent>();
                ApplyGlobalSettings(content, globalRows);
                Dictionary<string, SkillInfo> skillLookup = BuildSkillLookup(skillRows);
                Dictionary<string, List<string>> buildTagLookup = BuildArchetypeTagLookup(buildRows);
                Dictionary<string, Dictionary<string, string>> weaponRowLookup = BuildWeaponRowLookup(weaponRows);

                BuildWeapons(content, weaponRows, weaponEvoRows);
                RoleLoadSummary roleSummary = BuildCharacters(content, roleRows);
                BuildMoles(content, moleRows);
                BuildFacilities(content, facilityRows);
                BuildBosses(content, bossRows);
                BuildBossEncounters(content, stageCurveRows, bossPhaseRows);
                BuildUpgrades(content, upgradeRows, skillLookup, buildTagLookup, weaponRowLookup, weaponLvRows);
                BuildMetaNodes(content, metaRows);
                BuildAchievements(content, achievementRows);
                BuildEvents(content, eventRows);
                EnsureStartupUnlocksAndDefaults(content, roleSummary);
                AppendSyntheticUnlockMetaNodes(content);
                BuildCodex(content);

                sourceSummary =
                    $"{csvRoot} | Weapons:{content.Weapons.Count} Roles:{content.Characters.Count} " +
                    $"Moles:{content.Moles.Count} Upgrades:{content.Upgrades.Count}";
                diagnostics?.Add(sourceSummary);
                return true;
            }
            catch (Exception ex)
            {
                sourceSummary = $"Config load failed: {ex.Message}";
                diagnostics?.Add(sourceSummary);
                Debug.LogWarning($"[MoleSurvivors] Config load failed from {csvRoot}\n{ex}");
                content = null;
                return false;
            }
        }

        private static string FindConfigRoot()
        {
            List<string> candidates = new List<string>();
            string env = Environment.GetEnvironmentVariable(ConfigRootEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                candidates.Add(env.Trim());
            }

            string projectRoot = null;
            try
            {
                projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            }
            catch
            {
                projectRoot = null;
            }

            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                candidates.Add(Path.Combine(projectRoot, "Config", "FactoryConfig_v5.2"));
                candidates.Add(Path.Combine(projectRoot, "Config", "地鼠工厂_配置导出版_v5.2_csv_json"));
                candidates.Add(Path.Combine(projectRoot, "Config"));
            }

            candidates.Add("/Users/shiyuqian/Downloads/地鼠工厂_配置导出版_v5.2_csv_json");

            for (int i = 0; i < candidates.Count; i++)
            {
                string root = candidates[i];
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string csv = ResolveCsvRoot(root);
                if (!string.IsNullOrWhiteSpace(csv) && Directory.Exists(csv))
                {
                    return root;
                }
            }

            return string.Empty;
        }

        private static string ResolveCsvRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            if (!Directory.Exists(root))
            {
                return string.Empty;
            }

            if (string.Equals(Path.GetFileName(root), "csv", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            string nestedCsv = Path.Combine(root, "csv");
            if (Directory.Exists(nestedCsv))
            {
                return nestedCsv;
            }

            string directGlobal = Path.Combine(root, "Global.csv");
            return File.Exists(directGlobal) ? root : string.Empty;
        }

        private static List<Dictionary<string, string>> ReadTable(string csvRoot, string tableName)
        {
            string path = Path.Combine(csvRoot, $"{tableName}.csv");
            if (!File.Exists(path))
            {
                return new List<Dictionary<string, string>>();
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return new List<Dictionary<string, string>>();
            }

            List<string> headers = ParseCsvLine(lines[0]);
            if (headers.Count == 0)
            {
                return new List<Dictionary<string, string>>();
            }

            headers[0] = TrimBom(headers[0]);
            for (int i = 0; i < headers.Count; i++)
            {
                headers[i] = headers[i].Trim();
            }

            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<string> cells = ParseCsvLine(line);
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                {
                    string value = i < cells.Count ? cells[i] : string.Empty;
                    row[headers[i]] = value?.Trim() ?? string.Empty;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> values = new List<string>();
            if (line == null)
            {
                return values;
            }

            StringBuilder current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        bool escapedQuote = i + 1 < line.Length && line[i + 1] == '"';
                        if (escapedQuote)
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == ',')
                {
                    values.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                current.Append(ch);
            }

            values.Add(current.ToString());
            return values;
        }

        private static void ApplyGlobalSettings(GameContent content, List<Dictionary<string, string>> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                string key = GetString(row, "Name");
                float value = ParseFloat(GetString(row, "Value"), 0f);
                switch (key)
                {
                    case "BattleDurationSec":
                        content.RunDurationSeconds = Mathf.Max(120f, value);
                        break;
                    case "StartingGold":
                        content.StartingGold = Mathf.Max(0, Mathf.RoundToInt(value));
                        break;
                    case "StartingXP":
                        content.StartingExperience = Mathf.Max(0, Mathf.RoundToInt(value));
                        break;
                    case "StartingLife":
                        content.StartingDurability = Mathf.Max(1, Mathf.RoundToInt(value));
                        break;
                    case "StartingReroll":
                        content.StartingEventTickets = Mathf.Max(0, Mathf.RoundToInt(value));
                        break;
                    case "ComboWindowSec":
                        content.ComboWindowSeconds = Mathf.Clamp(value, 0.8f, 6f);
                        break;
                    case "LegendWarnSec":
                        content.LegendWarningSeconds = Mathf.Clamp(value, 0.4f, 4f);
                        break;
                    case "BossWarningSec":
                        content.BossWarningSeconds = Mathf.Clamp(value, 0.6f, 8f);
                        break;
                    case "AutoPickupRange":
                        content.AutoPickupRange = Mathf.Clamp(value, 0f, 6f);
                        break;
                    case "HitStopSec":
                        content.HitStopSeconds = Mathf.Clamp(value, 0f, 0.2f);
                        break;
                    case "CritHitStopSec":
                        content.CritHitStopSeconds = Mathf.Clamp(value, 0f, 0.3f);
                        break;
                    case "BossHitStopSec":
                        content.BossHitStopSeconds = Mathf.Clamp(value, 0f, 0.3f);
                        break;
                    case "CameraShakeSec":
                        content.CameraShakeSeconds = Mathf.Clamp(value, 0f, 1f);
                        break;
                    case "CameraShakeAmp":
                        content.CameraShakeAmplitude = Mathf.Clamp(value, 0f, 0.5f);
                        break;
                    case "CameraShakeFreq":
                        content.CameraShakeFrequency = Mathf.Clamp(value, 1f, 120f);
                        break;
                    case "RareHintCooldownSec":
                        content.RareHintCooldownSeconds = Mathf.Clamp(value, 0.1f, 10f);
                        break;
                    case "MidBossWarningLeadSec":
                        content.MidBossWarningLeadSeconds = Mathf.Clamp(value, 1f, 60f);
                        break;
                    case "FinalBossWarningLeadSec":
                        content.FinalBossWarningLeadSeconds = Mathf.Clamp(value, 1f, 90f);
                        break;
                }
            }

            content.InitialEventUnlockSeconds = Mathf.Clamp(content.InitialEventUnlockSeconds, 10f, 120f);
            content.InitialEventCooldownSeconds = Mathf.Clamp(content.InitialEventCooldownSeconds, 12f, 150f);
            content.EventRetryCooldownSeconds = Mathf.Clamp(content.EventRetryCooldownSeconds, 8f, 120f);
            content.ComboMissWindowSeconds = Mathf.Min(content.ComboWindowSeconds, content.ComboMissWindowSeconds);
            content.ComboDecayTickSeconds = Mathf.Min(content.ComboWindowSeconds, content.ComboDecayTickSeconds);
            content.BossGraceSeconds = Mathf.Max(10f, content.BossGraceSeconds);
            content.CritHitStopSeconds = Mathf.Max(content.HitStopSeconds, content.CritHitStopSeconds);
            content.BossHitStopSeconds = Mathf.Max(content.HitStopSeconds, content.BossHitStopSeconds);
        }

        private static Dictionary<string, SkillInfo> BuildSkillLookup(List<Dictionary<string, string>> rows)
        {
            Dictionary<string, SkillInfo> lookup = new Dictionary<string, SkillInfo>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                string id = GetString(row, "SkillID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                SkillInfo info = new SkillInfo
                {
                    Id = id,
                    PowerA = ParseFloat(GetString(row, "PowerA"), 0f),
                    PowerB = ParseFloat(GetString(row, "PowerB"), 0f),
                    MaxStack = Mathf.Max(1, ParseInt(GetString(row, "MaxStack"), 1)),
                };

                List<string> rawTags = SplitMulti(GetString(row, "Tags"));
                for (int tagIndex = 0; tagIndex < rawTags.Count; tagIndex++)
                {
                    string normalized = NormalizeTag(rawTags[tagIndex]);
                    if (!string.IsNullOrWhiteSpace(normalized) && !info.Tags.Contains(normalized))
                    {
                        info.Tags.Add(normalized);
                    }
                }

                lookup[id] = info;
            }

            return lookup;
        }

        private static Dictionary<string, List<string>> BuildArchetypeTagLookup(List<Dictionary<string, string>> rows)
        {
            Dictionary<string, List<string>> lookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                string id = GetString(row, "BuildID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                List<string> tags = SplitMulti(GetString(row, "CoreTags"))
                    .Select(NormalizeTag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                lookup[id] = tags;
            }

            return lookup;
        }

        private static Dictionary<string, Dictionary<string, string>> BuildWeaponRowLookup(List<Dictionary<string, string>> rows)
        {
            Dictionary<string, Dictionary<string, string>> lookup = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                string id = GetString(row, "WeaponID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                lookup[id] = row;
            }

            return lookup;
        }

        private static void BuildWeapons(
            GameContent content,
            List<Dictionary<string, string>> weaponRows,
            List<Dictionary<string, string>> weaponEvoRows)
        {
            Dictionary<string, List<Dictionary<string, string>>> evoLookup = weaponEvoRows
                .GroupBy(row => GetString(row, "BaseWeaponID"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < weaponRows.Count; i++)
            {
                Dictionary<string, string> row = weaponRows[i];
                string id = GetString(row, "WeaponID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                bool isEvolution = ParseBool(GetString(row, "IsEvolution"));
                string tier = GetString(row, "Tier");
                if (isEvolution || string.Equals(tier, "Facility", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                WeaponDef weapon = ScriptableObject.CreateInstance<WeaponDef>();
                weapon.Id = id;
                weapon.DisplayName = GetString(row, "Name", id);
                weapon.Description = GetString(row, "Description", weapon.DisplayName);
                weapon.Damage = Mathf.Max(1f, ParseFloat(GetString(row, "BaseDamage"), 10f));
                weapon.AttackInterval = Mathf.Clamp(ParseFloat(GetString(row, "BaseCD"), 0.5f), 0.1f, 3.5f);
                weapon.AttackRadius = Mathf.Clamp(ParseFloat(GetString(row, "BaseRadius"), 0.35f), 0.12f, 1.8f);
                weapon.CritChance = Mathf.Clamp01(ParseFloat(GetString(row, "Crit"), 0.05f));
                weapon.CritDamage = 1.45f + weapon.CritChance * 1.9f;

                string targetRule = GetString(row, "TargetRule");
                List<string> tags = SplitMulti(GetString(row, "Tags"))
                    .Select(NormalizeTag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .ToList();

                weapon.AutoAim = string.Equals(targetRule, "Auto", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(targetRule, "Orbit", StringComparison.OrdinalIgnoreCase);
                weapon.ChainCount = tags.Contains("Chain") || string.Equals(targetRule, "Chain", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
                bool splashLike = string.Equals(targetRule, "Splash", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(targetRule, "Arc", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(targetRule, "Wave", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(targetRule, "Impact", StringComparison.OrdinalIgnoreCase);
                weapon.SplashRadius = splashLike ? Mathf.Max(0.16f, weapon.AttackRadius * 0.75f) : 0f;
                bool droneLike = tags.Contains("Drone") || string.Equals(GetString(row, "Type"), "Drone", StringComparison.OrdinalIgnoreCase);
                weapon.DroneCount = droneLike ? 1 : 0;
                weapon.AutoHammerInterval = droneLike
                    ? Mathf.Clamp(weapon.AttackInterval * 1.9f, 0.3f, 2.6f)
                    : 0f;

                if (evoLookup.TryGetValue(id, out List<Dictionary<string, string>> evoRows) && evoRows.Count > 0)
                {
                    Dictionary<string, string> selectedEvo = evoRows
                        .FirstOrDefault(entry => ParseBool(GetString(entry, "DefaultEnabled"), true)) ?? evoRows[0];
                    weapon.EvolutionId = GetString(selectedEvo, "EvoID");
                    List<string> needs = SplitMulti(GetString(selectedEvo, "NeedStats"));
                    for (int needIndex = 0; needIndex < needs.Count; needIndex++)
                    {
                        if (!TryParseTagRequirement(needs[needIndex], out TagRequirement requirement))
                        {
                            continue;
                        }

                        weapon.EvolutionRequirements.Add(requirement);
                    }
                }

                content.Weapons.Add(weapon);
                if (string.Equals(tier, "Starter", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(content.StartupUnlockedWeaponIds, weapon.Id);
                }
            }
        }

        private static RoleLoadSummary BuildCharacters(GameContent content, List<Dictionary<string, string>> roleRows)
        {
            RoleLoadSummary summary = new RoleLoadSummary();
            for (int i = 0; i < roleRows.Count; i++)
            {
                Dictionary<string, string> row = roleRows[i];
                string id = GetString(row, "RoleID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                CharacterDef def = ScriptableObject.CreateInstance<CharacterDef>();
                def.Id = id;
                def.DisplayName = GetString(row, "Name", id);
                def.Description = GetString(row, "Description", def.DisplayName);

                string roleType = GetString(row, "RoleType");
                string unlockReq = GetString(row, "UnlockReq");
                float baseHp = ParseFloat(GetString(row, "BaseHP"), 5f);
                float baseMove = ParseFloat(GetString(row, "BaseMove"), 1f);
                float baseGoldPct = ParseFloat(GetString(row, "BaseGoldPct"), 0f);
                float baseXpPct = ParseFloat(GetString(row, "BaseXPpct"), 0f);
                List<string> tags = SplitMulti(GetString(row, "CoreTags"))
                    .Select(NormalizeTag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .ToList();

                float damageMult = 1f + (baseHp - 5f) * 0.025f;
                if (tags.Contains("Damage"))
                {
                    damageMult += 0.08f;
                }
                if (tags.Contains("Gold"))
                {
                    damageMult += baseGoldPct * 0.25f;
                }

                def.DamageMultiplier = Mathf.Clamp(damageMult, 0.85f, 1.45f);

                float rangeBonus = 0.02f + Mathf.Max(0f, baseMove - 1f) * 0.03f;
                if (tags.Contains("Range") || tags.Contains("AOE"))
                {
                    rangeBonus += 0.04f;
                }

                def.RangeBonus = Mathf.Clamp(rangeBonus, 0f, 0.16f);

                float automation = 1f;
                if (string.Equals(roleType, "Automation", StringComparison.OrdinalIgnoreCase) ||
                    tags.Contains("Automation") ||
                    tags.Contains("Facility") ||
                    tags.Contains("Drone"))
                {
                    automation += 0.18f;
                }

                automation += baseXpPct * 0.45f;
                def.AutomationMultiplier = Mathf.Clamp(automation, 0.8f, 1.6f);
                content.Characters.Add(def);

                bool defaultUnlocked = string.IsNullOrWhiteSpace(unlockReq) ||
                                       string.Equals(unlockReq, "Default", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(roleType, "Starter", StringComparison.OrdinalIgnoreCase);
                if (defaultUnlocked)
                {
                    AddUnique(content.StartupUnlockedCharacterIds, def.Id);
                    if (string.IsNullOrWhiteSpace(summary.DefaultCharacterId))
                    {
                        summary.DefaultCharacterId = def.Id;
                        summary.DefaultRoleStartWeaponId = GetString(row, "StartWeapon");
                    }
                }

                if (string.IsNullOrWhiteSpace(summary.AutomationCharacterId) &&
                    (string.Equals(roleType, "Automation", StringComparison.OrdinalIgnoreCase) || tags.Contains("Automation")))
                {
                    summary.AutomationCharacterId = def.Id;
                }
            }

            return summary;
        }

        private static void BuildMoles(GameContent content, List<Dictionary<string, string>> moleRows)
        {
            int total = Mathf.Max(1, moleRows.Count);
            for (int i = 0; i < moleRows.Count; i++)
            {
                Dictionary<string, string> row = moleRows[i];
                string id = GetString(row, "MoleID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                MoleDef def = ScriptableObject.CreateInstance<MoleDef>();
                def.Id = id;
                def.DisplayName = GetString(row, "Name", id);
                def.Rarity = ParseRarity(GetString(row, "Rarity"));
                def.Traits = ParseMoleTrait(GetString(row, "Trait"), ParseBool(GetString(row, "RareTarget")));

                float hp = ParseFloat(GetString(row, "HP"), 10f);
                float shield = ParseFloat(GetString(row, "ShieldHP"), 0f);
                float armor = ParseFloat(GetString(row, "ArmorPct"), 0f);
                float stay = ParseFloat(GetString(row, "StaySec"), 1f);
                float threat = ParseFloat(GetString(row, "ThreatCost"), 1f);

                def.BaseHp = Mathf.Max(1f, hp + shield * 0.35f + hp * armor * 0.42f);
                def.WarningSeconds = Mathf.Clamp(stay * 0.22f, 0.12f, 0.42f);
                def.UpSeconds = Mathf.Clamp(stay * 0.72f, 0.35f, 2.2f);
                def.CooldownSeconds = Mathf.Clamp(0.38f + stay * 0.28f, 0.32f, 1.6f);
                def.GoldReward = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(GetString(row, "BaseGold"), 1f)));
                def.ExpReward = Mathf.Max(1, Mathf.RoundToInt(def.GoldReward * 0.72f + threat * 1.3f));
                def.CoreReward = ParseBool(GetString(row, "RareTarget"))
                    ? Mathf.Max(1, (int)def.Rarity)
                    : Mathf.Max(0, def.Rarity >= Rarity.Epic ? 1 : 0);
                def.ThreatCost = Mathf.Max(0.6f, threat);

                float progress = total <= 1 ? 0f : (float)i / (total - 1);
                float minByProgress = Mathf.Lerp(0f, content.RunDurationSeconds * 0.72f, progress);
                if (def.Rarity >= Rarity.Epic)
                {
                    minByProgress = Mathf.Max(minByProgress, content.RunDurationSeconds * 0.24f);
                }
                if (def.Rarity == Rarity.Legendary)
                {
                    minByProgress = Mathf.Max(minByProgress, content.RunDurationSeconds * 0.46f);
                }

                string spawnPool = GetString(row, "SpawnPool");
                if (spawnPool.IndexOf("END", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    minByProgress = Mathf.Max(minByProgress, content.RunDurationSeconds * 0.63f);
                }

                def.MinTime = minByProgress;
                def.MaxTime = content.RunDurationSeconds + content.BossGraceSeconds;

                float rarityDivisor = 1f + (int)def.Rarity * 0.55f;
                float rarityTargetPenalty = ParseBool(GetString(row, "RareTarget")) ? 0.72f : 1f;
                def.SpawnWeight = Mathf.Clamp(
                    Mathf.Lerp(8.8f, 1.3f, progress) * rarityTargetPenalty / rarityDivisor,
                    0.25f,
                    10f);
                def.TintColor = ColorFromKey($"{GetString(row, "Trait")}_{GetString(row, "Rarity")}", 0.6f, 0.95f);
                content.Moles.Add(def);
            }
        }

        private static void BuildFacilities(GameContent content, List<Dictionary<string, string>> facilityRows)
        {
            Dictionary<FacilityType, List<Dictionary<string, string>>> grouped =
                new Dictionary<FacilityType, List<Dictionary<string, string>>>();
            for (int i = 0; i < facilityRows.Count; i++)
            {
                Dictionary<string, string> row = facilityRows[i];
                FacilityType type = MapFacilityType(row);
                if (!grouped.TryGetValue(type, out List<Dictionary<string, string>> list))
                {
                    list = new List<Dictionary<string, string>>();
                    grouped[type] = list;
                }

                list.Add(row);
            }

            FacilityType[] wantedTypes =
            {
                FacilityType.AutoHammerTower,
                FacilityType.SensorHammer,
                FacilityType.GoldMagnet,
                FacilityType.BountyMarker,
            };

            for (int index = 0; index < wantedTypes.Length; index++)
            {
                FacilityType type = wantedTypes[index];
                Dictionary<string, string> selected = null;
                if (grouped.TryGetValue(type, out List<Dictionary<string, string>> rows) && rows.Count > 0)
                {
                    selected = rows
                        .OrderByDescending(CalcFacilityPriority)
                        .FirstOrDefault();
                }

                if (selected == null && facilityRows.Count > 0)
                {
                    selected = facilityRows[Mathf.Clamp(index, 0, facilityRows.Count - 1)];
                }

                if (selected == null)
                {
                    continue;
                }

                FacilityDef def = ScriptableObject.CreateInstance<FacilityDef>();
                def.Id = GetString(selected, "FacilityID", type.ToString());
                def.DisplayName = GetString(selected, "Name", type.ToString());
                def.Description = GetString(selected, "Description", def.DisplayName);
                def.Type = type;
                def.BaseInterval = Mathf.Clamp(ParseFloat(GetString(selected, "IntervalSec"), 1f), 0.1f, 4f);
                def.BasePower = Mathf.Max(2f, ParseFloat(GetString(selected, "PowerA"), 0.2f) * 36f);
                def.BaseRadius = Mathf.Max(0.8f, ParseFloat(GetString(selected, "Radius"), 3f) * 0.34f);
                string trigger = GetString(selected, "Trigger");
                def.TriggerDelay = string.Equals(trigger, "OnHit", StringComparison.OrdinalIgnoreCase)
                    ? 0.18f
                    : (string.Equals(trigger, "Aura", StringComparison.OrdinalIgnoreCase) ? 0.04f : 0.09f);
                int rarityRank = (int)ParseRarity(GetString(selected, "Rarity"));
                def.UnlockAtSecond = 95 + rarityRank * 40;
                def.OverloadThreshold = 14f + ParseFloat(GetString(selected, "RiskBonus"), 0f) * 40f + rarityRank * 3f;
                def.OverloadDuration = 5f + rarityRank * 0.65f;
                AddUpgradeTag(def.Tags, "Facility");
                AddUpgradeTag(def.Tags, NormalizeTag(GetString(selected, "Theme")));
                AddUpgradeTag(def.Tags, NormalizeTag(GetString(selected, "Trigger")));
                content.Facilities.Add(def);
            }
        }

        private static void BuildBosses(GameContent content, List<Dictionary<string, string>> bossRows)
        {
            for (int i = 0; i < bossRows.Count; i++)
            {
                Dictionary<string, string> row = bossRows[i];
                string id = GetString(row, "BossID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                Rarity rarity = ParseRarity(GetString(row, "Rarity"));
                float hp = Mathf.Max(120f, ParseFloat(GetString(row, "HP"), 800f));
                string theme = GetString(row, "Theme");

                BossDef boss = ScriptableObject.CreateInstance<BossDef>();
                boss.Id = id;
                boss.DisplayName = GetString(row, "Name", id);
                boss.Hp = hp;
                boss.AttackInterval = Mathf.Clamp(2.9f - (int)rarity * 0.25f, 1.1f, 3.8f);
                boss.DurabilityDamage = theme.IndexOf("Execute", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                boss.RewardGold = Mathf.Max(80, Mathf.RoundToInt(hp * 0.2f));
                boss.RewardCore = Mathf.Clamp(Mathf.RoundToInt(hp / 90f), 4, 36);
                boss.TintColor = ColorFromKey(theme, 0.62f, 0.9f);
                content.Bosses.Add(boss);
            }
        }

        private static void BuildBossEncounters(
            GameContent content,
            List<Dictionary<string, string>> stageCurveRows,
            List<Dictionary<string, string>> bossPhaseRows)
        {
            Dictionary<string, List<BossPhaseInfo>> phaseLookup = new Dictionary<string, List<BossPhaseInfo>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bossPhaseRows.Count; i++)
            {
                Dictionary<string, string> row = bossPhaseRows[i];
                string bossId = GetString(row, "BossID");
                if (string.IsNullOrWhiteSpace(bossId))
                {
                    continue;
                }

                if (!phaseLookup.TryGetValue(bossId, out List<BossPhaseInfo> list))
                {
                    list = new List<BossPhaseInfo>();
                    phaseLookup[bossId] = list;
                }

                list.Add(new BossPhaseInfo
                {
                    ThreatLevel = ParseFloat(GetString(row, "ThreatLevel"), 2f),
                    HpChunk = ParseFloat(GetString(row, "HPChunk"), 0f),
                });
            }

            List<Dictionary<string, string>> selectedMapRows = stageCurveRows
                .Where(row => string.Equals(GetString(row, "MapID"), "MAP_FACTORY", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selectedMapRows.Count == 0 && stageCurveRows.Count > 0)
            {
                string firstMap = GetString(stageCurveRows[0], "MapID");
                selectedMapRows = stageCurveRows
                    .Where(row => string.Equals(GetString(row, "MapID"), firstMap, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            List<float> bossTimes = selectedMapRows
                .Where(row => ParseBool(GetString(row, "BossFlag")))
                .Select(row => ParseFloat(GetString(row, "TimeSec"), 0f))
                .Where(time => time > 0f && time <= content.RunDurationSeconds + 2f)
                .Distinct()
                .OrderBy(time => time)
                .ToList();

            if (bossTimes.Count == 0)
            {
                bossTimes.Add(content.RunDurationSeconds * 0.5f);
                bossTimes.Add(content.RunDurationSeconds);
            }
            else if (bossTimes.Count == 1)
            {
                bossTimes.Insert(0, content.RunDurationSeconds * 0.5f);
            }

            for (int i = 0; i < bossTimes.Count && content.Bosses.Count > 0; i++)
            {
                BossDef boss = content.Bosses[Mathf.Clamp(i, 0, content.Bosses.Count - 1)];
                List<BossPhaseInfo> phases = phaseLookup.TryGetValue(boss.Id, out List<BossPhaseInfo> list)
                    ? list
                    : null;
                float avgThreat = phases != null && phases.Count > 0
                    ? phases.Average(phase => phase.ThreatLevel)
                    : 2.3f;
                float hpPhaseSum = phases != null && phases.Count > 0
                    ? phases.Sum(phase => phase.HpChunk)
                    : boss.Hp;

                BossEncounterDef encounter = ScriptableObject.CreateInstance<BossEncounterDef>();
                encounter.Id = $"encounter_{Mathf.RoundToInt(bossTimes[i]):000}_{boss.Id}";
                encounter.DisplayName = boss.DisplayName;
                encounter.BossId = boss.Id;
                encounter.SpawnAtSecond = bossTimes[i];
                encounter.IsFinalBoss = i == bossTimes.Count - 1;
                encounter.HpMultiplier = Mathf.Clamp(hpPhaseSum / Mathf.Max(1f, boss.Hp), 0.7f, 1.8f);
                encounter.SpawnScale = encounter.IsFinalBoss ? 0.55f : 0.62f;
                encounter.ShieldCycleSeconds = Mathf.Clamp(10.6f - avgThreat * 0.88f, 4.8f, 12f);
                encounter.ShieldDuration = Mathf.Clamp(2f + avgThreat * 0.56f, 1.5f, 5.4f);
                encounter.RogueHoleInterval = Mathf.Clamp(8.8f - avgThreat * 0.72f, 4.2f, 9.2f);
                encounter.RogueHoleDuration = Mathf.Clamp(3.1f + avgThreat * 0.42f, 2f, 6.6f);
                encounter.RogueHoleCount = Mathf.Clamp(Mathf.RoundToInt(2.2f + avgThreat), 2, 6);
                content.BossEncounters.Add(encounter);
            }
        }

        private static void BuildUpgrades(
            GameContent content,
            List<Dictionary<string, string>> upgradeRows,
            Dictionary<string, SkillInfo> skillLookup,
            Dictionary<string, List<string>> buildTagLookup,
            Dictionary<string, Dictionary<string, string>> weaponRowLookup,
            List<Dictionary<string, string>> weaponLvRows)
        {
            HashSet<string> validWeaponLvIds = new HashSet<string>(
                weaponLvRows.Select(row => GetString(row, "WeaponID"))
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < upgradeRows.Count; i++)
            {
                Dictionary<string, string> row = upgradeRows[i];
                string id = GetString(row, "UpgradeID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string pool = GetString(row, "Pool", "All");
                if (!IsRuntimeUpgradePool(pool))
                {
                    continue;
                }

                string grantType = GetString(row, "GrantType");
                string refId = GetString(row, "RefID");
                string category = GetString(row, "Category");
                Rarity rarity = ParseRarity(GetString(row, "Rarity"));
                SkillInfo skill = skillLookup.TryGetValue(refId, out SkillInfo found) ? found : null;
                int rank = (int)rarity;
                int serial = ExtractTrailingNumber(id);

                HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> needTags = SplitMulti(GetString(row, "NeedTags"));
                for (int tagIndex = 0; tagIndex < needTags.Count; tagIndex++)
                {
                    AddUpgradeTag(tags, NormalizeTag(needTags[tagIndex]));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    AddUpgradeTag(tags, NormalizeTag(category));
                }

                if (skill != null)
                {
                    for (int tagIndex = 0; tagIndex < skill.Tags.Count; tagIndex++)
                    {
                        AddUpgradeTag(tags, NormalizeTag(skill.Tags[tagIndex]));
                    }
                }

                if (string.Equals(grantType, "GrantTag", StringComparison.OrdinalIgnoreCase) &&
                    buildTagLookup.TryGetValue(refId, out List<string> buildTags))
                {
                    for (int tagIndex = 0; tagIndex < buildTags.Count; tagIndex++)
                    {
                        AddUpgradeTag(tags, buildTags[tagIndex]);
                    }
                }

                if (string.Equals(grantType, "WeaponLv+", StringComparison.OrdinalIgnoreCase) &&
                    weaponRowLookup.TryGetValue(refId, out Dictionary<string, string> weaponRow))
                {
                    List<string> weaponTags = SplitMulti(GetString(weaponRow, "Tags"));
                    for (int tagIndex = 0; tagIndex < weaponTags.Count; tagIndex++)
                    {
                        AddUpgradeTag(tags, NormalizeTag(weaponTags[tagIndex]));
                    }
                }

                EffectMapping mapping = ResolveUpgradeEffect(grantType, refId, category, tags, skill, rank, serial, validWeaponLvIds);
                UpgradeDef def = ScriptableObject.CreateInstance<UpgradeDef>();
                def.Id = id;
                def.DisplayName = GetString(row, "Name", id);
                def.Description = GetString(row, "Description", def.DisplayName);
                def.Rarity = rarity;
                def.Category = category;
                def.EffectType = mapping.EffectType;
                def.Value = mapping.Value;
                def.MaxStacks = ResolveUpgradeMaxStacks(mapping.EffectType, skill, serial);
                def.UnlockAtSecond = ResolveUpgradeUnlockSecond(content, row);
                def.BaseWeight = ResolveUpgradeWeight(row, rank);
                def.IsAutomation =
                    tags.Contains("Automation") ||
                    tags.Contains("Facility") ||
                    mapping.EffectType == UpgradeEffectType.UnlockAutoHammer ||
                    mapping.EffectType == UpgradeEffectType.AutoHammerIntervalMultiplier ||
                    mapping.EffectType == UpgradeEffectType.DeployAutoHammerTower ||
                    mapping.EffectType == UpgradeEffectType.DeploySensorHammer ||
                    mapping.EffectType == UpgradeEffectType.DeployGoldMagnet ||
                    mapping.EffectType == UpgradeEffectType.DeployBountyMarker ||
                    mapping.EffectType == UpgradeEffectType.FacilityCooldownMultiplier ||
                    mapping.EffectType == UpgradeEffectType.FacilityPowerMultiplier ||
                    mapping.EffectType == UpgradeEffectType.FacilityOverloadThresholdMultiplier ||
                    mapping.EffectType == UpgradeEffectType.FacilityGoldMultiplier;
                def.IsLegendary = def.Rarity == Rarity.Legendary || string.Equals(grantType, "GrantTag", StringComparison.OrdinalIgnoreCase);

                foreach (string tag in tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))
                {
                    def.Tags.Add(tag);
                }

                content.Upgrades.Add(def);
            }
        }

        private static bool IsRuntimeUpgradePool(string pool)
        {
            if (string.IsNullOrWhiteSpace(pool))
            {
                return true;
            }

            string normalized = pool.Trim();
            return string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Runtime", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Release", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Live", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Prod", StringComparison.OrdinalIgnoreCase);
        }

        private static void BuildMetaNodes(GameContent content, List<Dictionary<string, string>> metaRows)
        {
            for (int i = 0; i < metaRows.Count; i++)
            {
                Dictionary<string, string> row = metaRows[i];
                string id = GetString(row, "MetaID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string category = GetString(row, "Category");
                MetaEffectType effectType = ResolveMetaEffectType(category);
                float baseValue = ParseFloat(GetString(row, "BaseValue"), 0f);
                float perLevelGain = ParseFloat(GetString(row, "PerLvGain"), 0f);
                float effectiveValue = baseValue + perLevelGain * 0.5f;

                MetaNodeDef node = ScriptableObject.CreateInstance<MetaNodeDef>();
                node.Id = id;
                node.DisplayName = GetString(row, "Name", id);
                node.Description = GetString(row, "Affects", GetString(row, "Name", id));
                node.EffectType = effectType;
                node.Value = ResolveMetaValue(effectType, effectiveValue);
                node.Cost = Mathf.Max(8, Mathf.RoundToInt(ParseFloat(GetString(row, "BasePrice"), 20f)));
                node.MaxLevel = Mathf.Max(1, ParseInt(GetString(row, "MaxLv"), 1));
                node.TargetId = string.Empty;

                List<string> requires = SplitMulti(GetString(row, "UnlockReq"));
                for (int requireIndex = 0; requireIndex < requires.Count; requireIndex++)
                {
                    string requireId = requires[requireIndex].Trim();
                    if (string.IsNullOrWhiteSpace(requireId))
                    {
                        continue;
                    }

                    node.Requires.Add(requireId);
                }

                content.MetaNodes.Add(node);
            }
        }

        private static void AppendSyntheticUnlockMetaNodes(GameContent content)
        {
            if (!string.IsNullOrWhiteSpace(content.SecondaryWeaponUnlockId))
            {
                MetaNodeDef node = ScriptableObject.CreateInstance<MetaNodeDef>();
                node.Id = $"META_UNLOCK_{content.SecondaryWeaponUnlockId}";
                node.DisplayName = $"解锁武器 {GetWeaponName(content, content.SecondaryWeaponUnlockId)}";
                node.Description = "永久解锁额外武器";
                node.EffectType = MetaEffectType.UnlockLightningWeapon;
                node.TargetId = content.SecondaryWeaponUnlockId;
                node.Value = 0f;
                node.Cost = 120;
                node.MaxLevel = 1;
                content.MetaNodes.Add(node);
            }

            if (!string.IsNullOrWhiteSpace(content.TertiaryWeaponUnlockId))
            {
                MetaNodeDef node = ScriptableObject.CreateInstance<MetaNodeDef>();
                node.Id = $"META_UNLOCK_{content.TertiaryWeaponUnlockId}";
                node.DisplayName = $"解锁武器 {GetWeaponName(content, content.TertiaryWeaponUnlockId)}";
                node.Description = "永久解锁进阶武器";
                node.EffectType = MetaEffectType.UnlockDroneWeapon;
                node.TargetId = content.TertiaryWeaponUnlockId;
                node.Value = 0f;
                node.Cost = 160;
                node.MaxLevel = 1;
                content.MetaNodes.Add(node);
            }

            if (!string.IsNullOrWhiteSpace(content.SecondaryCharacterUnlockId))
            {
                MetaNodeDef node = ScriptableObject.CreateInstance<MetaNodeDef>();
                node.Id = $"META_UNLOCK_{content.SecondaryCharacterUnlockId}";
                node.DisplayName = $"解锁角色 {GetCharacterName(content, content.SecondaryCharacterUnlockId)}";
                node.Description = "永久解锁额外角色";
                node.EffectType = MetaEffectType.UnlockEngineerCharacter;
                node.TargetId = content.SecondaryCharacterUnlockId;
                node.Value = 0f;
                node.Cost = 180;
                node.MaxLevel = 1;
                content.MetaNodes.Add(node);
            }
        }

        private static void BuildAchievements(GameContent content, List<Dictionary<string, string>> achievementRows)
        {
            for (int i = 0; i < achievementRows.Count; i++)
            {
                Dictionary<string, string> row = achievementRows[i];
                string id = GetString(row, "AchievementID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                int serial = ExtractTrailingNumber(id);
                int score = Mathf.Max(10, ParseInt(GetString(row, "Score"), 10));
                string category = GetString(row, "Category");
                AchievementTrigger trigger = ResolveAchievementTrigger(category, serial);
                int target = ResolveAchievementTarget(trigger, serial, score);

                AchievementDef def = ScriptableObject.CreateInstance<AchievementDef>();
                def.Id = id;
                def.DisplayName = GetString(row, "Name", id);
                def.Description = GetString(row, "Condition", GetString(row, "RewardDesc", def.DisplayName));
                def.Trigger = trigger;
                def.TargetInt = target;
                def.TargetFloat = 0f;
                def.RewardChips = Mathf.Max(5, score);
                content.Achievements.Add(def);
            }
        }

        private static void BuildEvents(GameContent content, List<Dictionary<string, string>> eventRows)
        {
            float runEnd = content.RunDurationSeconds + content.BossGraceSeconds;
            for (int i = 0; i < eventRows.Count; i++)
            {
                Dictionary<string, string> row = eventRows[i];
                string id = GetString(row, "EventID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string typeRaw = GetString(row, "Type");
                RunEventType type = MapRunEventType(typeRaw);
                int stage = ParseStageFromUnlockReq(GetString(row, "UnlockReq"));
                float rewardMult = ParseFloat(GetString(row, "RewardMult"), 1f);
                float riskMult = ParseFloat(GetString(row, "RiskMult"), 0.1f);
                float minTime = Mathf.Clamp((stage - 1) * (content.RunDurationSeconds / 7f), 0f, runEnd);
                float maxTime = runEnd;
                int goldCost = type == RunEventType.MerchantBoost
                    ? Mathf.RoundToInt(85f + stage * 22f + riskMult * 80f)
                    : 0;
                float value = ResolveEventValue(type, rewardMult, riskMult);

                RunEventDef def = ScriptableObject.CreateInstance<RunEventDef>();
                def.Id = id;
                def.DisplayName = GetString(row, "Name", id);
                def.Description = GetString(row, "Description", def.DisplayName);
                def.Type = type;
                def.MinTime = minTime;
                def.MaxTime = maxTime;
                def.GoldCost = Mathf.Max(0, goldCost);
                def.Value = value;
                content.Events.Add(def);
            }
        }

        private static void BuildCodex(GameContent content)
        {
            content.CodexEntries.Clear();

            for (int i = 0; i < content.Moles.Count; i++)
            {
                MoleDef mole = content.Moles[i];
                AddCodex(content, $"codex_{mole.Id}", mole.DisplayName, "地鼠", $"遭遇并击败：{mole.DisplayName}");
            }

            for (int i = 0; i < content.Weapons.Count; i++)
            {
                WeaponDef weapon = content.Weapons[i];
                AddCodex(content, $"codex_{weapon.Id}", weapon.DisplayName, "武器", weapon.Description);
            }

            for (int i = 0; i < content.Characters.Count; i++)
            {
                CharacterDef character = content.Characters[i];
                AddCodex(content, $"codex_{character.Id}", character.DisplayName, "角色", character.Description);
            }

            for (int i = 0; i < content.Facilities.Count; i++)
            {
                FacilityDef facility = content.Facilities[i];
                AddCodex(content, $"codex_{facility.Id}", facility.DisplayName, "设施", facility.Description);
            }

            for (int i = 0; i < content.Bosses.Count; i++)
            {
                BossDef boss = content.Bosses[i];
                AddCodex(content, $"codex_{boss.Id}", boss.DisplayName, "Boss", $"Boss 图鉴：{boss.DisplayName}");
            }

            AddCodex(content, "codex_boss", "Boss 总览", "Boss", "击败全部 Boss 获取最高评价。");

            for (int i = 0; i < content.Events.Count; i++)
            {
                RunEventDef runEvent = content.Events[i];
                AddCodex(content, $"codex_{runEvent.Id}", runEvent.DisplayName, "事件", runEvent.Description);
            }
        }

        private static void EnsureStartupUnlocksAndDefaults(GameContent content, RoleLoadSummary roleSummary)
        {
            if (content.StartupUnlockedWeaponIds.Count == 0 && content.Weapons.Count > 0)
            {
                AddUnique(content.StartupUnlockedWeaponIds, content.Weapons[0].Id);
            }

            if (content.StartupUnlockedCharacterIds.Count == 0 && content.Characters.Count > 0)
            {
                AddUnique(content.StartupUnlockedCharacterIds, content.Characters[0].Id);
            }

            if (!string.IsNullOrWhiteSpace(roleSummary.DefaultRoleStartWeaponId) &&
                content.Weapons.Any(w => string.Equals(w.Id, roleSummary.DefaultRoleStartWeaponId, StringComparison.OrdinalIgnoreCase)))
            {
                content.DefaultWeaponId = roleSummary.DefaultRoleStartWeaponId;
            }

            if (string.IsNullOrWhiteSpace(content.DefaultWeaponId) && content.StartupUnlockedWeaponIds.Count > 0)
            {
                content.DefaultWeaponId = content.StartupUnlockedWeaponIds[0];
            }

            if (string.IsNullOrWhiteSpace(content.DefaultWeaponId) && content.Weapons.Count > 0)
            {
                content.DefaultWeaponId = content.Weapons[0].Id;
            }

            if (!string.IsNullOrWhiteSpace(roleSummary.DefaultCharacterId) &&
                content.Characters.Any(c => string.Equals(c.Id, roleSummary.DefaultCharacterId, StringComparison.OrdinalIgnoreCase)))
            {
                content.DefaultCharacterId = roleSummary.DefaultCharacterId;
            }

            if (string.IsNullOrWhiteSpace(content.DefaultCharacterId) && content.StartupUnlockedCharacterIds.Count > 0)
            {
                content.DefaultCharacterId = content.StartupUnlockedCharacterIds[0];
            }

            if (string.IsNullOrWhiteSpace(content.DefaultCharacterId) && content.Characters.Count > 0)
            {
                content.DefaultCharacterId = content.Characters[0].Id;
            }

            AddUnique(content.StartupUnlockedWeaponIds, content.DefaultWeaponId);
            AddUnique(content.StartupUnlockedCharacterIds, content.DefaultCharacterId);

            if (string.IsNullOrWhiteSpace(content.SecondaryWeaponUnlockId))
            {
                content.SecondaryWeaponUnlockId = FindWeaponByTag(content, "Chain");
            }

            if (string.IsNullOrWhiteSpace(content.SecondaryWeaponUnlockId) && content.Weapons.Count > 1)
            {
                content.SecondaryWeaponUnlockId = content.Weapons[1].Id;
            }

            if (string.IsNullOrWhiteSpace(content.TertiaryWeaponUnlockId))
            {
                content.TertiaryWeaponUnlockId = FindWeaponByTag(content, "Drone");
            }

            if (string.IsNullOrWhiteSpace(content.TertiaryWeaponUnlockId) && content.Weapons.Count > 2)
            {
                content.TertiaryWeaponUnlockId = content.Weapons[2].Id;
            }

            if (string.IsNullOrWhiteSpace(content.SecondaryCharacterUnlockId))
            {
                content.SecondaryCharacterUnlockId = roleSummary.AutomationCharacterId;
            }

            if (string.IsNullOrWhiteSpace(content.SecondaryCharacterUnlockId) && content.Characters.Count > 1)
            {
                content.SecondaryCharacterUnlockId = content.Characters[1].Id;
            }
        }

        private static EffectMapping ResolveUpgradeEffect(
            string grantType,
            string refId,
            string category,
            HashSet<string> tags,
            SkillInfo skill,
            int rarityRank,
            int serial,
            HashSet<string> validWeaponLvIds)
        {
            UpgradeEffectType effectType;
            float skillPower = skill != null ? skill.PowerA : 0f;
            string refUpper = (refId ?? string.Empty).ToUpperInvariant();
            string categoryTag = NormalizeTag(category);

            if (string.Equals(grantType, "WeaponLv+", StringComparison.OrdinalIgnoreCase))
            {
                bool facilityWeapon = refUpper.StartsWith("WPN_FAC", StringComparison.OrdinalIgnoreCase);
                if (facilityWeapon)
                {
                    int n = Mathf.Max(1, ExtractTrailingNumber(refUpper));
                    effectType = (n % 4) switch
                    {
                        1 => UpgradeEffectType.DeployAutoHammerTower,
                        2 => UpgradeEffectType.DeploySensorHammer,
                        3 => UpgradeEffectType.DeployGoldMagnet,
                        _ => UpgradeEffectType.DeployBountyMarker,
                    };
                    tags.Add("Facility");
                    tags.Add("Automation");
                    return new EffectMapping
                    {
                        EffectType = effectType,
                        Value = 1f,
                    };
                }

                if (tags.Contains("Automation") || tags.Contains("Drone"))
                {
                    effectType = serial % 4 == 0
                        ? UpgradeEffectType.AddDroneCount
                        : UpgradeEffectType.AutoHammerIntervalMultiplier;
                }
                else if (tags.Contains("Gold"))
                {
                    effectType = UpgradeEffectType.AddGoldMultiplier;
                }
                else if (tags.Contains("Chain"))
                {
                    effectType = serial % 3 == 0
                        ? UpgradeEffectType.AddChainCount
                        : UpgradeEffectType.AddSplash;
                }
                else if (tags.Contains("Range"))
                {
                    effectType = UpgradeEffectType.AddRange;
                }
                else
                {
                    effectType = UpgradeEffectType.AddDamage;
                }

                if (!validWeaponLvIds.Contains(refId))
                {
                    effectType = UpgradeEffectType.AddDamage;
                }
            }
            else if (string.Equals(grantType, "GrantTag", StringComparison.OrdinalIgnoreCase))
            {
                if (tags.Contains("Facility"))
                {
                    effectType = UpgradeEffectType.FacilityPowerMultiplier;
                }
                else if (tags.Contains("Bounty"))
                {
                    effectType = UpgradeEffectType.DeployBountyMarker;
                }
                else if (tags.Contains("Chain"))
                {
                    effectType = UpgradeEffectType.AddChainCount;
                }
                else
                {
                    effectType = UpgradeEffectType.AddBossDamageMultiplier;
                }
            }
            else if (string.Equals(grantType, "GrantSkill", StringComparison.OrdinalIgnoreCase))
            {
                if (refUpper.Contains("PASS_DMG") || tags.Contains("Damage"))
                {
                    effectType = UpgradeEffectType.AddDamage;
                }
                else if (refUpper.Contains("PASS_ASPD") || tags.Contains("AttackSpeed"))
                {
                    effectType = UpgradeEffectType.AttackIntervalMultiplier;
                }
                else if (refUpper.Contains("PASS_AOE") || tags.Contains("Range"))
                {
                    effectType = UpgradeEffectType.AddRange;
                }
                else if (refUpper.Contains("PASS_CRIT") || tags.Contains("Crit"))
                {
                    effectType = serial % 5 == 0
                        ? UpgradeEffectType.AddCritDamage
                        : UpgradeEffectType.AddCritChance;
                }
                else if (refUpper.Contains("PASS_GOLD") || tags.Contains("Gold"))
                {
                    effectType = UpgradeEffectType.AddGoldMultiplier;
                }
                else if (refUpper.Contains("SHOCK_CHAIN"))
                {
                    effectType = UpgradeEffectType.AddChainCount;
                }
                else if (refUpper.Contains("BURN_BURST"))
                {
                    effectType = UpgradeEffectType.AddSplash;
                }
                else if (refUpper.Contains("EXEC_GRID"))
                {
                    effectType = UpgradeEffectType.DeploySensorHammer;
                }
                else if (refUpper.Contains("GOLD_ARMOR"))
                {
                    effectType = UpgradeEffectType.AddMaxDurability;
                }
                else if (refUpper.Contains("COMBO_FRENZY"))
                {
                    effectType = UpgradeEffectType.AttackIntervalMultiplier;
                }
                else if (tags.Contains("Automation"))
                {
                    effectType = serial % 7 == 0
                        ? UpgradeEffectType.UnlockAutoHammer
                        : UpgradeEffectType.AutoHammerIntervalMultiplier;
                }
                else if (tags.Contains("Chain") || tags.Contains("Shock") || tags.Contains("Frost") || tags.Contains("Fire"))
                {
                    effectType = UpgradeEffectType.AddSplash;
                }
                else
                {
                    effectType = UpgradeEffectType.AddDamage;
                }
            }
            else
            {
                string statRef = NormalizeTag(refId);
                if (statRef == "Damage")
                {
                    effectType = UpgradeEffectType.AddDamage;
                }
                else if (statRef == "Range")
                {
                    effectType = UpgradeEffectType.AddRange;
                }
                else if (statRef == "Crit")
                {
                    effectType = serial % 4 == 0
                        ? UpgradeEffectType.AddCritDamage
                        : UpgradeEffectType.AddCritChance;
                }
                else if (statRef == "Gold")
                {
                    effectType = UpgradeEffectType.AddGoldMultiplier;
                }
                else if (statRef == "Execute")
                {
                    effectType = serial % 2 == 0
                        ? UpgradeEffectType.AddBossDamageMultiplier
                        : UpgradeEffectType.AddMaxDurability;
                }
                else if (statRef == "Automation")
                {
                    effectType = serial % 5 == 0
                        ? UpgradeEffectType.FacilityCooldownMultiplier
                        : UpgradeEffectType.AutoHammerIntervalMultiplier;
                }
                else if (statRef == "Bounty" || statRef == "Rare")
                {
                    effectType = serial % 3 == 0
                        ? UpgradeEffectType.DeployBountyMarker
                        : UpgradeEffectType.FacilityGoldMultiplier;
                }
                else if (statRef == "Defense")
                {
                    effectType = UpgradeEffectType.AddMaxDurability;
                }
                else
                {
                    effectType = string.Equals(categoryTag, "Automation", StringComparison.OrdinalIgnoreCase)
                        ? UpgradeEffectType.AutoHammerIntervalMultiplier
                        : UpgradeEffectType.AddDamage;
                }
            }

            float value = ResolveUpgradeEffectValue(effectType, rarityRank, skillPower, serial);
            return new EffectMapping
            {
                EffectType = effectType,
                Value = value,
            };
        }

        private static float ResolveUpgradeEffectValue(UpgradeEffectType effectType, int rarityRank, float skillPower, int serial)
        {
            switch (effectType)
            {
                case UpgradeEffectType.AddDamage:
                    return Mathf.Clamp(0.9f + rarityRank * 0.45f + skillPower * 3.3f, 0.4f, 8f);
                case UpgradeEffectType.AttackIntervalMultiplier:
                    return Mathf.Clamp(0.97f - rarityRank * 0.014f - skillPower * 0.075f, 0.72f, 0.995f);
                case UpgradeEffectType.AddRange:
                    return Mathf.Clamp(0.03f + rarityRank * 0.015f + skillPower * 0.09f, 0.01f, 0.3f);
                case UpgradeEffectType.AddCritChance:
                    return Mathf.Clamp(0.011f + rarityRank * 0.006f + skillPower * 0.03f, 0.005f, 0.2f);
                case UpgradeEffectType.AddCritDamage:
                    return Mathf.Clamp(0.14f + rarityRank * 0.09f + skillPower * 0.2f, 0.08f, 1f);
                case UpgradeEffectType.AddChainCount:
                    return 1f;
                case UpgradeEffectType.AddSplash:
                    return Mathf.Clamp(0.12f + rarityRank * 0.08f + skillPower * 0.22f, 0.08f, 1.2f);
                case UpgradeEffectType.AddGoldMultiplier:
                    return Mathf.Clamp(0.06f + rarityRank * 0.03f + skillPower * 0.13f, 0.02f, 0.6f);
                case UpgradeEffectType.AddExpMultiplier:
                    return Mathf.Clamp(0.05f + rarityRank * 0.025f + skillPower * 0.1f, 0.02f, 0.5f);
                case UpgradeEffectType.UnlockAutoHammer:
                    return Mathf.Clamp(1.45f - rarityRank * 0.1f, 0.92f, 1.7f);
                case UpgradeEffectType.AutoHammerIntervalMultiplier:
                    return Mathf.Clamp(0.94f - rarityRank * 0.03f - skillPower * 0.03f, 0.72f, 0.98f);
                case UpgradeEffectType.UnlockAutoAim:
                    return 1f;
                case UpgradeEffectType.AddDroneCount:
                    return 1f;
                case UpgradeEffectType.AddMagnetRadius:
                    return Mathf.Clamp(0.14f + rarityRank * 0.07f, 0.08f, 0.8f);
                case UpgradeEffectType.AddMaxDurability:
                    return Mathf.Clamp(1f + rarityRank, 1f, 5f);
                case UpgradeEffectType.AddBossDamageMultiplier:
                    return Mathf.Clamp(0.08f + rarityRank * 0.04f + skillPower * 0.1f, 0.04f, 0.6f);
                case UpgradeEffectType.DeployAutoHammerTower:
                case UpgradeEffectType.DeploySensorHammer:
                case UpgradeEffectType.DeployGoldMagnet:
                case UpgradeEffectType.DeployBountyMarker:
                    return 1f;
                case UpgradeEffectType.FacilityCooldownMultiplier:
                    return Mathf.Clamp(0.93f - rarityRank * 0.03f, 0.76f, 0.98f);
                case UpgradeEffectType.FacilityPowerMultiplier:
                    return Mathf.Clamp(0.1f + rarityRank * 0.05f + skillPower * 0.08f, 0.06f, 0.8f);
                case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                    return Mathf.Clamp(0.92f - rarityRank * 0.02f, 0.72f, 0.97f);
                case UpgradeEffectType.FacilityGoldMultiplier:
                    return Mathf.Clamp(0.1f + rarityRank * 0.04f, 0.06f, 0.7f);
                default:
                    return serial % 2 == 0 ? 1f : 0.5f;
            }
        }

        private static int ResolveUpgradeMaxStacks(UpgradeEffectType effectType, SkillInfo skill, int serial)
        {
            int candidate = skill != null ? skill.MaxStack : 1;
            if (candidate > 1)
            {
                candidate = Mathf.Clamp(candidate / 20, 1, 3);
            }

            switch (effectType)
            {
                case UpgradeEffectType.AddDamage:
                case UpgradeEffectType.AddRange:
                case UpgradeEffectType.AddGoldMultiplier:
                case UpgradeEffectType.AddExpMultiplier:
                case UpgradeEffectType.AddBossDamageMultiplier:
                case UpgradeEffectType.FacilityPowerMultiplier:
                case UpgradeEffectType.FacilityGoldMultiplier:
                    return Mathf.Max(1, Mathf.Clamp(candidate + (serial % 2 == 0 ? 1 : 0), 1, 3));
                default:
                    return 1;
            }
        }

        private static int ResolveUpgradeUnlockSecond(GameContent content, Dictionary<string, string> row)
        {
            int minLevel = Mathf.Max(1, ParseInt(GetString(row, "MinLv"), 1));
            float unlock = (minLevel - 1) * 18f;
            string phaseBias = GetString(row, "PhaseBias");
            if (string.Equals(phaseBias, "Mid", StringComparison.OrdinalIgnoreCase))
            {
                unlock = Mathf.Max(unlock, content.RunDurationSeconds * 0.2f);
            }
            else if (string.Equals(phaseBias, "Late", StringComparison.OrdinalIgnoreCase))
            {
                unlock = Mathf.Max(unlock, content.RunDurationSeconds * 0.42f);
            }
            else if (string.Equals(phaseBias, "End", StringComparison.OrdinalIgnoreCase))
            {
                unlock = Mathf.Max(unlock, content.RunDurationSeconds * 0.62f);
            }

            return Mathf.RoundToInt(Mathf.Clamp(unlock, 0f, content.RunDurationSeconds));
        }

        private static float ResolveUpgradeWeight(Dictionary<string, string> row, int rarityRank)
        {
            float raw = ParseFloat(GetString(row, "Weight"), 60f);
            float normalized = Mathf.Clamp(raw / 55f, 0.18f, 3.2f);
            bool starterBias = ParseBool(GetString(row, "StarterBias"), false);
            if (starterBias)
            {
                normalized *= 1.12f;
            }

            normalized *= 1f - rarityRank * 0.05f;
            return Mathf.Clamp(normalized, 0.15f, 3.5f);
        }

        private static float ResolveMetaValue(MetaEffectType effectType, float rawValue)
        {
            switch (effectType)
            {
                case MetaEffectType.AddStartDamage:
                    return Mathf.Clamp(rawValue * 18f, 0.2f, 8f);
                case MetaEffectType.AttackIntervalMultiplier:
                    return Mathf.Clamp(1f - rawValue * 0.45f, 0.82f, 0.995f);
                case MetaEffectType.AddStartRange:
                    return Mathf.Clamp(rawValue * 0.32f, 0.01f, 0.22f);
                case MetaEffectType.AddMaxDurability:
                    return Mathf.Clamp(rawValue * 20f, 1f, 8f);
                case MetaEffectType.AddGoldMultiplier:
                    return Mathf.Clamp(rawValue * 0.8f, 0.01f, 0.5f);
                case MetaEffectType.AddExpMultiplier:
                    return Mathf.Clamp(rawValue * 0.75f, 0.01f, 0.5f);
                case MetaEffectType.AddStartingGold:
                    return Mathf.Clamp(rawValue * 220f, 10f, 220f);
                default:
                    return 0f;
            }
        }

        private static MetaEffectType ResolveMetaEffectType(string category)
        {
            string normalized = NormalizeTag(category);
            switch (normalized)
            {
                case "Damage":
                    return MetaEffectType.AddStartDamage;
                case "AttackSpeed":
                    return MetaEffectType.AttackIntervalMultiplier;
                case "Range":
                case "AOE":
                    return MetaEffectType.AddStartRange;
                case "Gold":
                    return MetaEffectType.AddGoldMultiplier;
                case "Exp":
                    return MetaEffectType.AddExpMultiplier;
                case "Utility":
                    return MetaEffectType.AddStartingGold;
                case "Defense":
                case "Boss":
                case "Facility":
                    return MetaEffectType.AddMaxDurability;
                case "Automation":
                    return MetaEffectType.AttackIntervalMultiplier;
                default:
                    return MetaEffectType.AddStartDamage;
            }
        }

        private static AchievementTrigger ResolveAchievementTrigger(string category, int serial)
        {
            bool challenge = category.IndexOf("Challenge", StringComparison.OrdinalIgnoreCase) >= 0;
            if (challenge)
            {
                AchievementTrigger[] cycle =
                {
                    AchievementTrigger.BossWin,
                    AchievementTrigger.AutoKillsInRun,
                    AchievementTrigger.MetaNodesPurchased,
                    AchievementTrigger.CoreShardsInRun,
                    AchievementTrigger.LifetimeWins,
                };
                return cycle[Mathf.Abs(serial) % cycle.Length];
            }

            AchievementTrigger[] progressCycle =
            {
                AchievementTrigger.KillCountInRun,
                AchievementTrigger.LifetimeRuns,
                AchievementTrigger.GoldInRun,
                AchievementTrigger.ComboInRun,
                AchievementTrigger.CodexDiscoveries,
            };
            return progressCycle[Mathf.Abs(serial) % progressCycle.Length];
        }

        private static int ResolveAchievementTarget(AchievementTrigger trigger, int serial, int score)
        {
            int rank = Mathf.Max(1, serial);
            switch (trigger)
            {
                case AchievementTrigger.KillCountInRun:
                    return 30 + rank * 6;
                case AchievementTrigger.LifetimeRuns:
                    return 1 + rank / 2;
                case AchievementTrigger.GoldInRun:
                    return 800 + rank * 120;
                case AchievementTrigger.ComboInRun:
                    return 8 + rank;
                case AchievementTrigger.CodexDiscoveries:
                    return 5 + rank / 3;
                case AchievementTrigger.BossWin:
                    return 1;
                case AchievementTrigger.AutoKillsInRun:
                    return 20 + rank * 4;
                case AchievementTrigger.MetaNodesPurchased:
                    return 2 + rank / 2;
                case AchievementTrigger.CoreShardsInRun:
                    return 8 + rank * 3;
                case AchievementTrigger.LifetimeWins:
                    return 1 + rank / 4;
                default:
                    return Mathf.Max(1, score / 2);
            }
        }

        private static RunEventType MapRunEventType(string sourceType)
        {
            switch ((sourceType ?? string.Empty).Trim())
            {
                case "Shop":
                    return RunEventType.MerchantBoost;
                case "Reward":
                case "Buff":
                    return RunEventType.TreasureRush;
                case "RiskReward":
                case "Crisis":
                    return RunEventType.CurseAltar;
                case "Facility":
                    return RunEventType.RepairStation;
                case "Quest":
                    return RunEventType.BountyContract;
                case "Combat":
                    return RunEventType.RogueHoleZone;
                case "Utility":
                    return RunEventType.MerchantBoost;
                default:
                    return RunEventType.TreasureRush;
            }
        }

        private static float ResolveEventValue(RunEventType type, float rewardMult, float riskMult)
        {
            switch (type)
            {
                case RunEventType.MerchantBoost:
                    return 1f;
                case RunEventType.TreasureRush:
                    return Mathf.Clamp(14f + rewardMult * 10f, 8f, 40f);
                case RunEventType.CurseAltar:
                    return Mathf.Clamp(15f + riskMult * 28f, 12f, 45f);
                case RunEventType.RepairStation:
                    return Mathf.Clamp(2f + rewardMult * 2.5f, 2f, 9f);
                case RunEventType.BountyContract:
                    return Mathf.Clamp(15f + rewardMult * 8f, 10f, 40f);
                case RunEventType.RogueHoleZone:
                    return Mathf.Clamp(10f + riskMult * 20f, 8f, 35f);
                default:
                    return 1f;
            }
        }

        private static int ParseStageFromUnlockReq(string unlockReq)
        {
            if (string.IsNullOrWhiteSpace(unlockReq))
            {
                return 1;
            }

            string normalized = unlockReq.Trim();
            int marker = normalized.IndexOf(">=", StringComparison.Ordinal);
            if (marker >= 0)
            {
                string right = normalized.Substring(marker + 2).Trim();
                return Mathf.Max(1, ParseInt(right, 1));
            }

            marker = normalized.IndexOf(":", StringComparison.Ordinal);
            if (marker >= 0)
            {
                string right = normalized.Substring(marker + 1).Trim();
                return Mathf.Max(1, ParseInt(right, 1));
            }

            return 1;
        }

        private static bool TryParseTagRequirement(string expression, out TagRequirement requirement)
        {
            requirement = null;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            string token = expression.Trim();
            int splitIndex = token.IndexOf(">=", StringComparison.Ordinal);
            if (splitIndex <= 0)
            {
                return false;
            }

            string tagRaw = token.Substring(0, splitIndex).Trim();
            string levelRaw = token.Substring(splitIndex + 2).Trim();
            int level = Mathf.Max(1, ParseInt(levelRaw, 1));
            string tag = NormalizeTag(tagRaw);
            if (string.IsNullOrWhiteSpace(tag) ||
                string.Equals(tag, "Build", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "BuildTag", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            requirement = new TagRequirement
            {
                Tag = tag,
                Level = level,
            };
            return true;
        }

        private static FacilityType MapFacilityType(Dictionary<string, string> row)
        {
            string theme = GetString(row, "Theme");
            string trigger = GetString(row, "Trigger");
            string target = GetString(row, "Target");
            string upperTheme = (theme ?? string.Empty).ToUpperInvariant();

            if (upperTheme.Contains("AUTOMATION"))
            {
                return FacilityType.AutoHammerTower;
            }

            if (upperTheme.Contains("GOLD") ||
                string.Equals(target, "Drop", StringComparison.OrdinalIgnoreCase))
            {
                return FacilityType.GoldMagnet;
            }

            if (upperTheme.Contains("RARE") ||
                upperTheme.Contains("SPAWN"))
            {
                return FacilityType.BountyMarker;
            }

            if (upperTheme.Contains("SHOCK") ||
                upperTheme.Contains("EXECUTE") ||
                upperTheme.Contains("FROST") ||
                string.Equals(trigger, "OnHit", StringComparison.OrdinalIgnoreCase))
            {
                return FacilityType.SensorHammer;
            }

            return FacilityType.SensorHammer;
        }

        private static float CalcFacilityPriority(Dictionary<string, string> row)
        {
            Rarity rarity = ParseRarity(GetString(row, "Rarity"));
            float interval = ParseFloat(GetString(row, "IntervalSec"), 1f);
            float power = ParseFloat(GetString(row, "PowerA"), 0.2f);
            float radius = ParseFloat(GetString(row, "Radius"), 3f);
            float risk = ParseFloat(GetString(row, "RiskBonus"), 0f);
            return (int)rarity * 10f + power * 14f + radius * 1.5f - interval * 1.2f + risk * 6f;
        }

        private static MoleTrait ParseMoleTrait(string rawTrait, bool rareTarget)
        {
            MoleTrait trait = MoleTrait.None;
            string normalized = (rawTrait ?? string.Empty).Trim();
            switch (normalized)
            {
                case "Fast":
                    trait |= MoleTrait.Fast;
                    break;
                case "Armored":
                    trait |= MoleTrait.Tank;
                    break;
                case "Explosive":
                    trait |= MoleTrait.Bomb;
                    break;
                case "Treasure":
                    trait |= MoleTrait.Chest;
                    break;
                case "Electric":
                    trait |= MoleTrait.Chain;
                    break;
                case "Shield":
                    trait |= MoleTrait.Shield;
                    break;
                case "Commander":
                case "Legend":
                    trait |= MoleTrait.Elite;
                    break;
                case "Split":
                    trait |= MoleTrait.Chain;
                    break;
                case "Wealthy":
                    trait |= MoleTrait.Chest;
                    break;
            }

            if (rareTarget)
            {
                trait |= MoleTrait.Chest;
            }

            return trait == MoleTrait.None ? MoleTrait.None : trait;
        }

        private static Rarity ParseRarity(string raw)
        {
            switch ((raw ?? string.Empty).Trim())
            {
                case "Common":
                    return Rarity.Common;
                case "Uncommon":
                case "Rare":
                    return Rarity.Rare;
                case "Epic":
                    return Rarity.Epic;
                case "Legendary":
                    return Rarity.Legendary;
                default:
                    return Rarity.Common;
            }
        }

        private static Color ColorFromKey(string key, float saturation, float value)
        {
            int hash = string.IsNullOrWhiteSpace(key) ? 0 : key.GetHashCode();
            float hue = Mathf.Abs(hash % 1000) / 1000f;
            return Color.HSVToRGB(hue, Mathf.Clamp01(saturation), Mathf.Clamp01(value));
        }

        private static string NormalizeTag(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string tag = raw.Trim();
            if (tag.StartsWith("TAG_", StringComparison.OrdinalIgnoreCase))
            {
                tag = tag.Substring(4);
            }

            if (tag.Equals("AOE", StringComparison.OrdinalIgnoreCase))
            {
                return "Range";
            }

            if (tag.Equals("AttackSpeed", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("ASPD", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("Speed", StringComparison.OrdinalIgnoreCase))
            {
                return "AttackSpeed";
            }

            if (tag.Equals("Damage", StringComparison.OrdinalIgnoreCase) || tag.Equals("DMG", StringComparison.OrdinalIgnoreCase))
            {
                return "Damage";
            }

            if (tag.Equals("Crit", StringComparison.OrdinalIgnoreCase))
            {
                return "Crit";
            }

            if (tag.Equals("Gold", StringComparison.OrdinalIgnoreCase) || tag.Equals("Economy", StringComparison.OrdinalIgnoreCase))
            {
                return "Gold";
            }

            if (tag.Equals("XP", StringComparison.OrdinalIgnoreCase))
            {
                return "Exp";
            }

            if (tag.Equals("Shock", StringComparison.OrdinalIgnoreCase) || tag.Equals("Chain", StringComparison.OrdinalIgnoreCase))
            {
                return "Chain";
            }

            if (tag.Equals("Automation", StringComparison.OrdinalIgnoreCase) || tag.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            {
                return "Automation";
            }

            if (tag.Equals("Drone", StringComparison.OrdinalIgnoreCase))
            {
                return "Drone";
            }

            if (tag.Equals("Rare", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("RareTarget", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("RareSpawn", StringComparison.OrdinalIgnoreCase))
            {
                return "Bounty";
            }

            if (tag.Equals("Execute", StringComparison.OrdinalIgnoreCase))
            {
                return "Boss";
            }

            if (tag.Equals("Facility", StringComparison.OrdinalIgnoreCase))
            {
                return "Facility";
            }

            if (tag.Equals("Defense", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("Tank", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("Shield", StringComparison.OrdinalIgnoreCase))
            {
                return "Defense";
            }

            if (tag.Equals("Boss", StringComparison.OrdinalIgnoreCase))
            {
                return "Boss";
            }

            if (tag.Length <= 1)
            {
                return string.Empty;
            }

            return char.ToUpperInvariant(tag[0]) + tag.Substring(1);
        }

        private static List<string> SplitMulti(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static bool ParseBool(string raw, bool fallback = false)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            string value = raw.Trim();
            if (value == "1" || value.Equals("Y", StringComparison.OrdinalIgnoreCase) || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("N", StringComparison.OrdinalIgnoreCase) || value.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fallback;
        }

        private static int ParseInt(string raw, int fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (int.TryParse(raw.Trim(), NumberStyles.Integer, Invariant, out int value))
            {
                return value;
            }

            if (float.TryParse(raw.Trim(), NumberStyles.Float, Invariant, out float f))
            {
                return Mathf.RoundToInt(f);
            }

            return fallback;
        }

        private static float ParseFloat(string raw, float fallback = 0f)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            return float.TryParse(raw.Trim(), NumberStyles.Float, Invariant, out float value)
                ? value
                : fallback;
        }

        private static string GetString(Dictionary<string, string> row, string key, string fallback = "")
        {
            if (row == null || string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            return row.TryGetValue(key, out string value)
                ? (value ?? string.Empty)
                : fallback;
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            int end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end]))
            {
                end--;
            }

            if (end < 0)
            {
                return 0;
            }

            int start = end;
            while (start >= 0 && char.IsDigit(value[start]))
            {
                start--;
            }

            string digits = value.Substring(start + 1, end - start);
            return ParseInt(digits, 0);
        }

        private static string TrimBom(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text[0] == '\uFEFF' ? text.Substring(1) : text;
        }

        private static void AddUpgradeTag(HashSet<string> set, string tag)
        {
            if (set == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            set.Add(tag);
        }

        private static void AddUpgradeTag(List<string> list, string tag)
        {
            if (list == null || string.IsNullOrWhiteSpace(tag) || list.Contains(tag))
            {
                return;
            }

            list.Add(tag);
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value) || list.Contains(value))
            {
                return;
            }

            list.Add(value);
        }

        private static void AddCodex(GameContent content, string id, string name, string category, string description)
        {
            if (content.CodexEntries.Any(entry => entry != null && string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            CodexDef def = ScriptableObject.CreateInstance<CodexDef>();
            def.Id = id;
            def.DisplayName = name;
            def.Category = category;
            def.Description = description;
            content.CodexEntries.Add(def);
        }

        private static string FindWeaponByTag(GameContent content, string tag)
        {
            if (content == null || content.Weapons == null || content.Weapons.Count == 0)
            {
                return string.Empty;
            }

            string normalized = NormalizeTag(tag);
            return content.Weapons
                .FirstOrDefault(weapon =>
                    weapon != null &&
                    !string.IsNullOrWhiteSpace(weapon.Id) &&
                    (weapon.DisplayName.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (normalized == "Chain" && weapon.ChainCount > 0) ||
                     (normalized == "Drone" && weapon.DroneCount > 0)))
                ?.Id ?? string.Empty;
        }

        private static string GetWeaponName(GameContent content, string weaponId)
        {
            return content.Weapons.FirstOrDefault(weapon => weapon.Id == weaponId)?.DisplayName ?? weaponId;
        }

        private static string GetCharacterName(GameContent content, string characterId)
        {
            return content.Characters.FirstOrDefault(character => character.Id == characterId)?.DisplayName ?? characterId;
        }
    }
}
