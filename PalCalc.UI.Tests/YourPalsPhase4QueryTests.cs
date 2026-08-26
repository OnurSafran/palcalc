using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using PalCalc.UI.ViewModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace PalCalc.UI.Tests;

[TestClass]
public class YourPalsPhase4QueryTests
{
    [TestMethod]
    public void SearchStatusAndGroupFiltersProjectTheSameStableRows()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.AreEqual(4, viewModel.TotalEntryCount);
            Assert.AreEqual(4, viewModel.FilteredEntryCount);

            viewModel.SelectedStatusFilter = YourPalsStatusFilter.Stale;
            Assert.HasCount(1, viewModel.Entries);
            Assert.AreEqual(YourPalsEntryStatus.Stale, viewModel.Entries[0].Status);

            viewModel.ClearQueryCommand.Execute(null);
            viewModel.SelectedGroupFilter = viewModel.GroupFilterOptions
                .Single(option => option.GroupId == "favorites");
            Assert.HasCount(2, viewModel.Entries);
            Assert.IsTrue(viewModel.Entries.All(entry => entry.GroupId == "favorites"));

            viewModel.SearchText = "entry-a";
            Assert.HasCount(1, viewModel.Entries);
            Assert.AreEqual("entry-a", viewModel.Entries[0].PalEntryKey);
            Assert.IsTrue(viewModel.HasActiveQuery);
        });
    }

    [TestMethod]
    public void ClearingGroupFilterClearsTheEditingSelection()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var favorites = viewModel.GroupFilterOptions.Single(option => option.GroupId == "favorites");
            viewModel.SelectedGroupFilter = favorites;
            Assert.AreEqual("favorites", viewModel.SelectedGroupSummary.GroupId);

            viewModel.SelectedGroupFilter = viewModel.GroupFilterOptions.Single(option => option.GroupId == null);

            Assert.IsNull(viewModel.SelectedGroupSummary);
        });
    }

    [TestMethod]
    public void PalNameSortUsesLocaleComparerAndEntryKeyTieBreaker()
    {
        WithQueryFixture((session, viewModel) =>
        {
            viewModel.SelectedSortField = YourPalsSortField.PalName;
            var rowsByKey = viewModel.Entries.ToDictionary(entry => entry.PalEntryKey);
            var sourceOrder = new[] { "entry-b", "entry-a", "entry-stale", "entry-invalid" };
            var comparer = StringComparer.Create(
                CultureInfo.GetCultureInfo(
                    PalCalc.UI.Localization.Translator.CurrentLocale.ToFormalName()),
                ignoreCase: true);

            CollectionAssert.AreEqual(
                sourceOrder
                    .Select(key => rowsByKey[key])
                    .OrderBy(entry => entry.PalName, comparer)
                    .ThenBy(entry => entry.PalEntryKey, StringComparer.Ordinal)
                    .Select(entry => entry.PalEntryKey)
                    .ToArray(),
                viewModel.Entries.Select(entry => entry.PalEntryKey).ToArray());

            viewModel.ToggleSortDirectionCommand.Execute(null);

            CollectionAssert.AreEqual(
                sourceOrder
                    .Select(key => rowsByKey[key])
                    .OrderByDescending(entry => entry.PalName, comparer)
                    .ThenBy(entry => entry.PalEntryKey, StringComparer.Ordinal)
                    .Select(entry => entry.PalEntryKey)
                    .ToArray(),
                viewModel.Entries.Select(entry => entry.PalEntryKey).ToArray());
        });
    }

    [TestMethod]
    public void QueryStateSurvivesViewRecreationButIsOwnedByItsSession()
    {
        WithTemporaryDirectory(path =>
        {
            var fixture = CreateFixture(path, "save-1");
            using var firstSession = fixture.Session;
            using var firstViewModel = new YourPalsViewModel(firstSession, Dispatcher.CurrentDispatcher);
            firstViewModel.SearchText = "missing";
            firstViewModel.SelectedStatusFilter = YourPalsStatusFilter.Stale;

            firstViewModel.Dispose();
            using var recreatedViewModel = new YourPalsViewModel(firstSession, Dispatcher.CurrentDispatcher);

            Assert.AreEqual("missing", recreatedViewModel.SearchText);
            Assert.AreEqual(YourPalsStatusFilter.Stale, recreatedViewModel.SelectedStatusFilter);
            Assert.HasCount(1, recreatedViewModel.Entries);

            var secondFixture = CreateFixture(path, "save-2");
            using var secondSession = secondFixture.Session;
            using var secondViewModel = new YourPalsViewModel(secondSession, Dispatcher.CurrentDispatcher);

            Assert.AreEqual("", secondViewModel.SearchText);
            Assert.AreEqual(YourPalsStatusFilter.All, secondViewModel.SelectedStatusFilter);
            Assert.HasCount(4, secondViewModel.Entries);

        });
    }

    [TestMethod]
    public void LocaleRefreshRebuildsDisplayRowsWithoutChangingMembership()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var originalLocale = PalCalc.UI.Localization.Translator.CurrentLocale;
            try
            {
                var targetLocale = originalLocale == PalCalc.UI.Localization.TranslationLocale.en
                    ? PalCalc.UI.Localization.TranslationLocale.tr
                    : PalCalc.UI.Localization.TranslationLocale.en;
                PalCalc.UI.Localization.Translator.CurrentLocale = targetLocale;

                Assert.AreEqual(4, viewModel.TotalEntryCount);
                Assert.HasCount(4, viewModel.Entries);
                Assert.AreEqual(
                    PalCalc.UI.ViewModel.YourPalsDisplayName.For(session.SourceSnapshot.Entries[0].Record.Pal),
                    viewModel.Entries.Single(entry => entry.PalEntryKey == "entry-a").PalName);
            }
            finally
            {
                PalCalc.UI.Localization.Translator.CurrentLocale = originalLocale;
            }
        }, new Pal
        {
            Name = "Zulu",
            InternalName = "TestPal",
            LocalizedNames = new Dictionary<string, string>
            {
                ["en"] = "Zulu",
                ["tr"] = "Alfa",
            },
        });
    }

    [TestMethod]
    public void LargeCollectionQueryProjectionRemainsTableSized()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("large-save");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, "large", YourPalsContract.DocumentFileName));
            var pal = PalDB.LoadEmbedded().Pals.First();
            var sourcePals = Enumerable.Range(0, 2500)
                .Select(index => SourcePal(pal, $"instance-{index}"))
                .ToArray();
            var members = sourcePals
                .Select((source, index) => YourPalsMember.Imported(
                    new ImportedPalReference
                    {
                        SourceIdentity = SourceIdentity.ForSave(owner),
                        SourceKey = $"Palbox:box-1:{index}",
                        InstanceId = source.InstanceId,
                        LastKnownInternalName = pal.InternalName,
                    },
                    $"entry-{index:D4}"))
                .ToList();
            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "large-group",
                        Name = "Large group",
                        Members = members,
                    },
                ],
            };
            store.Save(store.CreateNew(owner), document);

            using var session = new SavePalsSession(save, null, Cached(owner, sourcePals), store);
            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);

            Assert.AreEqual(2500, viewModel.TotalEntryCount);
            viewModel.SearchText = "entry-2499";
            Assert.HasCount(1, viewModel.Entries);
            Assert.AreEqual("entry-2499", viewModel.Entries[0].PalEntryKey);
        });
    }

    private static void WithQueryFixture(Action<SavePalsSession, YourPalsViewModel> action, Pal? pal = null)
    {
        WithTemporaryDirectory(path =>
        {
            var fixture = CreateFixture(path, "save-1", pal);
            using var session = fixture.Session;
            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);
            action(session, viewModel);
        });
    }

    private static QueryFixture CreateFixture(string rootPath, string gameId, Pal? pal = null)
    {
        var save = FakeSaveGame.Create(gameId);
        var owner = SaveIdentity.From(save);
        var store = new YourPalsDocumentStore(
            Path.Combine(rootPath, gameId, YourPalsContract.DocumentFileName));
        pal ??= PalDB.LoadEmbedded().Pals.First();
        var resolvedA = SourcePal(pal, "instance-a");
        var resolvedB = SourcePal(pal, "instance-b");
        var sourceIdentity = SourceIdentity.ForSave(owner);
        var document = new YourPalsDocument
        {
            OwnerSaveIdentity = owner,
            Groups =
            [
                new YourPalsGroup
                {
                    GroupId = "favorites",
                    Name = "Favorites",
                    Order = 0,
                    Members =
                    [
                        YourPalsMember.Imported(Imported(sourceIdentity, resolvedB), "entry-b"),
                        YourPalsMember.Imported(Imported(sourceIdentity, resolvedA), "entry-a"),
                    ],
                },
                new YourPalsGroup
                {
                    GroupId = "stale",
                    Name = "Stale",
                    Order = 1,
                    Members =
                    [YourPalsMember.Imported(new ImportedPalReference
                    {
                        SourceIdentity = sourceIdentity,
                        SourceKey = "Palbox:box-1:missing",
                        InstanceId = "missing-instance",
                        LastKnownInternalName = "MissingPal",
                    }, "entry-stale")],
                },
                new YourPalsGroup
                {
                    GroupId = "invalid",
                    Name = "Invalid",
                    Order = 2,
                    Members =
                    [new YourPalsMember
                    {
                        PalEntryKey = "entry-invalid",
                        Kind = "future-member-kind",
                    }],
                },
            ],
        };
            store.Save(store.CreateNew(owner), document);

        return new QueryFixture(
            new SavePalsSession(
                save,
                null,
                Cached(owner, resolvedA, resolvedB),
                store));
    }

    private static ImportedPalReference Imported(SourceIdentity sourceIdentity, PalInstance source) => new()
    {
        SourceIdentity = sourceIdentity,
        SourceKey = $"Palbox:box-1:{source.InstanceId[^1]}",
        InstanceId = source.InstanceId,
        LastKnownInternalName = source.Pal.InternalName,
    };

    private static CachedSaveGame Cached(SaveIdentity owner, params PalInstance[] pals)
    {
        var save = FakeSaveGame.Create(owner.GameId);
        return new CachedSaveGame(save)
        {
            OwnedPals = pals.ToList(),
            Players = [],
            Guilds = [],
            Bases = [],
            PalContainers = [],
        };
    }

    private static PalInstance SourcePal(Pal pal, string instanceId) => new()
    {
        Pal = pal,
        InstanceId = instanceId,
        Gender = PalGender.MALE,
        Location = new PalLocation
        {
            Type = LocationType.Palbox,
            ContainerId = "box-1",
            Index = 0,
        },
        PassiveSkills = [],
        ActiveSkills = [],
        EquippedActiveSkills = [],
    };

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-phase4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private sealed record QueryFixture(SavePalsSession Session);
}
