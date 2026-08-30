using PalCalc.Model;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using PalCalc.UI.ViewModel;
using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace PalCalc.UI.Tests
{
    [TestClass]
    public class YourPalsPhase3ReadOnlyTests
    {
        [TestMethod]
        public void ReadProjectionShowsGroupsSourceDetailsAndStableSelectionAfterRefresh()
        {
            WithTemporaryDirectory(path =>
            {
                var save = FakeSaveGame.Create("save-1");
                var owner = SaveIdentity.From(save);
                var store = new YourPalsDocumentStore(
                    Path.Combine(path, YourPalsContract.DocumentFileName));
                var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1");
                var document = new YourPalsDocument
                {
                    OwnerSaveIdentity = owner,
                    Groups =
                    [
                        new YourPalsGroup
                        {
                            GroupId = "empty-group",
                            Name = "Empty",
                            Order = 0,
                        },
                        new YourPalsGroup
                        {
                            GroupId = "favorites",
                            Name = "Favorites",
                            Order = 1,
                            Members =
                            [
                                YourPalsMember.Imported(
                                    new ImportedPalReference
                                    {
                                        SourceIdentity = SourceIdentity.ForSave(owner),
                                        SourceKey = "Palbox:box-1:0",
                                        InstanceId = "instance-1",
                                        LastKnownInternalName = sourcePal.Pal.InternalName,
                                    },
                                    "entry-1"),
                            ],
                        },
                    ],
                };
                store.Save(store.CreateNew(owner), document);

                using var session = new SavePalsSession(
                    save,
                    null,
                    Cached(owner, sourcePal),
                    store);
                using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);

                Assert.HasCount(2, viewModel.Groups);
                Assert.AreEqual("Empty", viewModel.Groups[0].Name);
                Assert.HasCount(1, viewModel.Entries);
                Assert.HasCount(1, viewModel.SourceEntries);
                Assert.AreEqual(YourPalsEntryStatus.Resolved, viewModel.Entries[0].Status);
                Assert.AreEqual(SourceIdentity.ForSave(owner).StableKey, viewModel.Entries[0].SourceScope);

                viewModel.SelectedEntry = viewModel.Entries[0];
                session.Refresh(Cached(owner, sourcePal));

                Assert.IsNotNull(viewModel.SelectedEntry);
                Assert.AreEqual("entry-1", viewModel.SelectedEntry.PalEntryKey);
            });
        }

        [TestMethod]
        public void RecoveryProjectionRemainsVisibleAndReadOnly()
        {
            WithTemporaryDirectory(path =>
            {
                var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
                File.WriteAllText(documentPath, "{ \"documentType\": \"your-pals\", \"documentVersion\": 1");

                var save = FakeSaveGame.Create("save-1");
                var owner = SaveIdentity.From(save);
                using var session = new SavePalsSession(
                    save,
                    null,
                    Cached(owner),
                    new YourPalsDocumentStore(documentPath));
                using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);

                Assert.AreEqual(SavePalsSessionState.Recovery, session.State);
                Assert.IsTrue(session.IsReadOnly);
                Assert.IsTrue(viewModel.HasDiagnostics);
                Assert.AreEqual("Recovery", viewModel.SessionState);
                Assert.AreNotEqual("No recovery details", viewModel.RecoveryState);
                Assert.IsTrue(viewModel.RecoveryGuidance.Contains(
                    "no empty replacement",
                    StringComparison.OrdinalIgnoreCase));
            });
        }

        [TestMethod]
        public void SaveSelectionCanOpenOrphanedDocumentsWithoutASelectedSave()
        {
            var opened = false;
            var viewModel = new SaveSelectionPageViewModel(
                [],
                null,
                () => opened = true);

            Assert.IsTrue(viewModel.OpenOrphanedDocumentsCommand.CanExecute(null));
            viewModel.OpenOrphanedDocumentsCommand.Execute(null);
            Assert.IsTrue(opened);
        }

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
            var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-phase3-" + Guid.NewGuid().ToString("N"));
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
    }
}
