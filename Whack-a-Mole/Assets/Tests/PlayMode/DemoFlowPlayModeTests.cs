using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MoleSurvivors.Tests.PlayMode
{
    public sealed class DemoFlowPlayModeTests
    {
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
        public IEnumerator FastForward_SpawnsBossAndEndsRun()
        {
            GameObject go = new GameObject("DemoControllerForTest");
            DemoGameController controller = go.AddComponent<DemoGameController>();
            yield return null;

            controller.FastForwardForTests(320f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.MidBossSpawned, "Mid boss should spawn around 5:00.");

            controller.FastForwardForTests(290f, 0.05f);
            Assert.IsTrue(controller.BossSpawned, "Boss should spawn at 10:00.");

            controller.FastForwardForTests(120f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.RunEnded, "Run should finish after boss phase.");
            Assert.GreaterOrEqual(controller.MetaState.TotalRuns, 1);
        }

        [UnityTest]
        public IEnumerator FastForward_ReachesRound3FacilityMilestones()
        {
            GameObject go = new GameObject("DemoControllerForRound3MilestoneTest");
            DemoGameController controller = go.AddComponent<DemoGameController>();
            yield return null;

            Assert.AreEqual(5, controller.CurrentRun.ActiveHoleCount, "Round8 should start with 5 active holes.");

            controller.FastForwardForTests(55f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.AutomationMilestoneReached, "35-45s should reach first automation relief.");
            Assert.GreaterOrEqual(controller.CurrentRun.FirstAutomationSecond, 35f, "First automation should not appear earlier than 35s target band.");
            Assert.LessOrEqual(controller.CurrentRun.FirstAutomationSecond, 45f, "First automation should appear around 40s.");
            Assert.GreaterOrEqual(controller.CurrentRun.CurrentAutomationForms, 2, "Starter automation should provide at least two automation forms.");
            Assert.GreaterOrEqual(controller.CurrentRun.ActiveHoleCount, 6, "Starter automation package should unlock at least one extra hole.");

            controller.FastForwardForTests(65f, 0.05f);
            Assert.GreaterOrEqual(controller.CurrentRun.ActiveHoleCount, 8, "By 120s active holes should expand to at least 8.");
            Assert.LessOrEqual(controller.CurrentRun.ActiveHoleCount, 10, "By 120s active holes should remain in 8-10 target band.");

            controller.FastForwardForTests(85f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.FacilityMilestoneReached, "Around 240s facilities should join combat.");
            Assert.GreaterOrEqual(controller.ActiveFacilityCount, 1, "At least one facility should be active by mid game.");

            controller.FastForwardForTests(220f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.FacilityOverdriveReached, "420s+ should enter facility high-frequency phase.");
            Assert.IsTrue(controller.CurrentRun.MidBossSpawned, "Round4 should include a mid boss timeline.");

            controller.FastForwardForTests(260f, 0.05f);
            Assert.IsTrue(controller.CurrentRun.RunEnded, "Run should complete after boss phase.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(controller.CurrentRun.BuildIdentity));
            Assert.GreaterOrEqual(controller.CurrentRun.AutomationGoldCollected, 0);
            Assert.GreaterOrEqual(controller.CurrentRun.PeakSingleIncome, 0);
            Assert.GreaterOrEqual(controller.CurrentRun.RareKillCount, 0);
            Assert.GreaterOrEqual(controller.CurrentRun.EventParticipationCount, 0);
        }

        private static void DestroyAllControllers()
        {
            DemoGameController[] controllers = Object.FindObjectsOfType<DemoGameController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                Object.Destroy(controllers[i].gameObject);
            }
        }
    }
}
