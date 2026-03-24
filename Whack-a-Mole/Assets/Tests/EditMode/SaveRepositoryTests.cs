using System;
using System.IO;
using NUnit.Framework;

namespace MoleSurvivors.Tests.EditMode
{
    public sealed class SaveRepositoryTests
    {
        [Test]
        public void SaveAndLoad_RoundTripsAndKeepsDefaults()
        {
            string path = Path.Combine(Path.GetTempPath(), $"ms-save-{Guid.NewGuid()}.json");
            try
            {
                JsonSaveRepository repo = new JsonSaveRepository(
                    path,
                    defaultUnlockedWeapons: new[] { "WPN_TEST_A" },
                    defaultUnlockedCharacters: new[] { "ROLE_TEST_A" },
                    defaultWeaponId: "WPN_TEST_A",
                    defaultCharacterId: "ROLE_TEST_A");
                MetaProgressState state = repo.LoadOrCreate();
                state.WorkshopChips = 123;
                state.ActiveWeaponId = "WPN_TEST_A";
                repo.Save(state);

                MetaProgressState loaded = repo.LoadOrCreate();
                Assert.AreEqual(123, loaded.WorkshopChips);
                Assert.Contains("WPN_TEST_A", loaded.UnlockedWeapons);
                Assert.Contains("ROLE_TEST_A", loaded.UnlockedCharacters);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void LoadOrCreate_MigratesBrokenOrOldEnvelope()
        {
            string path = Path.Combine(Path.GetTempPath(), $"ms-save-{Guid.NewGuid()}.json");
            try
            {
                File.WriteAllText(path, "{\"Version\":0,\"Meta\":{\"SaveVersion\":0}}");
                JsonSaveRepository repo = new JsonSaveRepository(
                    path,
                    defaultUnlockedWeapons: new[] { "WPN_DEFAULT" },
                    defaultUnlockedCharacters: new[] { "ROLE_DEFAULT" },
                    defaultWeaponId: "WPN_DEFAULT",
                    defaultCharacterId: "ROLE_DEFAULT");
                MetaProgressState loaded = repo.LoadOrCreate();

                Assert.Contains("WPN_DEFAULT", loaded.UnlockedWeapons);
                Assert.Contains("ROLE_DEFAULT", loaded.UnlockedCharacters);
                Assert.IsFalse(string.IsNullOrWhiteSpace(loaded.ActiveWeaponId));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
