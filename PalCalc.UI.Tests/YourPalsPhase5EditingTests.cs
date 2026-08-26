using Newtonsoft.Json.Linq;
using PalCalc.Model;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Tests;

[TestClass]
public class YourPalsPhase5EditingTests
{
    [TestMethod]
    public void SessionEditsSaveAndReloadGroupsMembersAndManualDefinitions()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1");
            using var session = new SavePalsSession(save, null, Cached(owner, sourcePal), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Archive", out var archiveId, out error), error);
            Assert.IsTrue(session.TryCreateGroup("Favorites", out var favoritesId, out error), error);
            Assert.IsTrue(session.TryMoveGroup(favoritesId, -1, out error), error);
            Assert.IsTrue(session.TryRenameGroup(archiveId, "Archived", out error), error);
            Assert.AreEqual("Favorites", session.Document.Groups[0].Name);

            Assert.IsTrue(session.TryAddImportedMember(
                favoritesId,
                session.SourceSnapshot.Entries.Single(),
                out var importedKey,
                out error), error);
            Assert.IsTrue(session.TryAddManualDefinition(
                favoritesId,
                "BadCatgirl",
                new Dictionary<string, JToken>
                {
                    ["gender"] = new JValue("FEMALE"),
                    ["level"] = new JValue(12),
                },
                out var manualDefinitionId,
                out var manualKey,
                out error), error);
            Assert.IsTrue(session.TryUpdateManualDefinition(
                manualDefinitionId,
                "BadCatgirl",
                null,
                out error), error);

            Assert.AreEqual(SavePalsSessionState.Dirty, session.State);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single(member =>
                member.Member.PalEntryKey == importedKey).Status);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single(member =>
                member.Member.PalEntryKey == manualKey).Status);
            Assert.HasCount(2, session.BuildSolverSource().Entries);
            Assert.IsTrue(session.BuildSolverSource().Pals.Any(pal =>
                pal.InstanceId == $"manual:{manualDefinitionId}"));

            Assert.IsTrue(session.TrySave());
            using var reloaded = new SavePalsSession(save, null, Cached(owner, sourcePal), store);

            Assert.AreEqual(SavePalsSessionState.Healthy, reloaded.State);
            Assert.AreEqual("Favorites", reloaded.Document.Groups[0].Name);
            Assert.HasCount(2, reloaded.Document.Groups[0].Members);
            Assert.AreEqual("BadCatgirl", reloaded.Document.ManualDefinitions.Single().RawInternalName);
            Assert.AreEqual("FEMALE", reloaded.Document.ManualDefinitions.Single().RawValues["gender"].Value<string>());
            Assert.AreEqual(YourPalsEntryStatus.Resolved, reloaded.ResolvedMembers.Single(member =>
                member.Member.ManualDefinitionId == manualDefinitionId).Status);
        });
    }

    [TestMethod]
    public void StaleImportedMemberCanBeReboundWithoutChangingItsEntryKey()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1");
            var group = new YourPalsGroup
            {
                GroupId = "favorites",
                Name = "Favorites",
                Members =
                [YourPalsMember.Imported(new ImportedPalReference
                {
                    SourceIdentity = SourceIdentity.ForSave(owner),
                    SourceKey = "Palbox:box-1:0",
                    InstanceId = "old-instance",
                    LastKnownInternalName = sourcePal.Pal.InternalName,
                }, "entry-1")],
            };
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups = [group],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, sourcePal), store);
            Assert.AreEqual(YourPalsEntryStatus.Stale, session.ResolvedMembers.Single().Status);

            Assert.IsTrue(session.TryRebindImportedMember(
                "favorites",
                "entry-1",
                session.SourceSnapshot.Entries.Single(),
                out var error), error);

            Assert.AreEqual("entry-1", session.ResolvedMembers.Single().Member.PalEntryKey);
            Assert.AreEqual("instance-1", session.ResolvedMembers.Single().Member.InstanceId);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single().Status);
        });
    }

    [TestMethod]
    public void RemovingAMemberLeavesTheSourceSnapshotUntouched()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1");
            using var session = new SavePalsSession(save, null, Cached(owner, sourcePal), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Favorites", out var groupId, out error), error);
            Assert.IsTrue(session.TryAddImportedMember(
                groupId,
                session.SourceSnapshot.Entries.Single(),
                out var entryKey,
                out error), error);
            Assert.IsTrue(session.TryRemoveMember(groupId, entryKey, out error), error);

            Assert.IsEmpty(session.Document.Groups.Single().Members);
            Assert.HasCount(1, session.SourceSnapshot.Entries);
            Assert.AreEqual("instance-1", session.SourceSnapshot.Entries[0].InstanceId);
        });
    }

    [TestMethod]
    public void RecoverySessionRejectsEditsAndKeepsTheDocumentReadOnly()
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

            Assert.IsFalse(session.CanEdit);
            Assert.IsFalse(session.TryCreateGroup("Should not save", out _, out var error));
            StringAssert.Contains(error, "read-only");
            Assert.IsNull(session.Document);
        });
    }

    [TestMethod]
    public void SolverSourceAdapterExcludesUnusableEntriesWithoutDeletingThem()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "resolved-instance");
            var sourceIdentity = SourceIdentity.ForSave(owner);
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "group-1",
                        Name = "Group",
                        Members =
                        [
                            YourPalsMember.Imported(new ImportedPalReference
                            {
                                SourceIdentity = sourceIdentity,
                                SourceKey = "Palbox:box-1:0",
                                InstanceId = pal.InstanceId,
                                LastKnownInternalName = pal.Pal.InternalName,
                            }, "resolved"),
                            YourPalsMember.Imported(new ImportedPalReference
                            {
                                SourceIdentity = sourceIdentity,
                                SourceKey = "Palbox:box-1:1",
                                InstanceId = "missing",
                                LastKnownInternalName = pal.Pal.InternalName,
                            }, "stale"),
                            new YourPalsMember
                            {
                                PalEntryKey = "invalid",
                                Kind = "future-kind",
                            },
                        ],
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, pal), store);
            var projection = session.BuildSolverSource();

            Assert.HasCount(1, projection.Entries);
            Assert.AreEqual("resolved", projection.Entries[0].PalEntryKey);
            Assert.HasCount(2, projection.ExcludedEntries);
            CollectionAssert.AreEquivalent(
                new[] { YourPalsEntryStatus.Stale, YourPalsEntryStatus.Invalid },
                projection.ExcludedEntries.Select(entry => entry.Status).ToArray());
            Assert.HasCount(3, session.Document.Groups.Single().Members);
        });
    }

    [TestMethod]
    public void SourceWithMissingGenderIsInvalidEverywhereAndNeverReachesTheSolver()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "missing-gender");
            pal.Gender = PalGender.NONE;
            var sourceIdentity = SourceIdentity.ForSave(owner);
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "group-1",
                        Name = "Group",
                        Members =
                        [YourPalsMember.Imported(new ImportedPalReference
                        {
                            SourceIdentity = sourceIdentity,
                            SourceKey = "Palbox:box-1:0",
                            InstanceId = pal.InstanceId,
                            LastKnownInternalName = pal.Pal.InternalName,
                        }, "entry-1")],
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, pal), store);
            var sourceEntry = session.SourceSnapshot.Entries.Single();

            Assert.IsFalse(session.CanUseSourceEntry(sourceEntry));
            Assert.AreEqual(YourPalsEntryStatus.Invalid, session.ResolvedMembers.Single().Status);
            Assert.IsEmpty(session.BuildSolverSource().Entries);
            Assert.HasCount(1, session.BuildSolverSource().ExcludedEntries);
            Assert.IsFalse(session.TryAddImportedMember(
                "group-1",
                sourceEntry,
                out _,
                out var error));
            StringAssert.Contains(error, "usable");
        });
    }

    [TestMethod]
    public void RebindingCannotCreateADuplicateImportedMemberInTheSameGroup()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var firstPal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "first-instance");
            var secondPal = SourcePal(PalDB.LoadEmbedded().Pals.Skip(1).First(), "second-instance");
            var sourceIdentity = SourceIdentity.ForSave(owner);
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "group-1",
                        Name = "Group",
                        Members =
                        [
                            YourPalsMember.Imported(new ImportedPalReference
                            {
                                SourceIdentity = sourceIdentity,
                                SourceKey = "Palbox:box-1:0",
                                InstanceId = "stale-instance",
                            }, "entry-1"),
                            YourPalsMember.Imported(new ImportedPalReference
                            {
                                SourceIdentity = sourceIdentity,
                                SourceKey = "Palbox:box-1:1",
                                InstanceId = secondPal.InstanceId,
                                LastKnownInternalName = secondPal.Pal.InternalName,
                            }, "entry-2"),
                        ],
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, firstPal, secondPal), store);
            var secondEntry = session.SourceSnapshot.Entries.Single(entry => entry.InstanceId == secondPal.InstanceId);

            Assert.IsFalse(session.TryRebindImportedMember(
                "group-1",
                "entry-1",
                secondEntry,
                out var error));
            StringAssert.Contains(error, "already in the selected group");
            Assert.AreEqual("stale-instance", session.Document.Groups.Single().Members.First().InstanceId);
        });
    }

    [TestMethod]
    public void ManualDefinitionAppliesRawSolverFields()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            using var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Favorites", out var groupId, out error), error);

            var db = PalDB.LoadEmbedded();
            var passive = db.StandardPassiveSkills.First();
            var active = db.ActiveSkills.First();
            Assert.IsTrue(session.TryAddManualDefinition(
                groupId,
                "BadCatgirl",
                new Dictionary<string, JToken>
                {
                    ["Gender"] = new JValue("FEMALE"),
                    ["Level"] = new JValue(20),
                    ["Rank"] = new JValue(4),
                    ["IV_HP"] = new JValue(80),
                    ["IV_Shot"] = new JValue(70),
                    ["IV_Defense"] = new JValue(60),
                    ["IV_Melee"] = new JValue(50),
                    ["OwnerPlayerId"] = new JValue("player-1"),
                    ["IsOnExpedition"] = new JValue(true),
                    ["NickName"] = new JValue("Manual Nyafia"),
                    ["PassiveSkills"] = new JArray(passive.InternalName),
                    ["ActiveSkills"] = new JArray(active.InternalName),
                    ["EquippedActiveSkills"] = new JArray(active.InternalName),
                },
                out _,
                out _,
                out error), error);

            var resolved = session.ResolvedMembers.Single().ResolvedRecord;
            Assert.AreEqual("Manual Nyafia", resolved.NickName);
            Assert.AreEqual("player-1", resolved.OwnerPlayerId);
            Assert.IsTrue(resolved.IsOnExpedition);
            Assert.AreEqual(50, resolved.IV_Melee);
            Assert.AreEqual(70, resolved.IV_Attack);
            Assert.AreEqual(passive.InternalName, resolved.PassiveSkills.Single().InternalName);
            Assert.AreEqual(active.InternalName, resolved.ActiveSkills.Single().InternalName);
            Assert.AreEqual(active.InternalName, resolved.EquippedActiveSkills.Single().InternalName);
        });
    }

    [TestMethod]
    public void MissingDocumentIsReadOnlyUntilExplicitlyCreated()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            using var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.AreEqual(YourPalsRecoveryState.MissingReadOnly, store.Load(owner).RecoveryState);
            Assert.IsTrue(session.CanCreateDocument);
            Assert.IsFalse(session.CanEdit);
            Assert.IsFalse(session.TryCreateGroup("No implicit document", out _, out _));
            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.CanEdit);
            Assert.IsTrue(session.TryCreateGroup("Created explicitly", out _, out error), error);
        });
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
        var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-phase5-" + Guid.NewGuid().ToString("N"));
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
