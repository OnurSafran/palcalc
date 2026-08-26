using Newtonsoft.Json.Linq;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using System;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Tests
{
    [TestClass]
    public class YourPalsPhase2PersistenceTests
    {
        [TestMethod]
        public void MalformedGroupAndMemberDoNotHideRecoverableData()
        {
            WithTemporaryDirectory(path =>
            {
                var owner = new SaveIdentity("user-1", "save-1");
                var store = new YourPalsDocumentStore(
                    Path.Combine(path, YourPalsContract.DocumentFileName));

                File.WriteAllText(
                    store.DocumentPath,
                    """
                    {
                      "documentType": "your-pals",
                      "documentVersion": 1,
                      "ownerSaveIdentity": {"userId": "user-1", "gameId": "save-1"},
                      "groups": [
                        {
                          "groupId": "good-1",
                          "name": "Favorites",
                          "order": 0,
                          "members": [
                            {"palEntryKey": "known-future", "kind": "future-kind", "futureField": {"keep": true}},
                            {"palEntryKey": "malformed-kind", "kind": 42},
                            {"palEntryKey": "known-2", "kind": "future-kind-2"}
                          ]
                        },
                        "not-a-group",
                        {
                          "groupId": "good-2",
                          "name": "Second",
                          "order": 1,
                          "members": []
                        }
                      ],
                      "manualDefinitions": []
                    }
                    """);

                var loaded = store.Load(owner);

                Assert.AreEqual(YourPalsRecoveryState.PartiallyRecoveredReadOnly, loaded.RecoveryState);
                Assert.IsFalse(loaded.CanPersistSafely);
                Assert.IsNotNull(loaded.Document);
                Assert.HasCount(2, loaded.Document!.Groups);
                Assert.HasCount(3, loaded.Document.Groups[0].Members);
                Assert.AreEqual("future-kind", loaded.Document.Groups[0].Members[0].Kind);
                Assert.IsTrue(loaded.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.MalformedGroup));
                Assert.IsTrue(loaded.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.InvalidMember));
                Assert.IsTrue(loaded.Document.Groups[0].Members[0].ExtensionData["futureField"]!["keep"]!.Value<bool>());
            });
        }

        [TestMethod]
    public void OutOfRangeGroupOrderDoesNotHideOtherGroups()
        {
            WithTemporaryDirectory(path =>
            {
                var owner = new SaveIdentity("user-1", "save-1");
                var store = new YourPalsDocumentStore(
                    Path.Combine(path, YourPalsContract.DocumentFileName));

                File.WriteAllText(
                    store.DocumentPath,
                    """
                    {
                      "documentType": "your-pals",
                      "documentVersion": 1,
                      "ownerSaveIdentity": {"userId": "user-1", "gameId": "save-1"},
                      "groups": [
                        {"groupId": "bad", "name": "Bad", "order": 9223372036854775807, "members": []},
                        {"groupId": "good", "name": "Good", "order": 0, "members": []}
                      ],
                      "manualDefinitions": []
                    }
                    """);

                var loaded = store.Load(owner);

                Assert.AreEqual(YourPalsRecoveryState.PartiallyRecoveredReadOnly, loaded.RecoveryState);
                Assert.IsNotNull(loaded.Document);
                Assert.HasCount(1, loaded.Document!.Groups);
                Assert.AreEqual("good", loaded.Document.Groups[0].GroupId);
                Assert.IsTrue(loaded.Diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.MalformedGroup));
        });
    }

    [TestMethod]
    public void DuplicateStableIdsMakeRecoveryReadOnly()
    {
        WithTemporaryDirectory(path =>
        {
            var owner = new SaveIdentity("user-1", "save-1");
            var store = new YourPalsDocumentStore(
                Path.Combine(path, YourPalsContract.DocumentFileName));

            File.WriteAllText(
                store.DocumentPath,
                """
                {
                  "documentType": "your-pals",
                  "documentVersion": 1,
                  "ownerSaveIdentity": {"userId": "user-1", "gameId": "save-1"},
                  "groups": [
                    {"groupId": "duplicate", "name": "One", "order": 0, "members": [
                      {"palEntryKey": "entry", "kind": "future-kind"}
                    ]},
                    {"groupId": "duplicate", "name": "Two", "order": 1, "members": [
                      {"palEntryKey": "entry", "kind": "future-kind-2"}
                    ]}
                  ],
                  "manualDefinitions": [
                    {"manualDefinitionId": "manual", "rawInternalName": "A", "rawValues": {}},
                    {"manualDefinitionId": "manual", "rawInternalName": "B", "rawValues": {}}
                  ]
                }
                """);

            var loaded = store.Load(owner);

            Assert.AreEqual(YourPalsRecoveryState.PartiallyRecoveredReadOnly, loaded.RecoveryState);
            Assert.IsTrue(loaded.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.DuplicateGroupId));
            Assert.IsTrue(loaded.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.DuplicateMemberKey));
            Assert.IsTrue(loaded.Diagnostics.Any(diagnostic =>
                diagnostic.Code == YourPalsDiagnosticCode.DuplicateManualDefinitionId));
        });
    }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            var path = Path.Combine(Path.GetTempPath(), "palcalc-phase2-" + Guid.NewGuid().ToString("N"));
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
