using System.Linq;
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

    [TestMethod]
    public void PalDexCompletenessAndOrder()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var catalog = new PalBreedingCatalogViewModel(null, palDb, breedingDb, GameSettings.Defaults);

        Assert.AreEqual(palDb.Pals.Count(), catalog.AllEntries.Count);

        var palDexIds = palDb.Pals.Select(p => p.Id).ToList();
        var catalogIds = catalog.AllEntries.Select(e => e.PalId).ToList();

        CollectionAssert.AreEqual(palDexIds, catalogIds);
    }

    [TestMethod]
    public void SearchFiltersAndSorting()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var catalog = new PalBreedingCatalogViewModel(null, palDb, breedingDb, GameSettings.Defaults);

        // Filter by text search
        catalog.SearchText = "Anubis";
        Assert.IsTrue(catalog.VisibleEntries.All(e => e.Pal.Name.Value.Contains("Anubis", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(catalog.VisibleEntries.Count > 0);

        // Clear search & apply Sort by Name
        catalog.SearchText = "";
        catalog.SelectedSort = PalCatalogSortOption.Name;
        var names = catalog.VisibleEntries.Select(e => e.Pal.Name.Value).ToList();
        var sortedNames = names.OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(sortedNames, names);
    }

    [TestMethod]
    public void WorkSuitabilityComparisonFiltersByTypeAndMinimumLevel()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var catalog = new PalBreedingCatalogViewModel(null, palDb, breedingDb, GameSettings.Defaults);
        var work = catalog.WorkSuitabilityTab;

        work.SelectedWorkTypeOption = work.WorkTypeOptions.Single(option => option.Type == WorkType.Mining);
        work.MinLevel = 3;

        Assert.IsTrue(work.IsComparisonMode);
        Assert.IsNotEmpty(work.ComparisonEntries);
        Assert.IsTrue(work.ComparisonEntries.All(entry =>
            entry.WorkType == WorkType.Mining && entry.Level >= 3));
    }
}
