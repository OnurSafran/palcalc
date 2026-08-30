using System.Collections.Generic;
using System.Linq;
using PalCalc.Model;
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

        var testSave = new CachedSaveGame(FakeSaveGame.Create("test_save_1"))
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

        var testSave = new CachedSaveGame(FakeSaveGame.Create("test_save_2"))
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
    public void SinglePlayerScope_UsesAllOwnedPalsAndKeepsExpeditionPairVisible()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var cattiva = palDb.Pals.First(p => p.Name == "Cattiva");
        var chikipi = palDb.Pals.First(p => p.Name == "Chikipi");
        var recipe = breedingDb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
        var save = FakeSaveGame.Create("test_singleplayer_scope");
        var cachedSave = new CachedSaveGame(save)
        {
            IsServerSave = false,
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "single_cat", Pal = cattiva, OwnerPlayerId = "player_a", Gender = PalGender.MALE, IsOnExpedition = true },
                new PalInstance { InstanceId = "single_chik", Pal = chikipi, OwnerPlayerId = "player_b", Gender = PalGender.FEMALE, IsOnExpedition = true }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(cachedSave, palDb, breedingDb, GameSettings.Defaults);

        var childEntry = catalog.AllEntries.First(e => e.PalId == recipe.Child.Id);
        Assert.AreEqual(LocalizationCodes.LC_BREEDING_SCOPE_SINGLE_PLAYER.Bind().Value, catalog.ActiveScopeDescription.Value);
        Assert.AreEqual(PalBreedingStatus.Ready, childEntry.Status);
        Assert.IsTrue(childEntry.HasOnlyExpeditionMatchingPair);
        Assert.Contains(childEntry, catalog.VisibleEntries);
        catalog.SelectedFilter = PalCatalogFilterOption.BreedableNow;
        Assert.Contains(childEntry, catalog.VisibleEntries);
        catalog.CancelPendingDetails();
    }

    [TestMethod]
    public void ServerScope_UnknownServerPlayerNameFallsBackToFirstParsedPlayer()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var pal = palDb.Pals.First(p => p.Name == "Cattiva");

        var testSave = new CachedSaveGame(FakeSaveGame.Create("test_save_server_default_player"))
        {
            IsServerSave = true,
            PlayerName = "UNKNOWN",
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "p_1", Name = "FirstPlayer" },
                new PlayerInstance { PlayerId = "p_2", Name = "OtherPlayer" }
            },
            Guilds = new List<GuildInstance>(),
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "first_player_pal", Pal = pal, OwnerPlayerId = "p_1", Gender = PalGender.MALE },
                new PalInstance { InstanceId = "other_player_pal", Pal = pal, OwnerPlayerId = "p_2", Gender = PalGender.FEMALE }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(testSave, palDb, breedingDb, GameSettings.Defaults);

        var cattivaEntry = catalog.AllEntries.First(e => e.PalId == pal.Id);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.Total);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.MaleCount);
        Assert.AreEqual(LocalizationCodes.LC_BREEDING_SCOPE_PLAYER.Bind(new { Name = "FirstPlayer" }).Value, catalog.ActiveScopeDescription.Value);
    }

    [TestMethod]
    public void PinnedPairs_IgnoreMalformedOwnedRecords()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var pal = palDb.Pals.First(p => p.Name == "Cattiva");
        var save = FakeSaveGame.Create("test_pinned_malformed_record");
        var cachedSave = new CachedSaveGame(save)
        {
            OwnedPals = new List<PalInstance>
            {
                new PalInstance { InstanceId = "malformed", Pal = null, Gender = PalGender.MALE },
                new PalInstance { InstanceId = "valid", Pal = pal, Gender = PalGender.FEMALE }
            }
        };
        var state = PalCatalogStateCache.GetState(CachedSaveGame.IdentifierFor(save));
        state.PinnedPairKeys.Clear();
        state.PinnedPairKeys.Add("malformed|valid");

        var catalog = new PalBreedingCatalogViewModel(cachedSave, palDb, breedingDb, GameSettings.Defaults);

        Assert.HasCount(0, catalog.PinnedPairs);
        catalog.CancelPendingDetails();
    }

    [TestMethod]
    public void DirectPlayerFallback_IncludesSelectedPlayersGlobalPalStorage()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var pal = palDb.Pals.First(p => p.Name == "Cattiva");

        var testSave = new CachedSaveGame(FakeSaveGame.Create("test_save_global_player"))
        {
            IsServerSave = true,
            PlayerName = "TestPlayer",
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "p_1", Name = "TestPlayer" },
                new PlayerInstance { PlayerId = "p_2", Name = "OtherPlayer" }
            },
            Guilds = new List<GuildInstance>(),
            PalContainers = new List<IPalContainer>
            {
                new GlobalPalStorageContainer { Id = "global", PlayerId = "p_1" }
            },
            OwnedPals = new List<PalInstance>
            {
                new PalInstance
                {
                    InstanceId = "global_1",
                    Pal = pal,
                    Gender = PalGender.MALE,
                    Location = new PalLocation { ContainerId = "global", Type = LocationType.GlobalPalStorage }
                },
                new PalInstance
                {
                    InstanceId = "global_2",
                    Pal = pal,
                    OwnerPlayerId = "p_2",
                    Gender = PalGender.FEMALE
                }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(testSave, palDb, breedingDb, GameSettings.Defaults);

        var cattivaEntry = catalog.AllEntries.First(e => e.PalId == pal.Id);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.Total);
        Assert.AreEqual(1, cattivaEntry.OwnedCounts.MaleCount);
    }

    [TestMethod]
    public void GuildScope_IncludesGlobalPalStorageOwnedByGuild()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var pal = palDb.Pals.First(p => p.Name == "Cattiva");

        var testSave = new CachedSaveGame(FakeSaveGame.Create("test_save_global_guild"))
        {
            IsServerSave = true,
            PlayerName = "TestPlayer",
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "p_1", Name = "TestPlayer" },
                new PlayerInstance { PlayerId = "p_2", Name = "OtherPlayer" }
            },
            Guilds = new List<GuildInstance>
            {
                new GuildInstance { Id = "guild_1", Name = "Guild One", MemberIds = new List<string> { "p_1" } },
                new GuildInstance { Id = "guild_2", Name = "Guild Two", MemberIds = new List<string> { "p_2" } }
            },
            PalContainers = new List<IPalContainer>
            {
                new GlobalPalStorageContainer { Id = "global", PlayerId = "p_1" }
            },
            OwnedPals = new List<PalInstance>
            {
                new PalInstance
                {
                    InstanceId = "global_guild_1",
                    Pal = pal,
                    Gender = PalGender.MALE,
                    Location = new PalLocation { ContainerId = "global", Type = LocationType.GlobalPalStorage }
                }
            }
        };

        var catalog = new PalBreedingCatalogViewModel(testSave, palDb, breedingDb, GameSettings.Defaults);

        Assert.AreEqual(1, catalog.AllEntries.First(e => e.PalId == pal.Id).OwnedCounts.Total);
    }

    [TestMethod]
    public void CachedSaveCopyFrom_InvalidatesContainerGuildScope()
    {
        var save = FakeSaveGame.Create("test_scope_cache_reload");
        var original = new CachedSaveGame(save)
        {
            Players = new List<PlayerInstance>
            {
                new PlayerInstance { PlayerId = "p_1", Name = "TestPlayer" }
            },
            Guilds = new List<GuildInstance>
            {
                new GuildInstance { Id = "guild_1", MemberIds = new List<string> { "p_1" } }
            },
            Bases = new List<BaseInstance>
            {
                new BaseInstance { Id = "base", OwnerGuildId = "guild_1" }
            },
            PalContainers = new List<IPalContainer>
            {
                new BasePalContainer { Id = "container", BaseId = "base" }
            },
            OwnedPals = new List<PalInstance>()
        };
        Assert.AreEqual("guild_1", original.GuildsByContainerId["container"].Id);

        var updated = new CachedSaveGame(save)
        {
            Players = original.Players,
            Guilds = new List<GuildInstance>
            {
                new GuildInstance { Id = "guild_2", MemberIds = new List<string> { "p_1" } }
            },
            Bases = new List<BaseInstance>
            {
                new BaseInstance { Id = "base", OwnerGuildId = "guild_2" }
            },
            PalContainers = new List<IPalContainer>
            {
                new BasePalContainer { Id = "container", BaseId = "base" }
            },
            OwnedPals = new List<PalInstance>()
        };

        original.CopyFrom(updated);

        Assert.AreEqual("guild_2", original.GuildsByContainerId["container"].Id);
    }

    [TestMethod]
    public void PinnedPairsRestoreFromOwnedInstanceIndex()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var cattiva = palDb.Pals.First(p => p.Name == "Cattiva");
        var chikipi = palDb.Pals.First(p => p.Name == "Chikipi");
        var save = FakeSaveGame.Create("test_pinned_pair_index");
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
        Assert.AreEqual(
            PalBreedingPairViewModel.MakePairKey(cachedSave.OwnedPals[0], cachedSave.OwnedPals[1]),
            catalog.PinnedPairs[0].PairKey);
        catalog.CancelPendingDetails();
    }
}
