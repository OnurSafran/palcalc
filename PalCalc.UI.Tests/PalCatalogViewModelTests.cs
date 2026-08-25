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

        Assert.HasCount(palDb.Pals.Count(), catalog.AllEntries);

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
        Assert.IsNotEmpty(catalog.VisibleEntries);

        // Clear search & apply Sort by Name
        catalog.SearchText = "";
        catalog.SelectedSort = PalCatalogSortOption.Name;
        var names = catalog.VisibleEntries.Select(e => e.Pal.Name.Value).ToList();
        var sortedNames = names.OrderBy(n => n).ToList();
        CollectionAssert.AreEqual(sortedNames, names);
    }

    [TestMethod]
    public void UnknownAvailabilityKeepsCandidatePairsVisibleButDisablesPinning()
    {
        var palDb = PalDB.LoadEmbedded();
        var breedingDb = PalBreedingDB.LoadEmbedded(palDb);
        var cattiva = palDb.Pals.First(p => p.Name == "Cattiva");
        var chikipi = palDb.Pals.First(p => p.Name == "Chikipi");
        var recipe = breedingDb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
        var result = PalBreedingCatalogCalculator.CalculateCatalog(
            new[]
            {
                new PalInstance { InstanceId = "unknown_cat", Pal = cattiva, Gender = PalGender.MALE },
                new PalInstance { InstanceId = "unknown_cat", Pal = cattiva, Gender = PalGender.FEMALE },
                new PalInstance { InstanceId = "known_chik", Pal = chikipi, Gender = PalGender.FEMALE }
            },
            palDb,
            breedingDb)
            .Single(entry => entry.ChildPal == recipe.Child);

        var recipeViewModel = new PalBreedingRecipeViewModel(
            result.Recipes.Single(match => match.Recipe == recipe),
            GameSettings.Defaults);

        Assert.AreEqual(PalBreedingStatus.Unknown, result.Status);
        Assert.IsNotEmpty(recipeViewModel.MatchingPairs);
        Assert.IsTrue(recipeViewModel.HasMatchingPairs);
        Assert.IsFalse(recipeViewModel.MatchingPairs[0].CanTogglePin);
    }

}
