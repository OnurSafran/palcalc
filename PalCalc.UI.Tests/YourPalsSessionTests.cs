using PalCalc.Model;
using PalCalc.SaveReader;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Tests;

[TestClass]
public class YourPalsSessionTests
{
    [TestMethod]
    public void SourceSnapshotDeduplicatesEquivalentRecordsAndRetainsConflicts()
    {
        var owner = new SaveIdentity("user-1", "save-1");
        var pal = PalDB.LoadEmbedded().Pals.First();
        var equivalentA = SourcePal(pal, "same-instance", PalGender.MALE);
        var equivalentB = SourcePal(pal, "same-instance", PalGender.MALE);
        var conflict = SourcePal(pal, "conflict-instance", PalGender.MALE);
        var conflictCopy = SourcePal(pal, "conflict-instance", PalGender.FEMALE);
        var malformed = SourcePal(pal, "", PalGender.MALE);
        var cached = Cached(owner, equivalentA, equivalentB, conflict, conflictCopy, malformed);

        var snapshot = YourPalsSourceSnapshot.Build(owner, cached, null);

        Assert.HasCount(3, snapshot.Entries);
        Assert.IsTrue(snapshot.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.DuplicateSourceRecord));
        Assert.IsTrue(snapshot.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.ConflictingSourceRecord));
        Assert.IsTrue(snapshot.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.InvalidSourceRecord));
        Assert.AreEqual(2, snapshot.Entries.Count(entry => entry.InstanceId == "conflict-instance"));
    }

    [TestMethod]
    public void ConflictingSourceCopiesCanBeSelectedAndPersistedByFingerprint()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var pal = PalDB.LoadEmbedded().Pals.First();
            var male = SourcePal(pal, "conflict-instance", PalGender.MALE);
            var female = SourcePal(pal, "conflict-instance", PalGender.FEMALE);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, "save-1", YourPalsContract.DocumentFileName));
            var sourceIdentity = SourceIdentity.ForSave(owner);
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "group-1",
                        Name = "Favorites",
                        Members =
                        [
                            YourPalsMember.Imported(new ImportedPalReference
                            {
                                SourceIdentity = sourceIdentity,
                                SourceKey = "Palbox:box-1:0",
                                InstanceId = "conflict-instance",
                            }, "entry-1"),
                        ],
                    },
                ],
            });

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner, male, female),
                store);

            var copies = session.SourceSnapshot.Entries
                .Where(entry => entry.InstanceId == "conflict-instance")
                .ToList();
            Assert.HasCount(2, copies);
            Assert.IsTrue(copies.All(session.CanUseSourceEntry));
            Assert.AreEqual(YourPalsEntryStatus.Conflict, session.ResolvedMembers.Single().Status);

            Assert.IsTrue(session.TryRebindImportedMember(
                "group-1",
                "entry-1",
                copies[0],
                out var error), error);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single().Status);
            Assert.AreEqual(
                copies[0].ContentFingerprint,
                session.Document.Groups.Single().Members.Single().SourceContentFingerprint);
            Assert.IsTrue(session.TrySave());

            using var reloaded = new SavePalsSession(save, null, Cached(owner, male, female), store);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, reloaded.ResolvedMembers.Single().Status);
            Assert.AreEqual(
                copies[0].ContentFingerprint,
                reloaded.Document.Groups.Single().Members.Single().SourceContentFingerprint);
        });
    }

    [TestMethod]
    public void SourceSnapshotSelectsDuplicateRecordsByStableContentOrder()
    {
        var owner = new SaveIdentity("user-1", "save-1");
        var pal = PalDB.LoadEmbedded().Pals.First();
        var highIv = SourcePal(pal, "same-instance", PalGender.MALE);
        highIv.IV_HP = 90;
        var lowIv = SourcePal(pal, "same-instance", PalGender.MALE);
        lowIv.IV_HP = 10;

        var forward = YourPalsSourceSnapshot.Build(owner, Cached(owner, highIv, lowIv), null);
        var reverse = YourPalsSourceSnapshot.Build(owner, Cached(owner, lowIv, highIv), null);

        Assert.AreEqual(10, forward.Entries.Single().Record.IV_HP);
        Assert.AreEqual(10, reverse.Entries.Single().Record.IV_HP);
    }

    [TestMethod]
    public void SourceSnapshotUsesTheSelectedServerOwnershipScope()
    {
        var save = FakeSaveGame.Create("save-1");
        var pal = PalDB.LoadEmbedded().Pals.First();
        var currentPlayerPal = SourcePal(pal, "current", PalGender.MALE);
        currentPlayerPal.OwnerPlayerId = "player-1";
        var foreignPlayerPal = SourcePal(pal, "foreign", PalGender.FEMALE);
        foreignPlayerPal.OwnerPlayerId = "player-2";
        var cached = new CachedSaveGame(save)
        {
            IsServerSave = true,
            PlayerName = "Current",
            Players =
            [
                new PlayerInstance { PlayerId = "player-1", Name = "Current" },
                new PlayerInstance { PlayerId = "player-2", Name = "Other" },
            ],
            Guilds = [],
            Bases = [],
            PalContainers = [],
            OwnedPals = [currentPlayerPal, foreignPlayerPal],
        };

        var snapshot = YourPalsSourceSnapshot.Build(SaveIdentity.From(save), cached, null);

        Assert.HasCount(1, snapshot.Entries);
        Assert.AreEqual("current", snapshot.Entries[0].InstanceId);
    }

    [TestMethod]
    public void SourceSnapshotDoesNotLeakForeignGlobalStorageRecords()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var pal = PalDB.LoadEmbedded().Pals.First();
            var cached = new CachedSaveGame(save)
            {
                IsServerSave = true,
                PlayerName = "Current",
                Players =
                [
                    new PlayerInstance { PlayerId = "player-1", Name = "Current" },
                    new PlayerInstance { PlayerId = "player-2", Name = "Other" },
                ],
                Guilds = [],
                Bases = [],
                PalContainers =
                [
                    new GlobalPalStorageContainer { Id = "global-current", PlayerId = "player-1" },
                    new GlobalPalStorageContainer { Id = "global-other", PlayerId = "player-2" },
                ],
                OwnedPals =
                [
                    new PalInstance
                    {
                        Pal = pal,
                        InstanceId = "global-current-pal",
                        Gender = PalGender.MALE,
                        Location = new PalLocation
                        {
                            Type = LocationType.GlobalPalStorage,
                            ContainerId = "global-current",
                            Index = 0,
                        },
                    },
                    new PalInstance
                    {
                        Pal = pal,
                        InstanceId = "global-other-pal",
                        Gender = PalGender.FEMALE,
                        Location = new PalLocation
                        {
                            Type = LocationType.GlobalPalStorage,
                            ContainerId = "global-other",
                            Index = 0,
                        },
                    },
                ],
            };

            var snapshot = YourPalsSourceSnapshot.Build(
                SaveIdentity.From(save),
                cached,
                new DirectSavesLocation(path));

            Assert.HasCount(1, snapshot.Entries);
            Assert.AreEqual("global-current-pal", snapshot.Entries.Single().InstanceId);
        });
    }

    [TestMethod]
    public void UnknownServerOwnershipMakesTheSourceUnavailable()
    {
        var save = FakeSaveGame.Create("save-1");
        var pal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "unscoped", PalGender.MALE);
        var cached = new CachedSaveGame(save)
        {
            IsServerSave = true,
            PlayerName = "Missing player",
            Players = [],
            Guilds = [],
            Bases = [],
            PalContainers = [],
            OwnedPals = [pal],
        };

        var snapshot = YourPalsSourceSnapshot.Build(SaveIdentity.From(save), cached, null);

        Assert.IsFalse(snapshot.IsAvailable);
        Assert.IsTrue(snapshot.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.OwnershipUnresolved));
        Assert.IsEmpty(snapshot.Entries);
    }

    [TestMethod]
    public void GlobalStorageUsesAStableSourceIdentityDistinctFromTheSave()
    {
        WithTemporaryDirectory(path =>
        {
            var location = new DirectSavesLocation(path);
            var owner = new SaveIdentity("user-1", "save-1");
            var saveIdentity = SourceIdentity.ForSave(owner);
            var globalIdentity = SourceIdentity.ForGlobalPalStorage(location);
            var globalIdentityAgain = SourceIdentity.ForGlobalPalStorage(location);

            Assert.AreNotEqual(saveIdentity, globalIdentity);
            Assert.AreEqual(globalIdentity, globalIdentityAgain);
            Assert.AreEqual(YourPalsSourceKind.GlobalPalStorage, globalIdentity.Kind);
        });
    }

    [TestMethod]
    public void RefreshRebuildsSourcesAndKeepsMissingMembersStale()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var store = new YourPalsDocumentStore(documentPath);
            var imported = YourPalsMember.Imported(
                new ImportedPalReference
                {
                    SourceIdentity = SourceIdentity.ForSave(owner),
                    SourceKey = "Palbox:box-1:0",
                    InstanceId = "pal-instance",
                    LastKnownInternalName = "Cattiva",
                },
                "entry-1");
            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "group-1",
                        Name = "Favorites",
                        Members = [imported],
                    },
                ],
            };
            store.Save(store.CreateNew(owner), document);

            var pal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "pal-instance", PalGender.MALE);
            using var session = new SavePalsSession(save, null, Cached(owner, pal), store);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single().Status);

            session.Refresh(Cached(owner));

            Assert.AreEqual(YourPalsEntryStatus.Stale, session.ResolvedMembers.Single().Status);
            Assert.HasCount(1, session.Document.Groups.Single().Members);
            Assert.AreEqual(SavePalsSessionState.Healthy, session.State);
        });
    }

    [TestMethod]
    public void DirtyStateCanBeSavedAndWriteFailureKeepsItDirty()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.AreEqual(SavePalsSessionState.Dirty, session.State);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(SavePalsSessionState.Healthy, session.State);
            session.Dispose();

            var blockedPath = Path.Combine(path, "blocked-document");
            Directory.CreateDirectory(blockedPath);
            using var failedSession = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(blockedPath));
            Assert.IsTrue(failedSession.TryCreateDocument(out error), error);

            Assert.IsFalse(failedSession.TrySave());
            Assert.AreEqual(SavePalsSessionState.WriteFailed, failedSession.State);
            Assert.IsTrue(failedSession.IsDirty);
        });
    }

    [TestMethod]
    public void SourceLoadFailureRemainsUntilASuccessfulSourceRefresh()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            store.Save(store.CreateNew(owner), YourPalsDocument.Empty(owner));

            using var session = new SavePalsSession(save, null, Cached(owner), store);

            session.RecordSourceLoadFailure(new IOException("source unavailable"));
            Assert.AreEqual(SavePalsSessionState.SourceUnavailable, session.State);
            Assert.IsTrue(session.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.SourceUnavailable));

            session.RefreshCurrent();
            Assert.AreEqual(SavePalsSessionState.SourceUnavailable, session.State);
            Assert.IsTrue(session.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.SourceUnavailable));

            Assert.IsTrue(session.TryCreateGroup("Favorites", out _, out var error), error);
            Assert.AreEqual(SavePalsSessionState.Dirty, session.State);
            Assert.IsTrue(session.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.SourceUnavailable));

            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(SavePalsSessionState.SourceUnavailable, session.State);
            Assert.IsTrue(session.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.SourceUnavailable));

            session.Refresh(Cached(owner));
            Assert.AreEqual(SavePalsSessionState.Healthy, session.State);
            Assert.IsFalse(session.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.SourceUnavailable));
        });
    }

    [TestMethod]
    public void SourceLoadFailureDoesNotAllowStaleSourceEntriesIntoImportsOrSolver()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1", PalGender.MALE);
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "favorites",
                        Name = "Favorites",
                        Members =
                        [YourPalsMember.Imported(new ImportedPalReference
                        {
                            SourceIdentity = SourceIdentity.ForSave(owner),
                            SourceKey = "Palbox:box-1:0",
                            InstanceId = sourcePal.InstanceId,
                            LastKnownInternalName = sourcePal.Pal.InternalName,
                        }, "entry-1")],
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, sourcePal), store);
            var sourceEntry = session.SourceSnapshot.Entries.Single();

            session.RecordSourceLoadFailure(new IOException("source unavailable"));

            Assert.IsFalse(session.IsSourceAvailable);
            Assert.IsFalse(session.CanUseSourceEntry(sourceEntry));
            Assert.IsEmpty(session.BuildSolverSource().Entries);
            Assert.HasCount(1, session.BuildSolverSource().ExcludedEntries);
        });
    }

    [TestMethod]
    public void ASourceEntryFromAnotherSessionCannotBeAdded()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, "save-1", YourPalsContract.DocumentFileName));
            using var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Favorites", out var groupId, out error), error);

            var otherSave = FakeSaveGame.Create("save-2");
            var otherOwner = SaveIdentity.From(otherSave);
            var otherPal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "other-instance", PalGender.MALE);
            var foreignEntry = YourPalsSourceSnapshot.Build(
                    otherOwner,
                    Cached(otherOwner, otherPal),
                    null)
                .Entries.Single();

            Assert.IsFalse(session.TryAddImportedMember(groupId, foreignEntry, out _, out error));
            StringAssert.Contains(error, "active save session");
            Assert.IsEmpty(session.Document.Groups.Single().Members);
        });
    }

    [TestMethod]
    public void SessionRejectsCachedDataFromAnotherSaveIdentity()
    {
        var save = FakeSaveGame.Create("save-1");
        var otherSave = FakeSaveGame.Create("save-2");
        var store = new YourPalsDocumentStore(
            Path.Combine(Path.GetTempPath(), "your-pals-identity-test-" + Guid.NewGuid().ToString("N"),
                YourPalsContract.DocumentFileName));

        Assert.Throws<InvalidOperationException>(() => new SavePalsSession(
            save,
            null,
            new CachedSaveGame(otherSave),
            store));
    }

    [TestMethod]
    public void AStaleSessionCannotOverwriteANewerDocument()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, "save-1", YourPalsContract.DocumentFileName);
            using var first = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(documentPath));
            using var second = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(documentPath));

            Assert.IsTrue(first.TryCreateDocument(out var error), error);
            Assert.IsTrue(second.TryCreateDocument(out error), error);
            Assert.IsTrue(first.TrySave());
            Assert.IsTrue(second.TryCreateGroup("Stale edit", out _, out error), error);

            Assert.IsFalse(second.TrySave());
            Assert.IsTrue(second.IsDirty);
            Assert.AreEqual(SavePalsSessionState.ExternalConflict, second.State);
            Assert.IsFalse(second.CanEdit);
            Assert.IsTrue(second.CanDiscardChangesAndReload);
            Assert.IsTrue(second.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.ExternalConflict));

            Assert.IsTrue(second.TryDiscardChangesAndReload(out error), error);
            Assert.AreEqual(SavePalsSessionState.Healthy, second.State);
            Assert.IsFalse(second.IsDirty);
            Assert.IsEmpty(second.Document.Groups);
        });
    }

    [TestMethod]
    public void OrphanedSessionCanBeReactivatedWithTheSameSaveIdentity()
    {
        var save = FakeSaveGame.Create("save-1");
        var owner = SaveIdentity.From(save);
        var store = new YourPalsDocumentStore(
            Path.Combine(Path.GetTempPath(), "your-pals-orphan-test-" + Guid.NewGuid().ToString("N"), YourPalsContract.DocumentFileName));
        store.Save(store.CreateNew(owner), YourPalsDocument.Empty(owner));
        using var session = new SavePalsSession(save, null, Cached(owner), store);

        session.MarkOrphaned();
        Assert.AreEqual(SavePalsSessionState.Orphaned, session.State);

        var refreshedSave = FakeSaveGame.Create("save-1");
        session.Refresh(Cached(owner), null, refreshedSave);

        Assert.AreSame(refreshedSave, session.Save);
        Assert.AreEqual(SavePalsSessionState.Healthy, session.State);
    }

    [TestMethod]
    public void RefreshCurrentDoesNotReactivateAnOrphanedSession()
    {
        var save = FakeSaveGame.Create("save-1");
        var owner = SaveIdentity.From(save);
        var store = new YourPalsDocumentStore(
            Path.Combine(Path.GetTempPath(), "your-pals-orphan-current-refresh-test-" + Guid.NewGuid().ToString("N"), YourPalsContract.DocumentFileName));
        using var session = new SavePalsSession(save, null, Cached(owner), store);

        session.MarkOrphaned();
        session.RefreshCurrent();

        Assert.AreEqual(SavePalsSessionState.Orphaned, session.State);
        Assert.IsTrue(session.IsReadOnly);
        Assert.IsFalse(session.CanEdit);
    }

    [TestMethod]
    public void OrphanedSessionStaysOrphanedWhenRefreshedWithoutTheSave()
    {
        var save = FakeSaveGame.Create("save-1");
        var owner = SaveIdentity.From(save);
        var store = new YourPalsDocumentStore(
            Path.Combine(Path.GetTempPath(), "your-pals-orphan-refresh-test-" + Guid.NewGuid().ToString("N"), YourPalsContract.DocumentFileName));
        using var session = new SavePalsSession(save, null, Cached(owner), store);

        session.MarkOrphaned();
        session.Refresh(null);

        Assert.AreEqual(SavePalsSessionState.Orphaned, session.State);
        Assert.IsTrue(session.IsReadOnly);
    }

    [TestMethod]
    public void AvailableSaveReactivatesAnOrphanedSessionEvenWhenItsSourceIsUnavailable()
    {
        var save = FakeSaveGame.Create("save-1");
        var owner = SaveIdentity.From(save);
        var store = new YourPalsDocumentStore(
            Path.Combine(Path.GetTempPath(), "your-pals-orphan-source-test-" + Guid.NewGuid().ToString("N"), YourPalsContract.DocumentFileName));
        store.Save(store.CreateNew(owner), YourPalsDocument.Empty(owner));
        using var session = new SavePalsSession(save, null, Cached(owner), store);

        session.MarkOrphaned();
        session.Refresh(null, null, FakeSaveGame.Create("save-1"));

        Assert.AreNotEqual(SavePalsSessionState.Orphaned, session.State);
        Assert.IsFalse(session.IsReadOnly);
        Assert.AreEqual(SavePalsSessionState.SourceUnavailable, session.State);
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

    private static PalInstance SourcePal(Pal pal, string instanceId, PalGender gender) => new()
    {
        Pal = pal,
        InstanceId = instanceId,
        Gender = gender,
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
        var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-" + Guid.NewGuid().ToString("N"));
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
