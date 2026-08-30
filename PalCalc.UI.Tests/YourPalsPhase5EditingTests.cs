using Newtonsoft.Json.Linq;
using PalCalc.Model;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using PalCalc.UI.ViewModel;
using PalCalc.UI.ViewModel.Mapped;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

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
    public void EditingAMissingManualDefinitionRecreatesItsStableDefinitionId()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "manual-group",
                        Name = "Manual group",
                        Members = [YourPalsMember.Manual("missing-definition", "entry-1")],
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner), store);
            using var viewModel = new YourPalsViewModel(session, System.Windows.Threading.Dispatcher.CurrentDispatcher);

            viewModel.SelectedEntry = viewModel.Entries.Single();
            Assert.IsTrue(viewModel.EditSelectedManualCommand.CanExecute(null));
            viewModel.EditSelectedManualCommand.Execute(null);
            viewModel.SelectedManualPal = PalViewModel.All.First();
            viewModel.SaveManualEditorCommand.Execute(null);

            Assert.AreEqual("missing-definition", session.Document.ManualDefinitions.Single().ManualDefinitionId);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single().Status);
        });
    }

    [TestMethod]
    public void ManualEditorUpdatesPreserveFieldsOwnedByNewerVersions()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "manual-group",
                        Name = "Manual group",
                        Members = [YourPalsMember.Manual("manual-1", "entry-1")],
                    },
                ],
                ManualDefinitions =
                [
                    new YourPalsManualDefinition
                    {
                        ManualDefinitionId = "manual-1",
                        RawInternalName = PalDB.LoadEmbedded().Pals.First().InternalName,
                        RawValues = new Dictionary<string, JToken>
                        {
                            ["level"] = new JValue(12),
                            ["rank"] = new JValue(5),
                            ["futureField"] = new JValue("keep-me"),
                        },
                    },
                ],
            });

            using var session = new SavePalsSession(save, null, Cached(owner), store);
            Assert.IsTrue(session.TryUpdateManualDefinition(
                "manual-1",
                PalDB.LoadEmbedded().Pals.First().InternalName,
                new Dictionary<string, JToken> { ["level"] = new JValue(20) },
                out var error), error);

            var values = session.Document.ManualDefinitions.Single().RawValues;
            Assert.AreEqual(20, values["level"].Value<int>());
            Assert.AreEqual(5, values["rank"].Value<int>());
            Assert.AreEqual("keep-me", values["futureField"].Value<string>());
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
    public void SelectingANonManualMemberClearsTheManualEditorValue()
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
                out _,
                out error), error);
            Assert.IsTrue(session.TryAddManualDefinition(
                groupId,
                "ManualPal",
                new Dictionary<string, JToken>(),
                out _,
                out _,
                out error), error);

            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);
            var manualEntry = viewModel.Entries.Single(entry =>
                entry.Member.KnownKind == YourPalsMemberKind.ManualDefinitionReference);
            var importedEntry = viewModel.Entries.Single(entry =>
                entry.Member.KnownKind == YourPalsMemberKind.ImportedReference);

            viewModel.SelectedEntry = manualEntry;
            Assert.AreEqual("ManualPal", viewModel.ManualInternalName);

            viewModel.SelectedEntry = importedEntry;
            Assert.AreEqual("", viewModel.ManualInternalName);
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

    [TestMethod]
    public void RemovingTheLastReferenceToAManualPalDropsItsDefinition()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            using var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Planning", out var groupId, out error), error);
            Assert.IsTrue(session.TryAddManualDefinition(
                groupId,
                "BadCatgirl",
                null,
                out var manualDefinitionId,
                out var manualKey,
                out error), error);
            Assert.AreEqual(manualDefinitionId, session.Document.ManualDefinitions.Single().ManualDefinitionId);

            Assert.IsTrue(session.TryRemoveMember(groupId, manualKey, out error), error);
            Assert.IsEmpty(session.Document.ManualDefinitions);
        });
    }

    [TestMethod]
    public void DeletingAGroupDropsTheManualDefinitionsOnlyItReferenced()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            using var session = new SavePalsSession(save, null, Cached(owner), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Planning", out var planningId, out error), error);
            Assert.IsTrue(session.TryCreateGroup("Keep", out var keepId, out error), error);
            Assert.IsTrue(session.TryAddManualDefinition(
                planningId, "BadCatgirl", null, out var droppedId, out _, out error), error);
            Assert.IsTrue(session.TryAddManualDefinition(
                keepId, "BadCatgirl", null, out var keptId, out _, out error), error);
            Assert.HasCount(2, session.Document.ManualDefinitions);

            Assert.IsTrue(session.TryDeleteGroup(planningId, out error), error);

            Assert.AreEqual(keptId, session.Document.ManualDefinitions.Single().ManualDefinitionId);
            Assert.IsTrue(session.TrySave());

            using var reloaded = new SavePalsSession(save, null, Cached(owner), store);
            Assert.AreEqual(keptId, reloaded.Document.ManualDefinitions.Single().ManualDefinitionId);
            Assert.IsFalse(reloaded.Document.ManualDefinitions.Any(d => d.ManualDefinitionId == droppedId));
        });
    }

    [TestMethod]
    public void RemovingMissingMembersDropsOnlyThePalsThatLeftTheSave()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pals = PalDB.LoadEmbedded().Pals.Take(2).ToList();
            var keptPal = SourcePal(pals[0], "instance-kept");
            var removedPal = SourcePal(pals[1], "instance-removed");

            using var session = new SavePalsSession(
                save, null, Cached(owner, keptPal, removedPal), store);

            Assert.IsTrue(session.TryCreateDocument(out var error), error);
            Assert.IsTrue(session.TryCreateGroup("Breeding", out var groupId, out error), error);
            // Each edit rebuilds the snapshot, so the entry must be re-read from the
            // current one rather than captured up front.
            foreach (var instanceId in new[] { "instance-kept", "instance-removed" })
            {
                var entry = session.SourceSnapshot.Entries.Single(
                    candidate => candidate.InstanceId == instanceId);
                Assert.IsTrue(session.TryAddImportedMember(groupId, entry, out _, out error), error);
            }
            Assert.IsTrue(session.TryAddManualDefinition(
                groupId, "BadCatgirl", null, out _, out var manualKey, out error), error);
            Assert.HasCount(3, session.ResolvedMembers);

            // The save reloads with one of the two Pals gone.
            session.Refresh(Cached(owner, keptPal));
            Assert.AreEqual(
                1,
                session.ResolvedMembers.Count(member => member.Status == YourPalsEntryStatus.Stale));

            Assert.IsTrue(session.TryRemoveMissingMembers(out var removedCount, out error), error);
            Assert.AreEqual(1, removedCount);

            var remaining = session.Document.Groups.Single().Members;
            Assert.HasCount(2, remaining);
            Assert.IsTrue(remaining.Any(member => member.InstanceId == "instance-kept"));
            Assert.IsFalse(remaining.Any(member => member.InstanceId == "instance-removed"));
            // The manual Pal is not "missing from the save" and must survive.
            Assert.IsTrue(remaining.Any(member => member.PalEntryKey == manualKey));
            Assert.HasCount(1, session.Document.ManualDefinitions);
        });
    }

    [TestMethod]
    public void RemovingMissingMembersIsRefusedWhileTheSaveSourceIsUnavailable()
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
            Assert.IsTrue(session.TryCreateGroup("Breeding", out var groupId, out error), error);
            Assert.IsTrue(session.TryAddImportedMember(
                groupId, session.SourceSnapshot.Entries.Single(), out _, out error), error);

            // With no cached save at all every imported member looks missing;
            // bulk removal must not wipe the group in that state.
            session.Refresh(null);
            Assert.IsFalse(session.IsSourceAvailable);

            Assert.IsFalse(session.TryRemoveMissingMembers(out var removedCount, out error));
            Assert.AreEqual(0, removedCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Assert.HasCount(1, session.Document.Groups.Single().Members);
        });
    }

    [TestMethod]
    public void SavedMemberFingerprintIsWrittenExactlyOnce()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var store = new YourPalsDocumentStore(documentPath);
            var sourcePal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1");

            using (var session = new SavePalsSession(save, null, Cached(owner, sourcePal), store))
            {
                Assert.IsTrue(session.TryCreateDocument(out var error), error);
                Assert.IsTrue(session.TryCreateGroup("Breeding", out var groupId, out error), error);
                Assert.IsTrue(session.TryAddImportedMember(
                    groupId, session.SourceSnapshot.Entries.Single(), out _, out error), error);
                Assert.IsTrue(session.TrySave());
            }

            // Reload and save again: the recovery reader must treat the fingerprint
            // as a known field rather than also copying it into extension data,
            // which would emit the property twice.
            using (var reloaded = new SavePalsSession(save, null, Cached(owner, sourcePal), store))
            {
                Assert.IsTrue(reloaded.TryCreateGroup("Second", out _, out var error), error);
                Assert.IsTrue(reloaded.TrySave());
            }

            var json = File.ReadAllText(documentPath);
            Assert.AreEqual(
                1,
                System.Text.RegularExpressions.Regex.Matches(json, "\"sourceContentFingerprint\"").Count,
                json);
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
