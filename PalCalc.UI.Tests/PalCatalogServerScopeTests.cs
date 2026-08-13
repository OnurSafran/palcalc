using System.Collections.Generic;
using System.Linq;
using PalCalc.Model;
using PalCalc.SaveReader;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.ViewModel.Inspector;

namespace PalCalc.UI.Tests;

[TestClass]
public class PalCatalogServerScopeTests
{
    [TestMethod]
    public void UnresolvedPlayerState_ClearsServerPals()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);

        var pal = palDb.Pals.First(p => p.Name == "Cattiva");

        var testSave = new CachedSaveGame(new StandardSaveGame("test_save_1"))
        {
            IsServerSave = true,
            PlayerName = "NonExistentPlayer",
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "player_1", Name = "OtherPlayer" }
            },
            Guilds = new List<GuildInstance>(),
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "inst_1", Pal = pal, OwnerPlayerId = "player_1", Gender = PalGender.MALE }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(testSave, palDb, breedingDb, GameSettings.Defaults);

        Assert.IsNotNull(catalog.ActiveScopeDescription);
        Assert.AreEqual(LocalizationCodes.LC_BREEDING_SCOPE_UNRESOLVED.Bind().Value, catalog.ActiveScopeDescription.Value);
    }

    [TestMethod]
    public void DirectPlayerFallback_IncludesOnlySelectedPlayerPals()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);

        var pal = palDb.Pals.First(p => p.Name == "Cattiva");

        var testSave = new CachedSaveGame(new StandardSaveGame("test_save_2"))
        {
            IsServerSave = true,
            PlayerName = "TestPlayer",
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "p_1", Name = "TestPlayer" },
                new PlayerInstance { PlayerId = "p_2", Name = "OtherPlayer" }
            },
            Guilds = new List<GuildInstance>(),
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "inst_1", Pal = pal, OwnerPlayerId = "p_1", Gender = PalGender.MALE },
                new PalInstance { InstanceId = "inst_2", Pal = pal, OwnerPlayerId = "p_2", Gender = PalGender.FEMALE }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(testSave, palDb, breedingDb, GameSettings.Defaults);

        var cattivaEntry = catalog.AllEntries.First(e => e.PalId == pal.Id);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.Total);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.MaleCount);
        Assert.AreEqual(0, cattivaEntry.OwnedCounts.FemaleCount);
    }

    [TestMethod]
    public void PinnedPairsRestoreFromOwnedInstanceIndex()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var cattiva = palDb.Pals.First(p => p.Name == "Cattiva");
        var chikipi = palDb.Pals.First(p => p.Name == "Chikipi");
        var save = new StandardSaveGame("test_pinned_pair_index");
        var cachedSave = new CachedSaveGame(save)
        {
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "indexed_1", Pal = cattiva, Gender = PalGender.MALE },
                new PalInstance { InstanceId = "indexed_2", Pal = chikipi, Gender = PalGender.FEMALE }
            }
        };
        var state = PalCatalogStateCache.GetState(CachedSaveGame.IdentifierFor(save));
        state.PinnedPairKeys.Clear();
        state.PinnedPairKeys.Add("indexed_1|indexed_2");

        var catalog = new PalBreedingCatalogViewModel(cachedSave, palDb, breedingDb, GameSettings.Defaults);

        Assert.HasCount(1, catalog.PinnedPairs);
        Assert.AreEqual("indexed_1|indexed_2", catalog.PinnedPairs[0].PairKey);
        catalog.CancelPendingDetails();
    }
}
