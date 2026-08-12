using CommunityToolkit.Mvvm.ComponentModel;
using FuzzySharp;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.Model;
using PalCalc.UI.ViewModel.Mapped;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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

        [ObservableProperty]
        private List<PalCatalogEntryViewModel> visibleEntries;

        [ObservableProperty]
        private PalCatalogEntryViewModel selectedEntry;

        [ObservableProperty]
        private PalBreedingDetailsViewModel selectedDetails;

        [ObservableProperty]
        private ILocalizedText activeScopeDescription;

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

        public ILocalizedText FilterAllText { get; } = LocalizationCodes.LC_BREEDING_FILTER_ALL.Bind();
        public ILocalizedText FilterOwnedText { get; } = LocalizationCodes.LC_BREEDING_FILTER_OWNED.Bind();
        public ILocalizedText FilterBreedableText { get; } = LocalizationCodes.LC_BREEDING_FILTER_BREEDABLE.Bind();

        public ILocalizedText SortPaldexText { get; } = LocalizationCodes.LC_BREEDING_SORT_PALDEX.Bind();
        public ILocalizedText SortNameText { get; } = LocalizationCodes.LC_BREEDING_SORT_NAME.Bind();

        public PalBreedingCatalogViewModel(CachedSaveGame cachedSave, PalDB palDb, PalBreedingDB breedingDb, GameSettings settings)
        {
            this.settings = settings ?? GameSettings.Defaults;
            var saveId = cachedSave?.UnderlyingSave != null ? CachedSaveGame.IdentifierFor(cachedSave.UnderlyingSave) : "designer";
            cachedState = PalCatalogStateCache.GetState(saveId);

            var rawPals = cachedSave?.OwnedPals ?? new List<PalInstance>();
            var ownedDataIsKnown = cachedSave != null;

            // Resolve server save scope if applicable
            if (cachedSave != null && cachedSave.IsServerSave)
            {
                var mainPlayer = cachedSave.Players?.FirstOrDefault(p => p.Name == cachedSave.PlayerName);
                if (mainPlayer != null)
                {
                    var playerGuild = cachedSave.Guilds?
                        .FirstOrDefault(g => g?.MemberIds?.Contains(mainPlayer.PlayerId) == true);
                    if (playerGuild != null)
                    {
                        var guildMemberIds = playerGuild.MemberIds ?? new List<string> { mainPlayer.PlayerId };

                        rawPals = rawPals.Where(p =>
                            (p.OwnerPlayerId != null && guildMemberIds.Contains(p.OwnerPlayerId)) ||
                            (p.Location != null && p.Location.ContainerId != null &&
                             cachedSave.GuildsByContainerId?.GetValueOrDefault(p.Location.ContainerId)?.Id == playerGuild.Id)
                        ).ToList();

                        ActiveScopeDescription = LocalizationCodes.LC_BREEDING_SCOPE_GUILD.Bind(
                            new { Name = playerGuild.Name ?? playerGuild.InternalName ?? playerGuild.Id }
                        );
                    }
                    else
                    {
                        // Fallback strictly to selected player's directly owned Pals
                        rawPals = rawPals.Where(p => p.OwnerPlayerId == mainPlayer.PlayerId).ToList();
                        ActiveScopeDescription = LocalizationCodes.LC_BREEDING_SCOPE_PLAYER.Bind(new { Name = mainPlayer.Name });
                    }
                }
                else
                {
                    // Never mix unrelated server guilds when the selected player cannot be resolved.
                    rawPals = new List<PalInstance>();
                    ownedDataIsKnown = false;
                    ActiveScopeDescription = LocalizationCodes.LC_BREEDING_SCOPE_UNRESOLVED.Bind();
                }
            }
            else
            {
                ActiveScopeDescription = cachedSave == null
                    ? LocalizationCodes.LC_BREEDING_SCOPE_UNRESOLVED.Bind()
                    : LocalizationCodes.LC_BREEDING_SCOPE_SINGLE_PLAYER.Bind();
            }

            activeOwnedPals = rawPals;

            var catalogResults = PalBreedingCatalogCalculator.CalculateCatalog(rawPals, palDb, breedingDb, ownedDataIsKnown);

            AllEntries = catalogResults
                .Select(r => new PalCatalogEntryViewModel(r))
                .OrderBy(e => e.PalId)
                .ToList();

            foreach (var entry in AllEntries)
                PropertyChangedEventManager.AddHandler(entry.Pal.Name, LocalizedNameChanged, nameof(ILocalizedText.Value));

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
        }

        partial void OnSelectedEntryChanged(PalCatalogEntryViewModel value)
        {
            if (value != null)
            {
                cachedState.SelectedPalId = value.PalId;
                SelectedDetails = new PalBreedingDetailsViewModel(value.Result, activeOwnedPals, settings);
            }
            else
            {
                SelectedDetails = null;
            }
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
                _ => filtered.OrderBy(e => e.PalId)
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
    }
}
