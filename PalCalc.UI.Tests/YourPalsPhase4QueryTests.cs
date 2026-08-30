using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using PalCalc.UI.ViewModel;
using PalCalc.UI.ViewModel.Mapped;
using PalCalc.UI.ViewModel.PalDerived;
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
    public void SearchIncludesFriendlyGameplayFields()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var gender = viewModel.Entries.First(entry => entry.Status == YourPalsEntryStatus.Resolved).Gender;
            viewModel.SearchText = gender;
            Assert.HasCount(2, viewModel.Entries);
            Assert.IsTrue(viewModel.Entries.All(entry => entry.Gender == gender));

            viewModel.SearchText = "Hunter";
            Assert.HasCount(1, viewModel.Entries);
            Assert.AreEqual("entry-a", viewModel.Entries[0].PalEntryKey);

            var location = viewModel.Entries.First().Location;
            viewModel.SearchText = location;
            Assert.HasCount(2, viewModel.Entries);
            Assert.IsTrue(viewModel.Entries.All(entry => entry.Location == location));
        });
    }

    [TestMethod]
    public void Phase1UsesFriendlyStatusesAndAttentionCounts()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var stale = viewModel.Entries.Single(entry => entry.PalEntryKey == "entry-stale");

            Assert.AreNotEqual(stale.Status.ToString(), stale.StatusLabel);
            Assert.IsFalse(string.IsNullOrWhiteSpace(stale.StatusExplanation));
            Assert.AreEqual(1, viewModel.Groups.Single(group => group.GroupId == "stale").AttentionCount);
            Assert.AreEqual(0, viewModel.Groups.Single(group => group.GroupId == "favorites").AttentionCount);
            StringAssert.Contains(viewModel.CollectionSummaryText, "4");
        });
    }

    [TestMethod]
    public void Phase1DistinguishesEmptyGroupsFromFilteredNoMatches()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.IsTrue(session.TryCreateGroup("Empty", out var emptyGroupId, out var error), error);
            var emptyGroup = viewModel.Groups.Single(group => group.GroupId == emptyGroupId);
            viewModel.SelectedGroupSummary = emptyGroup;

            Assert.AreEqual(0, viewModel.CurrentCollectionEntryCount);
            Assert.IsTrue(viewModel.HasEmptyCollection);
            Assert.IsFalse(viewModel.HasNoQueryMatches);

            viewModel.SelectedGroupSummary = viewModel.Groups.Single(group => group.GroupId == "favorites");
            viewModel.SearchText = "does-not-exist";

            Assert.AreEqual(2, viewModel.CurrentCollectionEntryCount);
            Assert.IsFalse(viewModel.HasEmptyCollection);
            Assert.IsTrue(viewModel.HasNoQueryMatches);
        });
    }

    [TestMethod]
    public void Phase2AddPickerUsesAnExplicitDestinationGroup()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.IsTrue(session.TryCreateGroup("New team", out var groupId, out var error), error);
            viewModel.SelectedGroupSummary = viewModel.Groups.Single(group => group.GroupId == groupId);

            viewModel.AddPalCommand.Execute(null);

            Assert.IsTrue(viewModel.IsAddPalPickerOpen);
            Assert.AreEqual(groupId, viewModel.SelectedAddGroup.GroupId);
            Assert.IsTrue(viewModel.AddPalOptions.Count > 0);

            var option = viewModel.AddPalOptions.First();
            viewModel.SelectedAddPal = option;
            Assert.IsTrue(viewModel.AddSelectedPalCommand.CanExecute(null));
            viewModel.AddSelectedPalCommand.Execute(null);

            Assert.IsFalse(viewModel.IsAddPalPickerOpen);
            Assert.HasCount(1, session.Document.Groups.Single(group => group.GroupId == groupId).Members);
            Assert.AreEqual(groupId, viewModel.SelectedEntry.GroupId);
        });
    }

    [TestMethod]
    public void Phase2ManualEditorUsesCatalogPalAndSupportedFields()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.IsTrue(session.TryCreateGroup("Manual team", out var groupId, out var error), error);
            viewModel.SelectedGroupSummary = viewModel.Groups.Single(group => group.GroupId == groupId);

            viewModel.AddPalCommand.Execute(null);
            viewModel.OpenManualEditorCommand.Execute(null);
            viewModel.SelectedManualPal = PalViewModel.All.First();
            viewModel.SelectedManualGender = CustomPalInstanceGender.Female;
            viewModel.ManualLevelText = "42";
            viewModel.ManualNickname = "Planner Pal";
            viewModel.SaveManualEditorCommand.Execute(null);

            var definition = session.Document.ManualDefinitions.Single();
            Assert.AreEqual(PalViewModel.All.First().ModelObject.InternalName, definition.RawInternalName);
            Assert.AreEqual("42", definition.RawValues["level"].ToString());
            Assert.AreEqual("FEMALE", definition.RawValues["gender"].ToString());
            Assert.AreEqual("Planner Pal", definition.RawValues["nickname"].ToString());
            Assert.IsFalse(viewModel.IsAddPalPickerOpen);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, viewModel.SelectedEntry.Status);
        });
    }

    [TestMethod]
    public void Phase2SelectingEntryOpensDismissibleDetailsState()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var entry = viewModel.Entries.First();
            viewModel.SelectedEntry = entry;

            Assert.IsTrue(viewModel.IsDetailsOpen);
            Assert.AreSame(entry, viewModel.SelectedEntry);

            viewModel.CloseDetailsCommand.Execute(null);

            Assert.IsFalse(viewModel.IsDetailsOpen);
            Assert.IsNull(viewModel.SelectedEntry);
        });
    }

    [TestMethod]
    public void Phase3AttentionReviewIsAFilteredProjectionOfSavedRows()
    {
        WithQueryFixture((session, viewModel) =>
        {
            viewModel.ReviewAttentionCommand.Execute(null);

            Assert.IsTrue(viewModel.IsAttentionReviewActive);
            Assert.AreEqual("Needs attention", viewModel.SelectedCollectionTitle);
            Assert.HasCount(2, viewModel.Entries);
            Assert.IsTrue(viewModel.Entries.All(entry => entry.Status != YourPalsEntryStatus.Resolved));

            var stale = viewModel.Entries.Single(entry => entry.Status == YourPalsEntryStatus.Stale);
            Assert.AreEqual("Find replacement", stale.AttentionActionText);
            Assert.IsTrue(viewModel.RepairEntryCommand.CanExecute(stale));
            Assert.IsFalse(viewModel.IsAllPalsSelected);
        });
    }

    [TestMethod]
    public void Phase3StaleActionRebindsTheExistingEntryWithoutDeletingIt()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var stale = viewModel.Entries.Single(entry => entry.Status == YourPalsEntryStatus.Stale);
            viewModel.RepairEntryCommand.Execute(stale);

            Assert.IsTrue(viewModel.IsRepairMode);
            Assert.IsTrue(viewModel.IsAddPalPickerOpen);
            Assert.AreEqual(stale.PalEntryKey, viewModel.SelectedEntry.PalEntryKey);
            Assert.IsTrue(viewModel.AddPalOptions.Count > 0);

            viewModel.SelectedAddPal = viewModel.AddPalOptions.First();
            viewModel.AddSelectedPalCommand.Execute(null);

            Assert.IsFalse(viewModel.IsAddPalPickerOpen);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single(member =>
                member.Member.PalEntryKey == stale.PalEntryKey).Status);
            Assert.AreEqual(4, session.Document.Groups.SelectMany(group => group.Members).Count());
        });
    }

    [TestMethod]
    public void RepairPickerDoesNotRequireASelectedDestinationGroup()
    {
        WithQueryFixture((session, viewModel) =>
        {
            var stale = viewModel.Entries.Single(entry => entry.Status == YourPalsEntryStatus.Stale);
            viewModel.RepairEntryCommand.Execute(stale);
            viewModel.SelectedAddGroup = null;
            viewModel.SelectedAddPal = viewModel.AddPalOptions.First();

            Assert.IsTrue(viewModel.AddSelectedPalCommand.CanExecute(null));
            viewModel.AddSelectedPalCommand.Execute(null);

            Assert.AreEqual(
                YourPalsEntryStatus.Resolved,
                session.ResolvedMembers.Single(member => member.Member.PalEntryKey == stale.PalEntryKey).Status);
            Assert.AreEqual("stale", viewModel.SelectedEntry.GroupId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(session.Document.Groups
                .Single(group => group.GroupId == "stale")
                .Members.Single(member => member.PalEntryKey == stale.PalEntryKey)
                .SourceKey));
        });
    }

    [TestMethod]
    public void Phase3SolverCardReportsReadyAndExcludedEntries()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.AreEqual(2, viewModel.SolverReadyCount);
            Assert.AreEqual(2, viewModel.SolverExcludedCount);
            StringAssert.Contains(viewModel.SolverSourceSummaryText, "2");
            StringAssert.Contains(viewModel.SolverSourceExcludedText, "2");

            Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.SolverSourceStateText));
        });
    }

    [TestMethod]
    public void Phase4OperationalStateLabelsAreLocalized()
    {
        WithQueryFixture((session, viewModel) =>
        {
            Assert.AreEqual("Ready", viewModel.SessionState);
            Assert.AreEqual("Available", viewModel.SourceState);

            var originalLocale = Translator.CurrentLocale;
            try
            {
                foreach (var locale in Enum.GetValues<TranslationLocale>())
                {
                    Translator.CurrentLocale = locale;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(
                        LocalizationCodes.LC_YOUR_PALS_SORT.Bind().Value));
                    Assert.IsFalse(string.IsNullOrWhiteSpace(
                        LocalizationCodes.LC_YOUR_PALS_RECOVERY_READ_ONLY.Bind().Value));
                    Assert.IsFalse(string.IsNullOrWhiteSpace(
                        LocalizationCodes.LC_YOUR_PALS_DELETE_ORPHAN_CONFIRM.Bind(new { path = "document" }).Value));
                }
            }
            finally
            {
                Translator.CurrentLocale = originalLocale;
            }
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
        resolvedA.NickName = "Hunter";
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
