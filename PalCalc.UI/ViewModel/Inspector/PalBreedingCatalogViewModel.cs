using CommunityToolkit.Mvvm.ComponentModel;
using FuzzySharp;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.ViewModel.Mapped;
using PalCalc.UI.ViewModel.PalCatalog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace PalCalc.UI.ViewModel.Inspector
{
    public enum PalCatalogFilterOption
    {
        All,
        Owned,
        BreedableNow
    }

    public enum PalCatalogSortOption
    {
        PalDex,
        Name
    }

    public partial class PalBreedingCatalogViewModel : ObservableObject
    {
        private static readonly ILogger logger = Log.ForContext<PalBreedingCatalogViewModel>();
        private static PalBreedingCatalogViewModel designerInstance;
        public static PalBreedingCatalogViewModel DesignerInstance =>
            designerInstance ??= CreateDesignerInstance();

        private static PalBreedingCatalogViewModel CreateDesignerInstance()
        {
            var palDb = PalDB.LoadEmbedded();
            return new PalBreedingCatalogViewModel(null, palDb, PalBreedingDB.LoadEmbedded(palDb), GameSettings.Defaults);
        }

        private readonly List<PalInstance> activeOwnedPals;
        private readonly GameSettings settings;
        private readonly PalCatalogState cachedState;
        private readonly PalBreedingCatalogCalculationSession calculationSession;
        private CancellationTokenSource detailsCancellation;

        public ObservableCollection<PalBreedingPairViewModel> PinnedPairs { get; } = new();

        [ObservableProperty]
        private List<PalCatalogEntryViewModel> visibleEntries;

        [ObservableProperty]
        private PalCatalogEntryViewModel selectedEntry;

        [ObservableProperty]
        private PalBreedingDetailsViewModel selectedDetails;

        [ObservableProperty]
        private ILocalizedText activeScopeDescription;

        [ObservableProperty]
        private bool isLoadingDetails;

        public WorkSuitabilityTabViewModel WorkSuitabilityTab { get; private set; }

        private string searchText = "";
        public string SearchText
        {
            get => searchText;
            set
            {
                if (SetProperty(ref searchText, value))
                {
                    cachedState.SearchText = value;
                    ApplyFilterAndSort();
                }
            }
        }

        private PalCatalogFilterOption selectedFilter = PalCatalogFilterOption.All;
        public PalCatalogFilterOption SelectedFilter
        {
            get => selectedFilter;
            set
            {
                if (SetProperty(ref selectedFilter, value))
                {
                    cachedState.SelectedFilter = value;
                    ApplyFilterAndSort();
                }
            }
        }

        private PalCatalogSortOption selectedSort = PalCatalogSortOption.PalDex;
        public PalCatalogSortOption SelectedSort
        {
            get => selectedSort;
            set
            {
                if (SetProperty(ref selectedSort, value))
                {
                    cachedState.SelectedSort = value;
                    ApplyFilterAndSort();
                }
            }
        }

        public List<PalCatalogEntryViewModel> AllEntries { get; }

        public string OwnedProgressDisplay => FormatProgress(FilterOwnedText?.Value ?? "Owned", AllEntries.Count(e => e.OwnedCount > 0));
        public string BreedableProgressDisplay => FormatProgress(FilterBreedableText?.Value ?? "Matching pair available", AllEntries.Count(e => e.HasMatchingPair));
        public bool HasPinnedPairs => PinnedPairs.Count > 0;

        private string FormatProgress(string label, int completed)
        {
            var total = AllEntries.Count;
            var percentage = total == 0 ? 0 : completed * 100.0 / total;
            return $"{label}: {completed}/{total} ({percentage:0}%)";
        }

        public ILocalizedText FilterAllText { get; } = LocalizationCodes.LC_BREEDING_FILTER_ALL.Bind();
        public ILocalizedText FilterOwnedText { get; } = LocalizationCodes.LC_BREEDING_FILTER_OWNED.Bind();
        public ILocalizedText FilterBreedableText { get; } = LocalizationCodes.LC_BREEDING_FILTER_BREEDABLE.Bind();

        public ILocalizedText SortPaldexText { get; } = LocalizationCodes.LC_BREEDING_SORT_PALDEX.Bind();
        public ILocalizedText SortNameText { get; } = LocalizationCodes.LC_BREEDING_SORT_NAME.Bind();
        public ILocalizedText PinnedPairsText { get; } = LocalizationCodes.LC_BREEDING_PINNED_PAIRS.Bind();

        public PalBreedingCatalogViewModel(CachedSaveGame cachedSave, PalDB palDb, PalBreedingDB breedingDb, GameSettings settings)
            : this(PrepareCatalogInput(cachedSave), palDb, breedingDb, settings, null)
        {
        }

        private PalBreedingCatalogViewModel(
            CatalogInput input,
            PalDB palDb,
            PalBreedingDB breedingDb,
            GameSettings settings,
            PalBreedingCatalogCalculationSession calculationSession)
        {
            this.settings = settings ?? GameSettings.Defaults;
            cachedState = PalCatalogStateCache.GetState(input.SaveId);
            activeOwnedPals = input.OwnedPals;
            ActiveScopeDescription = CreateScopeDescription(input);
            this.calculationSession = calculationSession ?? PalBreedingCatalogCalculationSession.Create(
                input.OwnedPals,
                palDb,
                breedingDb,
                input.OwnedDataIsKnown);

            AllEntries = this.calculationSession.Summaries
                .Select(r => new PalCatalogEntryViewModel(r))
                .OrderBy(e => e.PalId.PalDexNo)
                .ThenBy(e => e.PalId.IsVariant)
                .ToList();

            RebuildPinnedPairs();

            foreach (var entry in AllEntries)
                PropertyChangedEventManager.AddHandler(entry.Pal.Name, LocalizedNameChanged, nameof(ILocalizedText.Value));
            PropertyChangedEventManager.AddHandler(FilterOwnedText, LocalizedProgressChanged, nameof(ILocalizedText.Value));
            PropertyChangedEventManager.AddHandler(FilterBreedableText, LocalizedProgressChanged, nameof(ILocalizedText.Value));

            // Restore state from cache
            searchText = cachedState.SearchText ?? "";
            selectedFilter = cachedState.SelectedFilter;
            selectedSort = cachedState.SelectedSort;

            ApplyFilterAndSort();

            if (cachedState.SelectedPalId != null)
            {
                SelectedEntry = VisibleEntries.FirstOrDefault(e => e.PalId.Equals(cachedState.SelectedPalId)) ?? VisibleEntries.FirstOrDefault();
            }
            else
            {
                SelectedEntry = VisibleEntries.FirstOrDefault();
            }

            WorkSuitabilityTab = new WorkSuitabilityTabViewModel(this);
        }

        public static async Task<PalBreedingCatalogViewModel> CreateAsync(
            CachedSaveGame cachedSave,
            PalDB palDb,
            PalBreedingDB breedingDb,
            GameSettings settings,
            CancellationToken cancellationToken = default)
        {
            var (input, calculation) = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var preparedInput = PrepareCatalogInput(cachedSave);
                var preparedCalculation = PalBreedingCatalogCalculationSession.Create(
                    preparedInput.OwnedPals,
                    palDb,
                    breedingDb,
                    preparedInput.OwnedDataIsKnown,
                    cancellationToken);
                return (preparedInput, preparedCalculation);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new PalBreedingCatalogViewModel(input, palDb, breedingDb, settings, calculation);
        }

        private enum CatalogScope
        {
            Unresolved,
            SinglePlayer,
            Guild,
            Player
        }

        private sealed record CatalogInput(
            string SaveId,
            List<PalInstance> OwnedPals,
            bool OwnedDataIsKnown,
            CatalogScope Scope,
            string ScopeName);

        private static CatalogInput PrepareCatalogInput(CachedSaveGame cachedSave)
        {
            var saveId = cachedSave?.UnderlyingSave != null
                ? CachedSaveGame.IdentifierFor(cachedSave.UnderlyingSave)
                : "designer";
            var scope = YourPalsOwnershipScope.Resolve(cachedSave);
            return new CatalogInput(
                saveId,
                scope.FilterPals(cachedSave).ToList(),
                scope.OwnedDataIsKnown,
                scope.Kind switch
                {
                    YourPalsScopeKind.SinglePlayer => CatalogScope.SinglePlayer,
                    YourPalsScopeKind.Guild => CatalogScope.Guild,
                    YourPalsScopeKind.Player => CatalogScope.Player,
                    _ => CatalogScope.Unresolved,
                },
                scope.ScopeName);
        }

        private static ILocalizedText CreateScopeDescription(CatalogInput input) => input.Scope switch
        {
            CatalogScope.SinglePlayer => LocalizationCodes.LC_BREEDING_SCOPE_SINGLE_PLAYER.Bind(),
            CatalogScope.Guild => LocalizationCodes.LC_BREEDING_SCOPE_GUILD.Bind(new { Name = input.ScopeName }),
            CatalogScope.Player => LocalizationCodes.LC_BREEDING_SCOPE_PLAYER.Bind(new { Name = input.ScopeName }),
            _ => LocalizationCodes.LC_BREEDING_SCOPE_UNRESOLVED.Bind()
        };

        async partial void OnSelectedEntryChanged(PalCatalogEntryViewModel value)
        {
            detailsCancellation?.Cancel();
            detailsCancellation = null;
            SelectedDetails = null;

            if (value == null)
            {
                IsLoadingDetails = false;
                return;
            }

            cachedState.SelectedPalId = value.PalId;
            var cancellation = new CancellationTokenSource();
            var cancellationToken = cancellation.Token;
            detailsCancellation = cancellation;
            IsLoadingDetails = true;
            try
            {
                var details = await Task.Run(
                    () => calculationSession.GetDetails(value.Pal.ModelObject, cancellationToken),
                    cancellationToken);
                if (!cancellation.IsCancellationRequested && ReferenceEquals(SelectedEntry, value))
                {
                    SelectedDetails = new PalBreedingDetailsViewModel(
                        details,
                        activeOwnedPals,
                        settings,
                        PinnedPairKeys,
                        OnPairPinChanged);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load details for Pal {PalId}", value.PalId);
            }
            finally
            {
                if (ReferenceEquals(detailsCancellation, cancellation))
                {
                    detailsCancellation = null;
                    IsLoadingDetails = false;
                }
                cancellation.Dispose();
            }
        }

        private ICollection<string> PinnedPairKeys => cachedState.PinnedPairKeys;

        private void RebuildPinnedPairs()
        {
            PinnedPairs.Clear();
            var ownedByInstanceId = activeOwnedPals
                .Where(p => p?.Pal != null &&
                            (p.Gender == PalGender.MALE || p.Gender == PalGender.FEMALE) &&
                            !string.IsNullOrWhiteSpace(p.InstanceId) &&
                            AllEntries.Any(entry => entry.Pal.ModelObject == p.Pal))
                .GroupBy(p => p.InstanceId, StringComparer.Ordinal)
                .Where(g =>
                {
                    var first = g.First();
                    return g.Skip(1).All(p => PalBreedingCatalogCalculator.AreEquivalentOwnedRecords(first, p));
                })
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var restoredKeys = new HashSet<string>(StringComparer.Ordinal);
            var keysToRemove = new List<string>();
            var keysToAdd = new List<string>();
            foreach (var key in PinnedPairKeys.ToList())
            {
                if (!PalBreedingPairViewModel.TryParsePairKey(key, out var parent1Id, out var parent2Id))
                {
                    keysToRemove.Add(key);
                    continue;
                }
                if (parent1Id == parent2Id)
                {
                    keysToRemove.Add(key);
                    continue;
                }
                if (!ownedByInstanceId.TryGetValue(parent1Id, out var parent1) ||
                    !ownedByInstanceId.TryGetValue(parent2Id, out var parent2))
                    continue;

                var pair = new PalBreedingPairViewModel(
                    new PalBreedingPairResult { Parent1 = parent1, Parent2 = parent2 },
                    settings,
                    true,
                    OnPairPinChanged);
                if (!restoredKeys.Add(pair.PairKey))
                    continue;

                PinnedPairs.Add(pair);
                if (key != pair.PairKey)
                {
                    keysToRemove.Add(key);
                    keysToAdd.Add(pair.PairKey);
                }
            }

            foreach (var key in keysToRemove)
                PinnedPairKeys.Remove(key);
            foreach (var key in keysToAdd)
            {
                if (!PinnedPairKeys.Contains(key))
                    PinnedPairKeys.Add(key);
            }
            OnPropertyChanged(nameof(HasPinnedPairs));
        }

        public void CancelPendingDetails() => detailsCancellation?.Cancel();

        private void OnPairPinChanged(PalBreedingPairViewModel pair)
        {
            if (pair.IsPinned)
            {
                if (!PinnedPairKeys.Contains(pair.PairKey))
                {
                    PinnedPairKeys.Add(pair.PairKey);
                    PinnedPairs.Add(pair);
                }
            }
            else
            {
                PinnedPairKeys.Remove(pair.PairKey);
                PinnedPairs.Remove(PinnedPairs.FirstOrDefault(p => p.PairKey == pair.PairKey));
            }

            SelectedDetails?.UpdatePinnedPairState(pair.PairKey, pair.IsPinned);
            OnPropertyChanged(nameof(HasPinnedPairs));
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<PalCatalogEntryViewModel> filtered = AllEntries;

            // Apply filter
            filtered = SelectedFilter switch
            {
                PalCatalogFilterOption.Owned => filtered.Where(e => e.OwnedCount > 0),
                PalCatalogFilterOption.BreedableNow => filtered.Where(e => e.HasMatchingPair),
                _ => filtered
            };

            // Apply search
            var trimmedSearch = (SearchText ?? "").Trim();
            if (trimmedSearch.Length > 0)
            {
                filtered = filtered.Where(e =>
                {
                    var nameStr = e.Pal.Name.Value ?? "";
                    var modelName = e.Pal.ModelObject.Name ?? "";
                    var dexStr = e.PaldexNoDisplay;
                    var internalName = e.Pal.ModelObject.InternalName ?? "";

                    return nameStr.Contains(trimmedSearch, StringComparison.CurrentCultureIgnoreCase) ||
                           modelName.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) ||
                           dexStr.Equals(trimmedSearch, StringComparison.OrdinalIgnoreCase) ||
                           internalName.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) ||
                           Fuzz.PartialRatio(trimmedSearch.ToLower(), nameStr.ToLower()) > 80;
                });
            }

            // Apply sort
            filtered = SelectedSort switch
            {
                PalCatalogSortOption.Name => filtered.OrderBy(e => e.Pal.Name.Value),
                _ => filtered
                    .OrderBy(e => e.PalId.PalDexNo)
                    .ThenBy(e => e.PalId.IsVariant)
            };

            VisibleEntries = filtered.ToList();

            if (SelectedEntry == null || !VisibleEntries.Contains(SelectedEntry))
            {
                if (cachedState?.SelectedPalId != null)
                {
                    SelectedEntry = VisibleEntries.FirstOrDefault(e => e.PalId.Equals(cachedState.SelectedPalId)) ?? VisibleEntries.FirstOrDefault();
                }
                else
                {
                    SelectedEntry = VisibleEntries.FirstOrDefault();
                }
            }
        }

        private void LocalizedNameChanged(object sender, PropertyChangedEventArgs args)
        {
            if (SelectedSort == PalCatalogSortOption.Name || !string.IsNullOrWhiteSpace(SearchText))
                ApplyFilterAndSort();
        }

        private void LocalizedProgressChanged(object sender, PropertyChangedEventArgs args)
        {
            OnPropertyChanged(nameof(OwnedProgressDisplay));
            OnPropertyChanged(nameof(BreedableProgressDisplay));
        }
    }
}
