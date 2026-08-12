using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingRecipeViewModel : ObservableObject
    {
        public PalBreedingRecipeViewModel(
            RecipeMatchResult matchResult,
            GameSettings settings
        )
        {
            Recipe = matchResult.Recipe;
            Parent1 = PalViewModel.Make(Recipe.Parent1.Pal);
            Parent2 = PalViewModel.Make(Recipe.Parent2.Pal);

            Status = matchResult.Status;

            StatusText = Status switch
            {
                RecipeAvailabilityStatus.BothParentsOwned => LocalizationCodes.LC_BREEDING_RECIPE_BOTH.Bind(),
                RecipeAvailabilityStatus.IncompatibleParentsOwned => LocalizationCodes.LC_BREEDING_RECIPE_SAME_GENDER.Bind(),
                RecipeAvailabilityStatus.OneParentOwned => LocalizationCodes.LC_BREEDING_RECIPE_ONE.Bind(),
                RecipeAvailabilityStatus.NeitherParentOwned => LocalizationCodes.LC_BREEDING_RECIPE_NONE.Bind(),
                _ => LocalizationCodes.LC_BREEDING_STATUS_UNKNOWN.Bind()
            };

            Parent1CountsDisplay = LocalizationCodes.LC_BREEDING_OWNED_GENDER_BREAKDOWN.Bind(
                new
                {
                    Count = matchResult.Parent1Counts.Total,
                    MaleCount = matchResult.Parent1Counts.MaleCount,
                    FemaleCount = matchResult.Parent1Counts.FemaleCount
                }
            );

            Parent2CountsDisplay = LocalizationCodes.LC_BREEDING_OWNED_GENDER_BREAKDOWN.Bind(
                new
                {
                    Count = matchResult.Parent2Counts.Total,
                    MaleCount = matchResult.Parent2Counts.MaleCount,
                    FemaleCount = matchResult.Parent2Counts.FemaleCount
                }
            );

            MatchingPairs = matchResult.MatchingPairs
                .Select(p => new PalBreedingPairViewModel(p, settings))
                .ToList();

            MatchingPairCountDisplay = LocalizationCodes.LC_BREEDING_PAIR_COUNT.Bind(matchResult.MatchingPairCount);
            HasMoreMatchingPairs = matchResult.HasMoreMatchingPairs;
            DisplayedPairsNotice = LocalizationCodes.LC_BREEDING_PAIRS_TRUNCATED.Bind(
                new { Count = MatchingPairs.Count }
            );
        }

        public BreedingResult Recipe { get; }
        public PalViewModel Parent1 { get; }
        public PalViewModel Parent2 { get; }
        public RecipeAvailabilityStatus Status { get; }
        public ILocalizedText StatusText { get; }
        public ILocalizedText Parent1CountsDisplay { get; }
        public ILocalizedText Parent2CountsDisplay { get; }
        public List<PalBreedingPairViewModel> MatchingPairs { get; }
        public ILocalizedText MatchingPairCountDisplay { get; }
        public bool HasMoreMatchingPairs { get; }
        public ILocalizedText DisplayedPairsNotice { get; }
        public bool HasMatchingPairs => MatchingPairs.Count > 0;
    }
}
