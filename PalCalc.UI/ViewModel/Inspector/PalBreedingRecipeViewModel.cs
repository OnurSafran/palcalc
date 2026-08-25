using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System.Collections.Generic;
using System.Linq;
using System;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingRecipeViewModel : ObservableObject
    {
        private readonly RecipeMatchResult matchResult;
        private readonly GameSettings settings;
        private readonly ICollection<string> pinnedPairKeys;
        private readonly Action<PalBreedingPairViewModel> pinChanged;
        private List<PalBreedingPairViewModel> matchingPairs;

        public PalBreedingRecipeViewModel(
            RecipeMatchResult matchResult,
            GameSettings settings,
            ICollection<string> pinnedPairKeys = null,
            Action<PalBreedingPairViewModel> pinChanged = null
        )
        {
            this.matchResult = matchResult;
            this.settings = settings;
            this.pinnedPairKeys = pinnedPairKeys;
            this.pinChanged = pinChanged;
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

            MatchingPairCountDisplay = LocalizationCodes.LC_BREEDING_PAIR_COUNT.Bind(matchResult.MatchingPairCount);
            HasMoreMatchingPairs = matchResult.HasMoreMatchingPairs;
            DisplayedPairsNotice = LocalizationCodes.LC_BREEDING_PAIRS_TRUNCATED.Bind(
                new { Count = matchResult.MatchingPairs.Count }
            );

            MissingReason = matchResult.MissingReason;
            MissingReasonNote = MissingReason switch
            {
                RecipeMissingReason.MissingBothParents => LocalizationCodes.LC_BREEDING_MISSING_BOTH.Bind(),
                RecipeMissingReason.MissingParent1 => LocalizationCodes.LC_BREEDING_MISSING_PARENT1.Bind(new { Parent = Parent1.Name.Value }),
                RecipeMissingReason.MissingParent2 => LocalizationCodes.LC_BREEDING_MISSING_PARENT2.Bind(new { Parent = Parent2.Name.Value }),
                RecipeMissingReason.MissingGenderPair => LocalizationCodes.LC_BREEDING_MISSING_GENDER.Bind(),
                RecipeMissingReason.OnlyExpeditionParentsAvailable => LocalizationCodes.LC_BREEDING_EXPEDITION_ONLY.Bind(),
                _ => null
            };
            HasMissingReasonNote = MissingReasonNote != null;
        }

        public BreedingResult Recipe { get; }
        public PalViewModel Parent1 { get; }
        public PalViewModel Parent2 { get; }
        public RecipeAvailabilityStatus Status { get; }
        public RecipeMissingReason MissingReason { get; }
        public ILocalizedText MissingReasonNote { get; }
        public bool HasMissingReasonNote { get; }
        public ILocalizedText StatusText { get; }
        public ILocalizedText Parent1CountsDisplay { get; }
        public ILocalizedText Parent2CountsDisplay { get; }
        public List<PalBreedingPairViewModel> MatchingPairs => matchingPairs ??= matchResult.MatchingPairs
            .Select(p => new PalBreedingPairViewModel(
                p,
                settings,
                pinnedPairKeys?.Contains(PalBreedingPairViewModel.MakePairKey(p.Parent1, p.Parent2)) == true,
                pinChanged,
                availabilityKnown: Status != RecipeAvailabilityStatus.Unknown))
            .ToList();
        public ILocalizedText MatchingPairCountDisplay { get; }
        public bool HasMoreMatchingPairs { get; }
        public ILocalizedText DisplayedPairsNotice { get; }
        public bool HasMatchingPairs => matchResult.MatchingPairs.Count > 0;

        internal void UpdatePinnedPairState(string pairKey, bool isPinned)
        {
            if (matchingPairs == null)
                return;

            foreach (var pair in matchingPairs.Where(pair => pair.PairKey == pairKey))
                pair.IsPinned = isPinned;
        }
    }
}
