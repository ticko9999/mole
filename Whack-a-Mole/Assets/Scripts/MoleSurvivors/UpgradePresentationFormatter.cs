using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoleSurvivors
{
    public readonly struct UpgradeStatsSnapshot
    {
        public readonly float Damage;
        public readonly float AttackInterval;
        public readonly float AttackRadius;
        public readonly float CritChance;
        public readonly float CritDamage;
        public readonly int ChainCount;
        public readonly float SplashRadius;
        public readonly bool AutoAim;
        public readonly float AutoHammerInterval;
        public readonly int DroneCount;
        public readonly float GoldMultiplier;
        public readonly float ExpMultiplier;
        public readonly float MagnetRadius;
        public readonly float BossDamageMultiplier;
        public readonly int Durability;
        public readonly int MaxDurability;
        public readonly int ActiveFacilityCount;
        public readonly float FacilityCooldownMultiplier;
        public readonly float FacilityPowerMultiplier;
        public readonly float FacilityGoldMultiplier;
        public readonly int FacilityOverloadThresholdCurrent;

        private UpgradeStatsSnapshot(
            float damage,
            float attackInterval,
            float attackRadius,
            float critChance,
            float critDamage,
            int chainCount,
            float splashRadius,
            bool autoAim,
            float autoHammerInterval,
            int droneCount,
            float goldMultiplier,
            float expMultiplier,
            float magnetRadius,
            float bossDamageMultiplier,
            int durability,
            int maxDurability,
            int activeFacilityCount,
            float facilityCooldownMultiplier,
            float facilityPowerMultiplier,
            float facilityGoldMultiplier,
            int facilityOverloadThresholdCurrent)
        {
            Damage = damage;
            AttackInterval = attackInterval;
            AttackRadius = attackRadius;
            CritChance = critChance;
            CritDamage = critDamage;
            ChainCount = chainCount;
            SplashRadius = splashRadius;
            AutoAim = autoAim;
            AutoHammerInterval = autoHammerInterval;
            DroneCount = droneCount;
            GoldMultiplier = goldMultiplier;
            ExpMultiplier = expMultiplier;
            MagnetRadius = magnetRadius;
            BossDamageMultiplier = bossDamageMultiplier;
            Durability = durability;
            MaxDurability = maxDurability;
            ActiveFacilityCount = activeFacilityCount;
            FacilityCooldownMultiplier = facilityCooldownMultiplier;
            FacilityPowerMultiplier = facilityPowerMultiplier;
            FacilityGoldMultiplier = facilityGoldMultiplier;
            FacilityOverloadThresholdCurrent = facilityOverloadThresholdCurrent;
        }

        public static UpgradeStatsSnapshot Capture(RunState run)
        {
            if (run == null || run.Stats == null)
            {
                return default;
            }

            return new UpgradeStatsSnapshot(
                run.Stats.Damage,
                run.Stats.AttackInterval,
                run.Stats.AttackRadius,
                run.Stats.CritChance,
                run.Stats.CritDamage,
                run.Stats.ChainCount,
                run.Stats.SplashRadius,
                run.Stats.AutoAim,
                run.Stats.AutoHammerInterval,
                run.Stats.DroneCount,
                run.Stats.GoldMultiplier,
                run.Stats.ExpMultiplier,
                run.Stats.MagnetRadius,
                run.Stats.BossDamageMultiplier,
                run.Durability,
                run.MaxDurability,
                run.ActiveFacilityCount,
                run.FacilityCooldownMultiplier,
                run.FacilityPowerMultiplier,
                run.FacilityGoldMultiplier,
                run.FacilityOverloadThresholdCurrent);
        }
    }

    public static class UpgradePresentationFormatter
    {
        public static string BuildReadableDescription(UpgradeDef def, RunState run)
        {
            if (def == null)
            {
                return "无效升级";
            }

            string effectSummary = BuildEffectSummary(def, run);
            string raw = def.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return effectSummary;
            }

            bool isPlaceholder = raw.IndexOf("向泛用条目", StringComparison.Ordinal) >= 0 ||
                                 raw.IndexOf("给与技能", StringComparison.Ordinal) >= 0 ||
                                 raw.IndexOf("条目", StringComparison.Ordinal) >= 0;
            return isPlaceholder ? effectSummary : $"{effectSummary} | {raw}";
        }

        public static string BuildPreviewLine(UpgradeDef def, RunState run)
        {
            if (def == null || run == null || run.Stats == null)
            {
                return "数值预览不可用";
            }

            return def.EffectType switch
            {
                UpgradeEffectType.AddDamage => $"伤害 {run.Stats.Damage:0.0} -> {run.Stats.Damage + def.Value:0.0}",
                UpgradeEffectType.AttackIntervalMultiplier => $"攻速 {run.Stats.AttackInterval:0.00}s -> {Mathf.Max(0.1f, run.Stats.AttackInterval * def.Value):0.00}s",
                UpgradeEffectType.AddRange => $"范围 {run.Stats.AttackRadius:0.00} -> {run.Stats.AttackRadius + def.Value:0.00}",
                UpgradeEffectType.AddCritChance => $"暴击率 {run.Stats.CritChance * 100f:0.#}% -> {Mathf.Clamp01(run.Stats.CritChance + def.Value) * 100f:0.#}%",
                UpgradeEffectType.AddCritDamage => $"暴伤 x{run.Stats.CritDamage:0.00} -> x{run.Stats.CritDamage + def.Value:0.00}",
                UpgradeEffectType.AddChainCount => $"连锁 {run.Stats.ChainCount} -> {run.Stats.ChainCount + Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddSplash => $"溅射 {run.Stats.SplashRadius:0.00} -> {run.Stats.SplashRadius + def.Value:0.00}",
                UpgradeEffectType.AddGoldMultiplier => $"金币收益 {(run.Stats.GoldMultiplier - 1f) * 100f:0.#}% -> {(run.Stats.GoldMultiplier + def.Value - 1f) * 100f:0.#}%",
                UpgradeEffectType.AddExpMultiplier => $"经验收益 {(run.Stats.ExpMultiplier - 1f) * 100f:0.#}% -> {(run.Stats.ExpMultiplier + def.Value - 1f) * 100f:0.#}%",
                UpgradeEffectType.UnlockAutoHammer => run.Stats.AutoHammerInterval <= 0f
                    ? $"自动锤解锁: {def.Value:0.00}s/次"
                    : $"自动锤提频: {run.Stats.AutoHammerInterval:0.00}s -> {Mathf.Min(run.Stats.AutoHammerInterval, def.Value):0.00}s",
                UpgradeEffectType.AutoHammerIntervalMultiplier => run.Stats.AutoHammerInterval <= 0f
                    ? "自动锤解锁: 1.40s/次"
                    : $"自动锤提频: {run.Stats.AutoHammerInterval:0.00}s -> {Mathf.Max(0.12f, run.Stats.AutoHammerInterval * def.Value):0.00}s",
                UpgradeEffectType.UnlockAutoAim => run.Stats.AutoAim ? "自动瞄准已启用" : "自动瞄准: 关闭 -> 开启",
                UpgradeEffectType.AddDroneCount => $"无人机 {run.Stats.DroneCount} -> {run.Stats.DroneCount + Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddMagnetRadius => $"磁吸半径 {run.Stats.MagnetRadius:0.00} -> {run.Stats.MagnetRadius + def.Value:0.00}",
                UpgradeEffectType.AddMaxDurability => $"耐久 {run.Durability}/{run.MaxDurability} -> {run.Durability + Mathf.RoundToInt(def.Value)}/{run.MaxDurability + Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddBossDamageMultiplier => $"Boss增伤 {(run.Stats.BossDamageMultiplier - 1f) * 100f:0.#}% -> {(run.Stats.BossDamageMultiplier + def.Value - 1f) * 100f:0.#}%",
                UpgradeEffectType.DeployAutoHammerTower => "部署自动锤塔 Lv+1",
                UpgradeEffectType.DeploySensorHammer => "部署感应雷锤 Lv+1",
                UpgradeEffectType.DeployGoldMagnet => "部署金币吸附器 Lv+1",
                UpgradeEffectType.DeployBountyMarker => "部署赏金标记器 Lv+1",
                UpgradeEffectType.FacilityCooldownMultiplier => $"设施冷却倍率 x{run.FacilityCooldownMultiplier:0.00} -> x{Mathf.Clamp(run.FacilityCooldownMultiplier * def.Value, 0.45f, 1.2f):0.00}",
                UpgradeEffectType.FacilityPowerMultiplier => $"设施强度倍率 x{run.FacilityPowerMultiplier:0.00} -> x{Mathf.Clamp(run.FacilityPowerMultiplier + def.Value, 0.6f, 4f):0.00}",
                UpgradeEffectType.FacilityOverloadThresholdMultiplier => $"超载阈值 {run.FacilityOverloadThresholdCurrent} -> {Mathf.Clamp(Mathf.RoundToInt(run.FacilityOverloadThresholdCurrent * def.Value), 10, 70)}",
                UpgradeEffectType.FacilityGoldMultiplier => $"设施收益倍率 x{run.FacilityGoldMultiplier:0.00} -> x{Mathf.Clamp(run.FacilityGoldMultiplier + def.Value, 0.5f, 4f):0.00}",
                _ => BuildEffectSummary(def, run),
            };
        }

        public static string BuildAppliedDeltaLine(UpgradeDef def, UpgradeStatsSnapshot before, UpgradeStatsSnapshot after, RunState run)
        {
            List<string> parts = new List<string>(5);

            AppendFloatDelta(parts, "伤害", before.Damage, after.Damage, "0.0");
            AppendFloatDelta(parts, "攻速", before.AttackInterval, after.AttackInterval, "0.00s");
            AppendFloatDelta(parts, "范围", before.AttackRadius, after.AttackRadius, "0.00");
            AppendPercentDelta(parts, "暴击率", before.CritChance, after.CritChance);
            AppendFloatDelta(parts, "暴伤", before.CritDamage, after.CritDamage, "x0.00");
            AppendIntDelta(parts, "连锁", before.ChainCount, after.ChainCount);
            AppendFloatDelta(parts, "溅射", before.SplashRadius, after.SplashRadius, "0.00");
            AppendPercentDelta(parts, "金币收益", before.GoldMultiplier - 1f, after.GoldMultiplier - 1f);
            AppendPercentDelta(parts, "经验收益", before.ExpMultiplier - 1f, after.ExpMultiplier - 1f);
            AppendIntDelta(parts, "无人机", before.DroneCount, after.DroneCount);
            AppendFloatDelta(parts, "磁吸", before.MagnetRadius, after.MagnetRadius, "0.00");
            AppendPercentDelta(parts, "Boss增伤", before.BossDamageMultiplier - 1f, after.BossDamageMultiplier - 1f);
            AppendIntDelta(parts, "耐久上限", before.MaxDurability, after.MaxDurability);
            AppendFloatDelta(parts, "设施冷却倍率", before.FacilityCooldownMultiplier, after.FacilityCooldownMultiplier, "x0.00");
            AppendFloatDelta(parts, "设施强度倍率", before.FacilityPowerMultiplier, after.FacilityPowerMultiplier, "x0.00");
            AppendFloatDelta(parts, "设施收益倍率", before.FacilityGoldMultiplier, after.FacilityGoldMultiplier, "x0.00");
            AppendIntDelta(parts, "超载阈值", before.FacilityOverloadThresholdCurrent, after.FacilityOverloadThresholdCurrent);

            if (before.AutoAim != after.AutoAim)
            {
                parts.Add(after.AutoAim ? "自动瞄准 开启" : "自动瞄准 关闭");
            }

            if (before.AutoHammerInterval != after.AutoHammerInterval)
            {
                if (before.AutoHammerInterval <= 0f && after.AutoHammerInterval > 0f)
                {
                    parts.Add($"自动锤解锁 {after.AutoHammerInterval:0.00}s");
                }
                else
                {
                    parts.Add($"自动锤 {before.AutoHammerInterval:0.00}s -> {after.AutoHammerInterval:0.00}s");
                }
            }

            if (parts.Count == 0)
            {
                return BuildPreviewLine(def, run);
            }

            if (parts.Count > 3)
            {
                return $"{parts[0]} / {parts[1]} / {parts[2]}";
            }

            return string.Join(" / ", parts);
        }

        private static void AppendFloatDelta(List<string> parts, string label, float before, float after, string format)
        {
            if (Mathf.Abs(after - before) < 0.0001f)
            {
                return;
            }

            string valueBefore = FormatValue(before, format);
            string valueAfter = FormatValue(after, format);
            parts.Add($"{label} {valueBefore} -> {valueAfter}");
        }

        private static void AppendIntDelta(List<string> parts, string label, int before, int after)
        {
            if (before == after)
            {
                return;
            }

            parts.Add($"{label} {before} -> {after}");
        }

        private static void AppendPercentDelta(List<string> parts, string label, float beforeRatio, float afterRatio)
        {
            if (Mathf.Abs(afterRatio - beforeRatio) < 0.0001f)
            {
                return;
            }

            parts.Add($"{label} {beforeRatio * 100f:0.#}% -> {afterRatio * 100f:0.#}%");
        }

        private static string FormatValue(float value, string format)
        {
            if (format.StartsWith("x", StringComparison.Ordinal))
            {
                string number = format.Substring(1);
                return $"x{value.ToString(number)}";
            }

            if (format.EndsWith("s", StringComparison.Ordinal))
            {
                string number = format.Substring(0, format.Length - 1);
                return $"{value.ToString(number)}s";
            }

            return value.ToString(format);
        }

        private static string BuildEffectSummary(UpgradeDef def, RunState run)
        {
            return def.EffectType switch
            {
                UpgradeEffectType.AddDamage => $"伤害 +{def.Value:0.0}",
                UpgradeEffectType.AttackIntervalMultiplier => $"攻击间隔 x{def.Value:0.00}",
                UpgradeEffectType.AddRange => $"范围 +{def.Value:0.00}",
                UpgradeEffectType.AddCritChance => $"暴击率 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.AddCritDamage => $"暴伤 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.AddChainCount => $"连锁 +{Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddSplash => $"溅射半径 +{def.Value:0.00}",
                UpgradeEffectType.AddGoldMultiplier => $"金币收益 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.AddExpMultiplier => $"经验收益 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.UnlockAutoHammer => $"解锁自动锤 ({def.Value:0.00}s)",
                UpgradeEffectType.AutoHammerIntervalMultiplier => $"自动锤间隔 x{def.Value:0.00}",
                UpgradeEffectType.UnlockAutoAim => "解锁自动瞄准",
                UpgradeEffectType.AddDroneCount => $"无人机 +{Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddMagnetRadius => $"磁吸范围 +{def.Value:0.00}",
                UpgradeEffectType.AddMaxDurability => $"耐久上限 +{Mathf.RoundToInt(def.Value)}",
                UpgradeEffectType.AddBossDamageMultiplier => $"Boss伤害 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.DeployAutoHammerTower => "部署自动锤塔",
                UpgradeEffectType.DeploySensorHammer => "部署感应雷锤",
                UpgradeEffectType.DeployGoldMagnet => "部署金币吸附器",
                UpgradeEffectType.DeployBountyMarker => "部署赏金标记器",
                UpgradeEffectType.FacilityCooldownMultiplier => $"设施冷却倍率 x{def.Value:0.00}",
                UpgradeEffectType.FacilityPowerMultiplier => $"设施强度 +{def.Value * 100f:0.#}%",
                UpgradeEffectType.FacilityOverloadThresholdMultiplier => $"超载阈值 x{def.Value:0.00}",
                UpgradeEffectType.FacilityGoldMultiplier => $"设施收益 +{def.Value * 100f:0.#}%",
                _ => $"效果值 {def.Value:0.##}",
            };
        }
    }
}
