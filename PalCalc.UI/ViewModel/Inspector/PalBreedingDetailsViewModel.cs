using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System.Collections.Generic;
using System.Linq;
using System;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingDetailsViewModel : ObservableObject
    {
        public PalBreedingDetailsViewModel(
            PalCatalogEntryResult entryResult,
            IEnumerable<PalInstance> allOwnedPals,
            GameSettings settings,
            ICollection<string> pinnedPairKeys = null,
            Action<PalBreedingPairViewModel> pinChanged = null
        )
        {
            Pal = PalViewModel.Make(entryResult.ChildPal);
            PaldexDisplay = LocalizationCodes.LC_BREEDING_PALDEX_LABEL.Bind(new { Number = entryResult.ChildPal.Id });
            Status = entryResult.Status;

            StatusText = Status switch
            {
                PalBreedingStatus.Ready => LocalizationCodes.LC_BREEDING_STATUS_READY.Bind(),
                PalBreedingStatus.MissingPair => LocalizationCodes.LC_BREEDING_STATUS_MISSING.Bind(),
                PalBreedingStatus.Unavailable => LocalizationCodes.LC_BREEDING_STATUS_UNAVAILABLE.Bind(),
                _ => LocalizationCodes.LC_BREEDING_STATUS_UNKNOWN.Bind()
            };

            OwnedCount = entryResult.OwnedCounts.Total;
            OwnedCountsDisplay = LocalizationCodes.LC_BREEDING_OWNED_GENDER_BREAKDOWN.Bind(
                new
                {
                    Count = entryResult.OwnedCounts.Total,
                    MaleCount = entryResult.OwnedCounts.MaleCount,
                    FemaleCount = entryResult.OwnedCounts.FemaleCount
                }
            );

            // Filter owned instances of this Pal
            OwnedInstances = (allOwnedPals ?? Enumerable.Empty<PalInstance>())
                .Where(inst => inst != null && inst.Pal == entryResult.ChildPal && !string.IsNullOrWhiteSpace(inst.InstanceId))
                .GroupBy(inst => inst.InstanceId, System.StringComparer.Ordinal)
                .Where(g => g.Select(inst => (inst.Pal, inst.Gender)).Distinct().Count() == 1)
                .Select(g => g.First())
                .Select(inst => new PalBreedingOwnedInstanceViewModel(inst, settings))
                .ToList();

            Recipes = entryResult.Recipes
                .Select(r => new PalBreedingRecipeViewModel(r, settings, pinnedPairKeys, pinChanged))
                .OrderByDescending(r => r.Status == RecipeAvailabilityStatus.BothParentsOwned)
                .ThenByDescending(r => r.Status == RecipeAvailabilityStatus.IncompatibleParentsOwned)
                .ThenByDescending(r => r.Status == RecipeAvailabilityStatus.OneParentOwned)
                .ToList();
        }

        public PalViewModel Pal { get; }
        public ILocalizedText PaldexDisplay { get; }
        public PalBreedingStatus Status { get; }
        public ILocalizedText StatusText { get; }
        public int OwnedCount { get; }
        public ILocalizedText OwnedCountsDisplay { get; }
        public List<PalBreedingOwnedInstanceViewModel> OwnedInstances { get; }
        public List<PalBreedingRecipeViewModel> Recipes { get; }
        public bool HasRecipes => Recipes.Count > 0;
    }
}
