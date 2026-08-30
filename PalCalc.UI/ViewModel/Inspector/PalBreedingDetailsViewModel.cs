using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections;

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
            HasOnlyExpeditionMatchingPair = entryResult.HasOnlyExpeditionMatchingPair;

            StatusText = HasOnlyExpeditionMatchingPair
                ? LocalizationCodes.LC_BREEDING_STATUS_EXPEDITION_ONLY.Bind()
                : Status switch
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
                .Where(g =>
                {
                    var first = g.First();
                    return g.Skip(1).All(inst => PalBreedingCatalogCalculator.AreEquivalentOwnedRecords(first, inst));
                })
                .Select(g => g.First())
                .Select(inst => new PalBreedingOwnedInstanceViewModel(inst, settings))
                .ToList();

            Recipes = new LazyRecipeViewModelList(entryResult.Recipes, settings, pinnedPairKeys, pinChanged);
            SelectedRecipe = Recipes.Count > 0 ? Recipes[0] as PalBreedingRecipeViewModel : null;
        }

        public PalViewModel Pal { get; }
        public ILocalizedText PaldexDisplay { get; }
        public PalBreedingStatus Status { get; }
        public bool HasOnlyExpeditionMatchingPair { get; }
        public ILocalizedText StatusText { get; }
        public int OwnedCount { get; }
        public ILocalizedText OwnedCountsDisplay { get; }
        public List<PalBreedingOwnedInstanceViewModel> OwnedInstances { get; }
        public IList Recipes { get; }
        public bool HasRecipes => Recipes.Count > 0;

        [ObservableProperty]
        private PalBreedingRecipeViewModel selectedRecipe;

        internal void UpdatePinnedPairState(string pairKey, bool isPinned)
        {
            if (Recipes is LazyRecipeViewModelList lazyRecipes)
                lazyRecipes.UpdatePinnedPairState(pairKey, isPinned);
        }
    }

    // WPF's virtualizing panel reads IList items by index, so recipe view-models are
    // only created for containers that are actually brought into view.
    internal sealed class LazyRecipeViewModelList : IList
    {
        private readonly IReadOnlyList<RecipeMatchResult> recipes;
        private readonly PalBreedingRecipeViewModel[] cache;
        private readonly GameSettings settings;
        private readonly ICollection<string> pinnedPairKeys;
        private readonly Action<PalBreedingPairViewModel> pinChanged;

        public LazyRecipeViewModelList(
            IReadOnlyList<RecipeMatchResult> recipes,
            GameSettings settings,
            ICollection<string> pinnedPairKeys,
            Action<PalBreedingPairViewModel> pinChanged)
        {
            this.recipes = recipes;
            this.settings = settings;
            this.pinnedPairKeys = pinnedPairKeys;
            this.pinChanged = pinChanged;
            cache = new PalBreedingRecipeViewModel[recipes.Count];
        }

        public int Count => recipes.Count;
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public object this[int index]
        {
            get => cache[index] ??= new PalBreedingRecipeViewModel(recipes[index], settings, pinnedPairKeys, pinChanged);
            set => throw new NotSupportedException();
        }

        public bool Contains(object value) => IndexOf(value) >= 0;

        public int IndexOf(object value)
        {
            for (var i = 0; i < cache.Length; i++)
            {
                if (ReferenceEquals(cache[i], value))
                    return i;
            }

            return -1;
        }

        public void CopyTo(Array array, int index)
        {
            for (var i = 0; i < Count; i++)
                array.SetValue(this[i], index + i);
        }

        public IEnumerator GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return this[i];
        }

        internal void UpdatePinnedPairState(string pairKey, bool isPinned)
        {
            foreach (var recipe in cache)
                recipe?.UpdatePinnedPairState(pairKey, isPinned);
        }

        public int Add(object value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object value) => throw new NotSupportedException();
        public void Remove(object value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
