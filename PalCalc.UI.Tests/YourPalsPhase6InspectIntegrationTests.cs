using PalCalc.Model;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence;
using System;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Tests;

[TestClass]
public class YourPalsPhase6InspectIntegrationTests
{
    [TestMethod]
    public void InspectCustomContainerRecordsAreNotImportedIntoYourPalsSources()
    {
        var owner = new SaveIdentity("user-1", "save-1");
        var pal = PalDB.LoadEmbedded().Pals.First();
        var inspectPal = SourcePal(pal, "inspect-only", LocationType.Custom);
        var cached = Cached(owner, inspectPal);

        var snapshot = YourPalsSourceSnapshot.Build(owner, cached, null);

        Assert.IsEmpty(snapshot.Entries);
        Assert.IsFalse(snapshot.Diagnostics.Any(d =>
            d.Code == YourPalsDiagnosticCode.SourceUnavailable));
    }

    [TestMethod]
    public void InspectAndYourPalsDocumentsRemainIndependent()
    {
        WithTemporaryDirectory(path =>
        {
            var inspectPath = Path.Combine(path, "custom-containers.json");
            var yourPalsPath = Path.Combine(path, YourPalsContract.DocumentFileName);
            File.WriteAllText(inspectPath, "inspect data");

            var save = FakeSaveGame.Create("save-1");
            var owner = SaveIdentity.From(save);
            var store = new YourPalsDocumentStore(yourPalsPath);
            var loaded = store.CreateNew(owner);

            store.Save(loaded, new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups =
                [
                    new YourPalsGroup
                    {
                        GroupId = "favorites",
                        Name = "Favorites",
                    },
                ],
            });

            Assert.AreEqual("inspect data", File.ReadAllText(inspectPath));
            Assert.IsTrue(File.Exists(yourPalsPath));
            Assert.AreNotEqual(inspectPath, yourPalsPath);
            Assert.AreEqual("Favorites", store.Load(owner).Document.Groups.Single().Name);
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

    private static PalInstance SourcePal(Pal pal, string instanceId, LocationType locationType) => new()
    {
        Pal = pal,
        InstanceId = instanceId,
        Gender = PalGender.MALE,
        Location = new PalLocation
        {
            Type = locationType,
            ContainerId = "inspect-container",
            Index = 0,
        },
        PassiveSkills = [],
        ActiveSkills = [],
        EquippedActiveSkills = [],
    };

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "palcalc-your-pals-phase6-" + Guid.NewGuid().ToString("N"));
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
