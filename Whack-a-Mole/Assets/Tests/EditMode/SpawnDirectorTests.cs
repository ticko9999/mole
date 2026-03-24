using System;
using NUnit.Framework;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class SpawnDirectorTests
    {
        [Test]
        public void SelectMoleForBudget_RespectsBudgetConstraint()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            Random random = new Random(7);

            for (int i = 0; i < 30; i++)
            {
                float elapsed = i * 18f;
                MoleDef selected = SpawnDirector.SelectMoleForBudget(content.Moles, elapsed, 2.2f, random);
                if (selected != null)
                {
                    Assert.LessOrEqual(selected.ThreatCost, 2.4f);
                }
            }
        }

        [Test]
        public void AccumulateThreat_IncreasesOverTime()
        {
            float start = 0f;
            float afterEarly = SpawnDirector.AccumulateThreat(start, 10f, 1f, 1f);
            float afterLate = SpawnDirector.AccumulateThreat(start, 300f, 1f, 1f);

            Assert.Greater(afterEarly, start);
            Assert.Greater(afterLate, afterEarly);
        }

        [Test]
        public void SelectMoleForBudget_RareWeightMultiplierBiasesTowardRareMoles()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            float rarityScoreBase = 0f;
            float rarityScoreBiased = 0f;
            int samples = 320;

            for (int i = 0; i < samples; i++)
            {
                MoleDef basePick = SpawnDirector.SelectMoleForBudget(content.Moles, 320f, 4.7f, new Random(1000 + i), 1f);
                MoleDef biasedPick = SpawnDirector.SelectMoleForBudget(content.Moles, 320f, 4.7f, new Random(2000 + i), 2.4f);
                rarityScoreBase += basePick != null ? (int)basePick.Rarity : 0f;
                rarityScoreBiased += biasedPick != null ? (int)biasedPick.Rarity : 0f;
            }

            Assert.Greater(rarityScoreBiased, rarityScoreBase, "Higher rare weight should produce more high-rarity picks.");
        }
    }
}
