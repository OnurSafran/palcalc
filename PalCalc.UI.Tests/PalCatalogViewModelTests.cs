using PalCalc.Model;
using PalCalc.UI.ViewModel.Inspector;

namespace PalCalc.UI.Tests;

[TestClass]
public class PalCatalogViewModelTests
{
    [TestMethod]
    public void WorkSuitabilityFollowsCatalogSelection()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var catalog = new PalBreedingCatalogViewModel(null, palDb, breedingDb, GameSettings.Defaults);

        var selected = catalog.AllEntries.First(entry =>
            entry.Pal.ModelObject.WorkSuitability?.Any(pair => pair.Value > 0) == true);

        catalog.SelectedEntry = selected;

        var expected = selected.Pal.ModelObject.WorkSuitability!
            .Where(pair => pair.Value > 0)
            .ToList();

        Assert.IsNotNull(catalog.WorkSuitabilityTab);
        Assert.IsTrue(catalog.WorkSuitabilityTab.HasData);
        CollectionAssert.AreEquivalent(
            expected.Select(pair => pair.Key).ToList(),
            catalog.WorkSuitabilityTab.Entries.Select(entry => entry.Type).ToList());
        CollectionAssert.AreEquivalent(
            expected.Select(pair => pair.Value).ToList(),
            catalog.WorkSuitabilityTab.Entries.Select(entry => entry.Level).ToList());
    }
}
