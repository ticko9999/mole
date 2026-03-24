using System.Linq;
using NUnit.Framework;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class AchievementServiceTests
    {
        [Test]
        public void Evaluate_UnlocksExpectedAchievements()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            RunState run = new RunState
            {
                TotalKills = 120,
                HighestCombo = 25,
                Gold = 2600,
                AutoKills = 110,
                CoreShards = 24,
                RunWon = true,
            };
            MetaProgressState meta = new MetaProgressState
            {
                TotalRuns = 4,
                TotalWins = 2,
            };
            if (!string.IsNullOrWhiteSpace(content.DefaultWeaponId))
            {
                meta.UnlockedWeapons.Add(content.DefaultWeaponId);
            }

            if (!string.IsNullOrWhiteSpace(content.DefaultCharacterId))
            {
                meta.UnlockedCharacters.Add(content.DefaultCharacterId);
            }

            meta.CodexEntries.AddRange(content.CodexEntries.Take(12).Select(entry => entry.Id));
            for (int i = 0; i < 12; i++)
            {
                MetaStateUtils.SetNodeLevel(meta, $"node_{i}", 1);
            }

            AchievementService service = new AchievementService();
            var unlocked = service.Evaluate(content, run, meta);

            Assert.IsTrue(unlocked.Count > 0);
            Assert.IsTrue(meta.AchievementIds.Count > 0);
            Assert.IsTrue(unlocked.Any(a => a.Trigger == AchievementTrigger.BossWin));
            Assert.IsTrue(unlocked.Any(a => a.Trigger == AchievementTrigger.KillCountInRun ||
                                            a.Trigger == AchievementTrigger.GoldInRun));
        }
    }
}
