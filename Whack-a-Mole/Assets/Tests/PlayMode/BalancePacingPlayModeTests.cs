using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MoleSurvivors.Tests.PlayMode
{
    public sealed class BalancePacingPlayModeTests
    {
        private const int SimulationRuns = 12;
        private const float MaxSimulationSeconds = 540f;
        private const float ChunkSeconds = 1f;
        private const float TickStep = 0.05f;
        private const int SeedBase = 64000;

        [Serializable]
        private sealed class RunMetric
        {
            public int Index;
            public int Seed;
            public float FirstUpgradeSecond = -1f;
            public float ReliefSecond = -1f;
            public float FunSecond = -1f;
            public float FullAutomationSecond = -1f;
            public float EndSecond;
            public bool RunEnded;
            public int FinalLevel;
            public int Kills;
            public int UpgradePicks;
            public int ActiveFacilities;
        }

        private sealed class AggregateSummary
        {
            public int RunCount;
            public float AvgFirstUpgradeSecond;
            public float AvgReliefSecond;
            public float AvgFunSecond;
            public float AvgFullAutomationSecond;
            public float P75ReliefSecond;
            public float P75FunSecond;
            public float ReliefMissRate;
            public float FunMissRate;
            public float FullAutomationMissRate;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DemoBootstrapper.DisableAutoBootstrapForTests = true;
            DestroyAllControllers();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyAllControllers();
            DemoBootstrapper.DisableAutoBootstrapForTests = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator MonteCarlo_BalancePacing_ReportAndGuardrails()
        {
            List<RunMetric> runs = new List<RunMetric>(SimulationRuns);
            for (int i = 0; i < SimulationRuns; i++)
            {
                int seed = SeedBase + i * 17;
                RunMetric metric = new RunMetric
                {
                    Index = i,
                    Seed = seed,
                };

                GameObject go = new GameObject($"BalancePacingRun_{i:D2}");
                DemoGameController controller = go.AddComponent<DemoGameController>();
                yield return null;
                controller.SetRandomSeedForTests(seed);

                while (!controller.CurrentRun.RunEnded &&
                       controller.CurrentRun.ElapsedSeconds < MaxSimulationSeconds)
                {
                    controller.FastForwardForTests(ChunkSeconds, TickStep);
                    CaptureMetric(controller, metric);
                }

                CaptureMetric(controller, metric);
                metric.EndSecond = controller.CurrentRun.ElapsedSeconds;
                metric.RunEnded = controller.CurrentRun.RunEnded;
                metric.FinalLevel = controller.CurrentRun.Level;
                metric.Kills = controller.CurrentRun.TotalKills;
                metric.UpgradePicks = GetTotalUpgradePicks(controller.CurrentRun);
                metric.ActiveFacilities = controller.ActiveFacilityCount;
                runs.Add(metric);

                UnityEngine.Object.Destroy(go);
                yield return null;
            }

            AggregateSummary summary = BuildSummary(runs);
            string reportPath = WriteReport(runs, summary);
            Debug.Log(
                $"[MoleSurvivors][BalancePacing] runs={summary.RunCount}, " +
                $"avg_first_upgrade={summary.AvgFirstUpgradeSecond:0.0}s, " +
                $"avg_relief={summary.AvgReliefSecond:0.0}s, p75_relief={summary.P75ReliefSecond:0.0}s, " +
                $"avg_fun={summary.AvgFunSecond:0.0}s, p75_fun={summary.P75FunSecond:0.0}s, " +
                $"avg_full={summary.AvgFullAutomationSecond:0.0}s, " +
                $"miss_relief={summary.ReliefMissRate * 100f:0.#}%, " +
                $"miss_fun={summary.FunMissRate * 100f:0.#}%, " +
                $"miss_full={summary.FullAutomationMissRate * 100f:0.#}%, " +
                $"report={reportPath}");

            Assert.GreaterOrEqual(summary.RunCount, 6, "Simulation sample size is too small.");
            Assert.LessOrEqual(summary.AvgFirstUpgradeSecond, 95f, "首个升级平均出现过慢（>95s）。");
            Assert.LessOrEqual(summary.P75ReliefSecond, 130f, "自动化减负 P75 过慢（>130s）。");
            Assert.LessOrEqual(summary.ReliefMissRate, 0.15f, "自动化减负缺失比例过高（>15%）。");
            Assert.LessOrEqual(summary.P75FunSecond, 260f, "开始爽点 P75 过慢（>260s）。");
        }

        private static void CaptureMetric(DemoGameController controller, RunMetric metric)
        {
            RunState run = controller.CurrentRun;
            if (run == null || run.Stats == null)
            {
                return;
            }

            float t = run.ElapsedSeconds;
            int picks = GetTotalUpgradePicks(run);
            int automationScore = ComputeAutomationScore(run, controller.ActiveFacilityCount);

            if (metric.FirstUpgradeSecond < 0f && picks > 0)
            {
                metric.FirstUpgradeSecond = t;
            }

            if (metric.ReliefSecond < 0f && HasRelief(run, controller.ActiveFacilityCount))
            {
                metric.ReliefSecond = t;
            }

            if (metric.FunSecond < 0f && automationScore >= 2)
            {
                metric.FunSecond = t;
            }

            if (metric.FullAutomationSecond < 0f && automationScore >= 5)
            {
                metric.FullAutomationSecond = t;
            }
        }

        private static bool HasRelief(RunState run, int activeFacilityCount)
        {
            if (run == null || run.Stats == null)
            {
                return false;
            }

            return run.AutomationMilestoneReached ||
                   run.Stats.AutoHammerInterval > 0f ||
                   run.Stats.DroneCount > 0 ||
                   activeFacilityCount > 0;
        }

        private static int ComputeAutomationScore(RunState run, int activeFacilityCount)
        {
            if (run == null || run.Stats == null)
            {
                return 0;
            }

            int score = 0;
            if (run.Stats.AutoHammerInterval > 0f)
            {
                score += 1;
                if (run.Stats.AutoHammerInterval <= 1.1f)
                {
                    score += 1;
                }
            }

            if (run.Stats.AutoAim)
            {
                score += 1;
            }

            if (run.Stats.DroneCount > 0)
            {
                score += 2;
            }

            if (activeFacilityCount >= 1)
            {
                score += 1;
            }

            if (activeFacilityCount >= 3)
            {
                score += 1;
            }

            if (run.FacilityOverdriveReached)
            {
                score += 1;
            }

            return score;
        }

        private static int GetTotalUpgradePicks(RunState run)
        {
            if (run == null || run.UpgradeStacks == null)
            {
                return 0;
            }

            int total = 0;
            foreach (KeyValuePair<string, int> pair in run.UpgradeStacks)
            {
                total += Mathf.Max(0, pair.Value);
            }

            return total;
        }

        private static AggregateSummary BuildSummary(List<RunMetric> runs)
        {
            AggregateSummary summary = new AggregateSummary
            {
                RunCount = runs.Count,
            };

            summary.AvgFirstUpgradeSecond = AverageWithPenalty(runs, metric => metric.FirstUpgradeSecond);
            summary.AvgReliefSecond = AverageWithPenalty(runs, metric => metric.ReliefSecond);
            summary.AvgFunSecond = AverageWithPenalty(runs, metric => metric.FunSecond);
            summary.AvgFullAutomationSecond = AverageWithPenalty(runs, metric => metric.FullAutomationSecond);

            summary.P75ReliefSecond = PercentileWithPenalty(runs, metric => metric.ReliefSecond, 0.75f);
            summary.P75FunSecond = PercentileWithPenalty(runs, metric => metric.FunSecond, 0.75f);

            summary.ReliefMissRate = MissRate(runs, metric => metric.ReliefSecond);
            summary.FunMissRate = MissRate(runs, metric => metric.FunSecond);
            summary.FullAutomationMissRate = MissRate(runs, metric => metric.FullAutomationSecond);

            return summary;
        }

        private static float MissRate(List<RunMetric> runs, Func<RunMetric, float> selector)
        {
            if (runs.Count == 0)
            {
                return 1f;
            }

            int miss = runs.Count(metric => selector(metric) < 0f);
            return miss / (float)runs.Count;
        }

        private static float AverageWithPenalty(List<RunMetric> runs, Func<RunMetric, float> selector)
        {
            if (runs.Count == 0)
            {
                return float.PositiveInfinity;
            }

            float total = 0f;
            for (int i = 0; i < runs.Count; i++)
            {
                float value = selector(runs[i]);
                total += value >= 0f ? value : MaxSimulationSeconds + 1f;
            }

            return total / runs.Count;
        }

        private static float PercentileWithPenalty(List<RunMetric> runs, Func<RunMetric, float> selector, float percentile)
        {
            if (runs.Count == 0)
            {
                return float.PositiveInfinity;
            }

            List<float> values = runs
                .Select(metric => selector(metric) >= 0f ? selector(metric) : MaxSimulationSeconds + 1f)
                .OrderBy(value => value)
                .ToList();
            int index = Mathf.Clamp(Mathf.CeilToInt(percentile * values.Count) - 1, 0, values.Count - 1);
            return values[index];
        }

        private static string WriteReport(List<RunMetric> runs, AggregateSummary summary)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(Path.GetTempPath(), $"mole_balance_pacing_{stamp}.csv");
            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("metric,value");
            sb.AppendLine($"run_count,{summary.RunCount}");
            sb.AppendLine($"avg_first_upgrade_sec,{summary.AvgFirstUpgradeSecond:0.000}");
            sb.AppendLine($"avg_relief_sec,{summary.AvgReliefSecond:0.000}");
            sb.AppendLine($"avg_fun_sec,{summary.AvgFunSecond:0.000}");
            sb.AppendLine($"avg_full_automation_sec,{summary.AvgFullAutomationSecond:0.000}");
            sb.AppendLine($"p75_relief_sec,{summary.P75ReliefSecond:0.000}");
            sb.AppendLine($"p75_fun_sec,{summary.P75FunSecond:0.000}");
            sb.AppendLine($"relief_miss_rate,{summary.ReliefMissRate:0.000}");
            sb.AppendLine($"fun_miss_rate,{summary.FunMissRate:0.000}");
            sb.AppendLine($"full_automation_miss_rate,{summary.FullAutomationMissRate:0.000}");
            sb.AppendLine();
            sb.AppendLine("run_index,seed,first_upgrade_sec,relief_sec,fun_sec,full_automation_sec,end_sec,run_ended,final_level,kills,upgrade_picks,active_facilities");
            for (int i = 0; i < runs.Count; i++)
            {
                RunMetric run = runs[i];
                sb.Append(run.Index).Append(',')
                    .Append(run.Seed).Append(',')
                    .Append(FormatMetric(run.FirstUpgradeSecond)).Append(',')
                    .Append(FormatMetric(run.ReliefSecond)).Append(',')
                    .Append(FormatMetric(run.FunSecond)).Append(',')
                    .Append(FormatMetric(run.FullAutomationSecond)).Append(',')
                    .Append(run.EndSecond.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                    .Append(run.RunEnded ? "1" : "0").Append(',')
                    .Append(run.FinalLevel).Append(',')
                    .Append(run.Kills).Append(',')
                    .Append(run.UpgradePicks).Append(',')
                    .Append(run.ActiveFacilities)
                    .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string FormatMetric(float value)
        {
            return value >= 0f
                ? value.ToString("0.000", CultureInfo.InvariantCulture)
                : "NA";
        }

        private static void DestroyAllControllers()
        {
            DemoGameController[] controllers = UnityEngine.Object.FindObjectsOfType<DemoGameController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                UnityEngine.Object.Destroy(controllers[i].gameObject);
            }
        }
    }
}
