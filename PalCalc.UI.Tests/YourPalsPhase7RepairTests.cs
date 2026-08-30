using Newtonsoft.Json.Linq;
using PalCalc.Model;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using PalCalc.UI.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace PalCalc.UI.Tests;

[TestClass]
public class YourPalsPhase7RepairTests
{
    [TestMethod]
    public void MalformedLargeManualIntegerStaysUnresolvedWithoutThrowing()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var internalName = PalDB.LoadEmbedded().Pals.First().InternalName;
            File.WriteAllText(
                documentPath,
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{owner.UserId}}", "gameId": "{{owner.GameId}}"},
                  "groups": [{
                    "groupId": "favorites",
                    "name": "Favorites",
                    "order": 0,
                    "members": [{"palEntryKey": "manual-entry", "kind": "manual-definition-reference", "manualDefinitionId": "manual-1"}]
                  }],
                  "manualDefinitions": [{
                    "manualDefinitionId": "manual-1",
                    "rawInternalName": "{{internalName}}",
                    "rawValues": {"level": 999999999999999999999999999999}
                  }]
                }
                """);

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(documentPath));

            var member = session.ResolvedMembers.Single();
            Assert.AreEqual(YourPalsEntryStatus.Unresolved, member.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(member.Reason));
            Assert.IsEmpty(session.BuildSolverSource().Entries);
        });
    }

    [TestMethod]
    public void ExplicitRepairRetainsMalformedFieldTokensForManualRecovery()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{owner.UserId}}", "gameId": "{{owner.GameId}}"},
                  "groups": [{
                    "groupId": "favorites",
                    "name": "Favorites",
                    "order": 0,
                    "members": [{"palEntryKey": "entry-1", "kind": "imported-reference", "instanceId": 12345}]
                  }],
                  "manualDefinitions": [{
                    "manualDefinitionId": "manual-1",
                    "rawInternalName": "Cattiva",
                    "rawValues": ["preserve-me"]
                  }]
                }
                """);

            var store = new YourPalsDocumentStore(documentPath);
            using var session = new SavePalsSession(save, null, Cached(owner), store);
            Assert.IsTrue(session.CanRepairRecoveredDocument);
            Assert.IsTrue(session.TryRepairRecoveredDocument(out _, out var error), error);

            var saved = JObject.Parse(File.ReadAllText(documentPath));
            var savedMember = (JObject)saved["groups"]![0]!["members"]![0]!;
            var savedDefinition = (JObject)saved["manualDefinitions"]![0]!;
            Assert.AreEqual(12345, savedMember[YourPalsContract.RecoveryRawFieldsExtensionDataKey]!["instanceId"]!.Value<int>());
            Assert.AreEqual(JTokenType.Array, savedDefinition[YourPalsContract.RecoveryRawFieldsExtensionDataKey]!["rawValues"]!.Type);
        });
    }

    [TestMethod]
    public void BulkRebindResolvesConflictingSameInstanceMembers()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pal = PalDB.LoadEmbedded().Pals.First();
            var first = SourcePal(pal, "instance-1", "box-1", 0);
            var second = SourcePal(pal, "instance-1", "box-2", 1);
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
                            SourceKey = null,
                            InstanceId = "instance-1",
                        }, "entry-1")],
                    },
                ],
            });

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner, first, second),
                store);
            Assert.AreEqual(YourPalsEntryStatus.Conflict, session.ResolvedMembers.Single().Status);

            var selected = session.SourceSnapshot.Entries.Single(entry => entry.SourceKey.Contains("box-2", StringComparison.Ordinal));
            Assert.IsTrue(session.TryBulkRebindMatchingMembers(selected, out var repairedCount, out var error), error);
            Assert.AreEqual(1, repairedCount);
            Assert.AreEqual(YourPalsEntryStatus.Resolved, session.ResolvedMembers.Single().Status);
            Assert.AreEqual(selected.SourceKey, session.Document.Groups[0].Members[0].SourceKey);
            Assert.HasCount(1, session.BuildSolverSource().Entries);
        });
    }

    [TestMethod]
    public void AddPickerMarksConflictingCopiesAlreadyPresentInTheDestinationGroup()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pal = PalDB.LoadEmbedded().Pals.First();
            var first = SourcePal(pal, "instance-1", "box-1", 0);
            var second = SourcePal(pal, "instance-1", "box-2", 1);
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
                            SourceKey = null,
                            InstanceId = "instance-1",
                        }, "entry-1")],
                    },
                ],
            });

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner, first, second),
                store);
            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);
            viewModel.SelectedGroupSummary = viewModel.Groups.Single(group => group.GroupId == "favorites");

            viewModel.AddPalCommand.Execute(null);

            Assert.IsGreaterThan(viewModel.AddPalOptions.Count, 1);
            Assert.IsTrue(viewModel.AddPalOptions.All(option => option.IsAlreadyInSelectedGroup));
        });
    }

    [TestMethod]
    public void DuplicateMembersCanBeRemovedWithoutUsingRandomIdentifiers()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));
            var pal = SourcePal(PalDB.LoadEmbedded().Pals.First(), "instance-1", "box-1", 0);
            var first = YourPalsMember.Imported(new ImportedPalReference
            {
                SourceIdentity = SourceIdentity.ForSave(owner),
                SourceKey = "Palbox:box-1:0",
                InstanceId = "instance-1",
            }, "entry-1");
            var duplicate = YourPalsMember.Imported(new ImportedPalReference
            {
                SourceIdentity = SourceIdentity.ForSave(owner),
                SourceKey = "Palbox:box-1:0",
                InstanceId = "instance-1",
            }, "entry-2");
            store.Save(store.CreateNew(owner), new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [new YourPalsGroup
                {
                    GroupId = "favorites",
                    Name = "Favorites",
                    Members = [first, duplicate],
                }],
            });

            using var session = new SavePalsSession(save, null, Cached(owner, pal), store);
            Assert.IsTrue(session.TryRemoveDuplicateMembers(out var summary, out var error), error);
            Assert.AreEqual(1, summary.RemovedDuplicateMembers);
            Assert.HasCount(1, session.Document.Groups.Single().Members);
            Assert.AreEqual("entry-1", session.Document.Groups.Single().Members.Single().PalEntryKey);
        });
    }

    [TestMethod]
    public void PartialRecoveryCanBeExplicitlyRepairedAndSaved()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{owner.UserId}}", "gameId": "{{owner.GameId}}"},
                  "groups": [
                    {"groupId": "duplicate", "name": "One", "order": 0, "members": [{"palEntryKey": "entry", "kind": "future-kind"}, {"palEntryKey": "manual-entry", "kind": "manual-definition-reference", "manualDefinitionId": "manual"}]},
                    {"groupId": "duplicate", "name": "Two", "order": 1, "members": [{"palEntryKey": "entry", "kind": "future-kind-2"}]}
                  ],
                  "manualDefinitions": [
                    {"manualDefinitionId": "manual", "rawInternalName": "A", "rawValues": { } },
                    {"manualDefinitionId": "manual", "rawInternalName": "B", "rawValues": { } }
                  ]
                }
                """);

            var store = new YourPalsDocumentStore(documentPath);
            using var session = new SavePalsSession(save, null, Cached(owner), store);
            Assert.IsTrue(session.CanRepairRecoveredDocument);
            Assert.IsTrue(session.TryRepairRecoveredDocument(out var summary, out var error), error);
            Assert.IsTrue(summary.TotalChanges > 0);
            Assert.AreEqual(1, summary.RemovedDuplicateManualDefinitions);
            Assert.AreEqual(SavePalsSessionState.Healthy, session.State);
            Assert.AreEqual(
                session.Document.Groups.Count,
                session.Document.Groups.Select(group => group.GroupId).Distinct(StringComparer.Ordinal).Count());
            Assert.AreEqual(
                session.Document.ManualDefinitions.Count,
                session.Document.ManualDefinitions.Select(definition => definition.ManualDefinitionId).Distinct(StringComparer.Ordinal).Count());
            Assert.AreEqual("A", session.Document.ManualDefinitions.Single().RawInternalName);
            Assert.AreEqual(YourPalsRecoveryState.Healthy, store.Load(owner).RecoveryState);
        });
    }

    [TestMethod]
    public void OrphanedDocumentsAreListedAndDeletedWithTheirBackup()
    {
        WithTemporaryDirectory(path =>
        {
            var orphanOwner = new SaveIdentity("user-1", "missing-save");
            var ownerDirectory = Path.Combine(path, "missing-save");
            Directory.CreateDirectory(ownerDirectory);
            var documentPath = Path.Combine(ownerDirectory, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "missing-save"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);
            File.WriteAllText(documentPath + ".bak", "backup");

            var orphans = YourPalsOrphanedDocumentManager.Find(
                path,
                [new SaveIdentity("user-1", "available-save")]);
            var orphan = orphans.Single();
            Assert.AreEqual(orphanOwner.CanonicalKey, orphan.OwnerLabel);
            Assert.IsTrue(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error), error);
            Assert.IsFalse(File.Exists(documentPath));
            Assert.IsFalse(File.Exists(documentPath + ".bak"));
        });
    }

    [TestMethod]
    public void DamagedOrphanWithNoReadableOwnerCanStillBeDeleted()
    {
        WithTemporaryDirectory(path =>
        {
            var ownerDirectory = Path.Combine(path, "damaged-save");
            Directory.CreateDirectory(ownerDirectory);
            var documentPath = Path.Combine(ownerDirectory, YourPalsContract.DocumentFileName);
            File.WriteAllText(documentPath, "{ this is not valid json");
            File.WriteAllText(documentPath + ".bak", "also not valid json");

            var orphan = YourPalsOrphanedDocumentManager.Find(path, []).Single();
            Assert.IsNull(orphan.OwnerSaveIdentity);

            Assert.IsTrue(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error), error);
            Assert.IsFalse(File.Exists(documentPath));
            Assert.IsFalse(File.Exists(documentPath + ".bak"));
        });
    }

    [TestMethod]
    public void OrphanListedWithoutAnOwnerIsNotDeletedOnceItsOwnerBecomesReadable()
    {
        WithTemporaryDirectory(path =>
        {
            var ownerDirectory = Path.Combine(path, "repaired-save");
            Directory.CreateDirectory(ownerDirectory);
            var documentPath = Path.Combine(ownerDirectory, YourPalsContract.DocumentFileName);
            File.WriteAllText(documentPath, "{ this is not valid json");

            var orphan = YourPalsOrphanedDocumentManager.Find(path, []).Single();
            Assert.IsNull(orphan.OwnerSaveIdentity);

            // The file is replaced with a readable one after the list was built.
            File.WriteAllText(
                documentPath,
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "repaired-save"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);

            Assert.IsFalse(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Assert.IsTrue(File.Exists(documentPath));
        });
    }

    [TestMethod]
    public void BackupOnlyOrphanIsListedAndCanBeDeleted()
    {
        WithTemporaryDirectory(path =>
        {
            var orphanOwner = new SaveIdentity("user-1", "missing-save");
            var ownerDirectory = Path.Combine(path, "missing-save");
            Directory.CreateDirectory(ownerDirectory);
            var documentPath = Path.Combine(ownerDirectory, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath + ".bak",
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{orphanOwner.UserId}}", "gameId": "{{orphanOwner.GameId}}"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);

            var orphan = YourPalsOrphanedDocumentManager.Find(path, []).Single();

            Assert.AreEqual(documentPath, orphan.DocumentPath);
            Assert.IsTrue(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error), error);
            Assert.IsFalse(File.Exists(documentPath + ".bak"));
        });
    }

    [TestMethod]
    public void ValidBackupIsRecoveredReadOnlyWhenPrimaryIsMissing()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var store = new YourPalsDocumentStore(documentPath);
            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups = [new YourPalsGroup { GroupId = "favorites", Name = "Favorites", Order = 0 }],
            };
            var initial = store.CreateNew(owner);
            store.Save(initial, document);
            File.Move(documentPath, documentPath + ".bak");

            var loaded = store.Load(owner);

            Assert.AreEqual(YourPalsRecoveryState.PartiallyRecoveredReadOnly, loaded.RecoveryState);
            Assert.IsFalse(loaded.CanPersistSafely);
            Assert.AreEqual("favorites", loaded.Document.Groups.Single().GroupId);

            store.RepairAndSave(loaded, loaded.Document);

            Assert.IsTrue(File.Exists(documentPath));
            Assert.IsTrue(File.Exists(documentPath + ".bak"));
        });
    }

    [TestMethod]
    public void ValidBackupIsRecoveredReadOnlyWhenPrimaryIsCorrupt()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var store = new YourPalsDocumentStore(documentPath);
            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups = [new YourPalsGroup { GroupId = "favorites", Name = "Favorites", Order = 0 }],
            };
            var initial = store.CreateNew(owner);
            store.Save(initial, document);
            File.Copy(documentPath, documentPath + ".bak");
            File.WriteAllText(documentPath, "not-json");

            var loaded = store.Load(owner);

            Assert.AreEqual(YourPalsRecoveryState.PartiallyRecoveredReadOnly, loaded.RecoveryState);
            Assert.AreEqual("favorites", loaded.Document.Groups.Single().GroupId);
            Assert.IsTrue(loaded.Diagnostics.Any(diagnostic =>
                diagnostic.Message.Contains("backup was recovered", StringComparison.OrdinalIgnoreCase)));

            using var session = new SavePalsSession(save, null, Cached(owner), store);
            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);
            Assert.IsTrue(viewModel.RecoveryGuidance.Contains(
                "Repair recovered",
                StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void OrphanOwnerIsRecoveredFromValidBackupWhenPrimaryIsCorrupt()
    {
        WithTemporaryDirectory(path =>
        {
            var orphanOwner = new SaveIdentity("user-1", "missing-save");
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(documentPath, "not-json");
            File.WriteAllText(
                documentPath + ".bak",
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{orphanOwner.UserId}}", "gameId": "{{orphanOwner.GameId}}"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);

            var orphan = YourPalsOrphanedDocumentManager.Find(path, []).Single();

            Assert.AreEqual(orphanOwner.CanonicalKey, orphan.OwnerLabel);
            Assert.IsTrue(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error), error);
            Assert.IsFalse(File.Exists(documentPath));
            Assert.IsFalse(File.Exists(documentPath + ".bak"));
        });
    }

    [TestMethod]
    public void RepairIsBlockedWhenWholeRecordsWereDroppedDuringRecovery()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                $$"""
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "{{owner.UserId}}", "gameId": "{{owner.GameId}}"},
                  "groups": ["not-a-group"],
                  "manualDefinitions": []
                }
                """);

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(documentPath));
            using var viewModel = new YourPalsViewModel(session, Dispatcher.CurrentDispatcher);

            Assert.IsFalse(session.CanRepairRecoveredDocument);
            Assert.IsTrue(viewModel.RecoveryGuidance.Contains(
                "Repair is disabled",
                StringComparison.Ordinal));
            Assert.IsFalse(session.TryRepairRecoveredDocument(out _, out var error));
            Assert.IsTrue(error.Contains("could not be represented safely", StringComparison.Ordinal));
            Assert.IsTrue(File.ReadAllText(documentPath).Contains("not-a-group", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void RepairIsBlockedWhenAGroupMemberCollectionWasDroppedDuringRecovery()
    {
        WithTemporaryDirectory(path =>
        {
            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            var original = $$"""
            {
              "documentType": "your-pals",
              "documentVersion": 1,
              "ownerSaveIdentity": {"userId": "{{owner.UserId}}", "gameId": "{{owner.GameId}}"},
              "groups": [{"groupId": "favorites", "name": "Favorites", "order": 0}],
              "manualDefinitions": []
            }
            """;
            File.WriteAllText(documentPath, original);

            using var session = new SavePalsSession(
                save,
                null,
                Cached(owner),
                new YourPalsDocumentStore(documentPath));

            Assert.IsFalse(session.CanRepairRecoveredDocument);
            Assert.IsTrue(session.HasUnrecoverableRecoveryData);
            Assert.IsFalse(session.TryRepairRecoveredDocument(out _, out var error));
            Assert.IsTrue(error.Contains("could not be represented safely", StringComparison.Ordinal));
            Assert.AreEqual(original, File.ReadAllText(documentPath));
        });
    }

    [TestMethod]
    public void NumericGenderStringDoesNotResolveAsAnEnumValue()
    {
        var definition = new YourPalsManualDefinition
        {
            ManualDefinitionId = "manual-1",
            RawInternalName = PalDB.LoadEmbedded().Pals.First().InternalName,
            RawValues = new Dictionary<string, JToken>
            {
                ["gender"] = new JValue("1"),
            },
        };

        Assert.IsFalse(YourPalsManualDefinitionResolver.TryResolve(
            definition,
            out _,
            out var reason));
        Assert.IsTrue(reason.Contains("gender", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void OrphanDeletionRejectsAStaleOwnerRecord()
    {
        WithTemporaryDirectory(path =>
        {
            var ownerDirectory = Path.Combine(path, "save");
            Directory.CreateDirectory(ownerDirectory);
            var documentPath = Path.Combine(ownerDirectory, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "actual-save"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);

            var forged = new YourPalsOrphanedDocument(
                documentPath,
                new SaveIdentity("user-1", "different-save"),
                "stale");

            Assert.IsFalse(YourPalsOrphanedDocumentManager.TryDelete(path, forged, out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Assert.IsTrue(File.Exists(documentPath));
        });
    }

    [TestMethod]
    public void OrphanDeletionRejectsAParseableBackupWithAnotherOwner()
    {
        WithTemporaryDirectory(path =>
        {
            var documentPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(
                documentPath,
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "primary-save"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);
            File.WriteAllText(
                documentPath + ".bak",
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "different-save"},
                  "groups": [],
                  "manualDefinitions": []
                }
                """);

            var orphan = new YourPalsOrphanedDocument(
                documentPath,
                new SaveIdentity("user-1", "primary-save"),
                "orphaned");

            Assert.IsFalse(YourPalsOrphanedDocumentManager.TryDelete(path, orphan, out var error));
            Assert.IsTrue(error.Contains("different owner", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(documentPath));
            Assert.IsTrue(File.Exists(documentPath + ".bak"));
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

    private static PalInstance SourcePal(Pal pal, string instanceId, string containerId, int index) => new()
    {
        Pal = pal,
        InstanceId = instanceId,
        Gender = PalGender.MALE,
        Location = new PalLocation
        {
            Type = LocationType.Palbox,
            ContainerId = containerId,
            Index = index,
        },
        PassiveSkills = [],
        ActiveSkills = [],
        EquippedActiveSkills = [],
    };

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-phase7-" + Guid.NewGuid().ToString("N"));
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
