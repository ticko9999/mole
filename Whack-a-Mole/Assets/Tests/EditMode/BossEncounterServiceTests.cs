using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class BossEncounterServiceTests
    {
        [Test]
        public void CreateTimeline_BuildsMidAndFinalBossEncounters()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            BossEncounterService service = new BossEncounterService();

            List<BossEncounterRuntime> timeline = service.CreateTimeline(content);
            Assert.GreaterOrEqual(timeline.Count, 2);
            Assert.IsTrue(timeline.Any(e => e.Def != null && !e.Def.IsFinalBoss));
            Assert.IsTrue(timeline.Any(e => e.Def != null && e.Def.IsFinalBoss));
            Assert.LessOrEqual(timeline[0].Def.SpawnAtSecond, timeline[timeline.Count - 1].Def.SpawnAtSecond);
        }

        [Test]
        public void TickActiveEncounter_TriggersShieldAndRogueBursts()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            BossEncounterService service = new BossEncounterService();
            List<BossEncounterRuntime> timeline = service.CreateTimeline(content);
            BossEncounterRuntime mid = timeline.First(e => e.Def != null && !e.Def.IsFinalBoss);
            mid.Spawned = true;

            RunState run = new RunState
            {
                ElapsedSeconds = 320f,
            };
            List<HoleRuntime> holes = new List<HoleRuntime>
            {
                new HoleRuntime(0, new Vector2(-1f, 0f), 1.2f, 3, null, null, null),
                new HoleRuntime(1, new Vector2(0f, 0f), 1.5f, 4, null, null, null),
                new HoleRuntime(2, new Vector2(1f, 0f), 1.1f, 2, null, null, null),
                new HoleRuntime(3, new Vector2(2f, 0f), 1.7f, 4, null, null, null),
            };

            int rogueBursts = 0;
            int shieldToggles = 0;
            for (int i = 0; i < 200; i++)
            {
                service.TickActiveEncounter(
                    run,
                    mid,
                    0.1f,
                    holes,
                    (_, __) => rogueBursts++,
                    _ => shieldToggles++);
            }

            Assert.Greater(rogueBursts, 0);
            Assert.Greater(shieldToggles, 0);
            Assert.Greater(run.RogueZoneBurstCount, 0);
        }
    }
}
