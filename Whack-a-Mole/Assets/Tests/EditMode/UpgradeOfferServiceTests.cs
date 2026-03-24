using System;
using System.Linq;
using NUnit.Framework;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class UpgradeOfferServiceTests
    {
        [Test]
        public void BuildOffer_ReturnsThreeUniqueAndAtLeastOneUsefulOption()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            string weaponId = !string.IsNullOrWhiteSpace(content.DefaultWeaponId)
                ? content.DefaultWeaponId
                : content.Weapons.First().Id;
            RunState run = new RunState
            {
                ElapsedSeconds = 180f,
                WeaponId = weaponId,
            };
            run.BuildTags.Add("Range");
            run.BuildTags.Add("Crit");

            UpgradeOfferService service = new UpgradeOfferService();
            var offer = service.BuildOffer(content, run, new Random(42));

            Assert.AreEqual(3, offer.Count);
            Assert.AreEqual(3, offer.Select(o => o.Id).Distinct().Count());
            Assert.IsTrue(offer.Any(o => o.Tags.Contains("Range") || o.Tags.Contains("Crit") || o.Tags.Contains("Damage")));
        }

        [Test]
        public void BuildOffer_FacilityTagsBecomeFrequentInMidLateGame()
        {
            GameContent content = DefaultContentFactory.CreateDefault();
            UpgradeOfferService service = new UpgradeOfferService();

            int facilityOfferCount = 0;
            for (int i = 0; i < 16; i++)
            {
                string automationWeapon = content.Weapons
                    .FirstOrDefault(w => w != null && (w.DroneCount > 0 || w.AutoHammerInterval > 0f))
                    ?.Id ?? content.Weapons.First().Id;
                RunState run = new RunState
                {
                    ElapsedSeconds = 280f,
                    WeaponId = automationWeapon,
                    ActiveFacilityCount = 1,
                };
                run.BuildTags.Add("Facility");
                run.BuildTags.Add("Automation");
                run.FacilityLevels[FacilityType.AutoHammerTower] = 1;

                var offer = service.BuildOffer(content, run, new Random(200 + i));
                if (offer.Any(o => o.Tags.Contains("Facility")))
                {
                    facilityOfferCount++;
                }
            }

            Assert.GreaterOrEqual(facilityOfferCount, 8, "Facility-oriented offers should appear frequently after mid game.");
        }
    }
}
