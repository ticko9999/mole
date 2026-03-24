using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class FacilityServiceTests
    {
        [Test]
        public void TryDeployFacility_PicksBestHoleAndDoesNotDuplicateType()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            RunState run = new RunState
            {
                ElapsedSeconds = 260f,
            };
            run.FacilityLevels[FacilityType.AutoHammerTower] = 1;

            List<HoleRuntime> holes = CreateHoles();
            FacilityService service = new FacilityService();

            bool firstDeploy = service.TryDeployFacility(content, run, holes, FacilityType.AutoHammerTower, out HoleRuntime deployedA);
            Assert.IsTrue(firstDeploy);
            Assert.IsNotNull(deployedA);

            run.FacilityLevels[FacilityType.AutoHammerTower] = 2;
            bool secondDeploy = service.TryDeployFacility(content, run, holes, FacilityType.AutoHammerTower, out HoleRuntime deployedB);
            Assert.IsTrue(secondDeploy);
            Assert.IsNotNull(deployedB);
            Assert.AreEqual(deployedA.Index, deployedB.Index, "Same type should stack on the existing slot.");

            int sameTypeCount = holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.AutoHammerTower);
            Assert.AreEqual(1, sameTypeCount);
            Assert.GreaterOrEqual(deployedB.Facility.Level, 2);
        }

        [Test]
        public void Tick_TriggersDamageAndCanEnterOverload()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            RunState run = new RunState
            {
                ElapsedSeconds = 320f,
                FacilityOverloadThresholdCurrent = 1,
            };
            run.FacilityLevels[FacilityType.AutoHammerTower] = 1;

            List<HoleRuntime> holes = CreateHoles();
            MoleDef mole = content.Moles.First(m => m.Rarity == Rarity.Common);
            holes[0].Spawn(mole, 1f);
            holes[0].Tick(1f, null); // move to hit window

            FacilityService service = new FacilityService();
            service.TryDeployFacility(content, run, holes, FacilityType.AutoHammerTower, out _);

            int hits = 0;
            for (int i = 0; i < 40; i++)
            {
                service.Tick(
                    content,
                    run,
                    holes,
                    0.1f,
                    () => false,
                    (_, _, _) => { hits++; },
                    (_, _) => { });
            }

            Assert.Greater(hits, 0, "Facility should produce at least one damage callback.");
            Assert.GreaterOrEqual(run.FacilityOverloadCount, 1, "Trigger accumulation should eventually enter overload.");
        }

        private static List<HoleRuntime> CreateHoles()
        {
            return new List<HoleRuntime>
            {
                new HoleRuntime(0, new Vector2(0f, 0f), 1.3f, 2, null, null, null),
                new HoleRuntime(1, new Vector2(1.8f, 0f), 1.8f, 4, null, null, null),
                new HoleRuntime(2, new Vector2(-1.8f, 0f), 1.1f, 1, null, null, null),
            };
        }
    }
}
