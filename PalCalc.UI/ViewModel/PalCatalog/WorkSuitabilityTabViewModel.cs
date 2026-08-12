using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Inspector;
using PalCalc.UI.ViewModel.Mapped;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PalCalc.UI.ViewModel.PalCatalog
{
    public class WorkTypeOptionViewModel
    {
        public WorkType? Type { get; }
        public ILocalizedText Label { get; }

        public WorkTypeOptionViewModel(WorkType? type)
        {
            Type = type;
            Label = type.HasValue
                ? WorkSuitabilityEntryViewModel.GetTypeName(type.Value)
                : LocalizationCodes.LC_BREEDING_FILTER_ALL.Bind();
        }
    }

    public partial class WorkSuitabilityTabViewModel : ObservableObject
    {
        public PalBreedingCatalogViewModel Catalog { get; }

        [ObservableProperty]
        private PalCatalogEntryViewModel selectedEntry;

        [ObservableProperty]
        private List<WorkSuitabilityEntryViewModel> entries = new();

        [ObservableProperty]
        private bool hasData;

        [ObservableProperty]
        private WorkTypeOptionViewModel selectedWorkTypeOption;

        [ObservableProperty]
        private int minLevel = 1;

        [ObservableProperty]
        private bool isComparisonMode;

        [ObservableProperty]
        private List<WorkSuitabilityComparisonEntryViewModel> comparisonEntries = new();

        public List<WorkTypeOptionViewModel> WorkTypeOptions { get; }
        public List<int> MinLevelOptions { get; } = new List<int> { 1, 2, 3, 4, 5, 6 };

        public ILocalizedText NoDataText { get; } = LocalizationCodes.LC_WORKSUITABILITY_NO_DATA.Bind();

        public WorkSuitabilityTabViewModel(PalBreedingCatalogViewModel catalog)
        {
            Catalog = catalog;

            var options = new List<WorkTypeOptionViewModel> { new WorkTypeOptionViewModel(null) };
            options.AddRange(Enum.GetValues<WorkType>().Select(t => new WorkTypeOptionViewModel(t)));
            WorkTypeOptions = options;
            selectedWorkTypeOption = options[0];

            if (Catalog != null)
            {
                Catalog.PropertyChanged += Catalog_PropertyChanged;
                UpdateFromSelectedEntry(Catalog.SelectedEntry);
            }
        }

        partial void OnSelectedWorkTypeOptionChanged(WorkTypeOptionViewModel value) => ApplyFilters();
        partial void OnMinLevelChanged(int value) => ApplyFilters();

        private void Catalog_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PalBreedingCatalogViewModel.SelectedEntry))
            {
                UpdateFromSelectedEntry(Catalog.SelectedEntry);
            }
            else if (e.PropertyName == nameof(PalBreedingCatalogViewModel.AllEntries) ||
                     e.PropertyName == nameof(PalBreedingCatalogViewModel.VisibleEntries))
            {
                ApplyFilters();
            }
        }

        private void UpdateFromSelectedEntry(PalCatalogEntryViewModel entry)
        {
            SelectedEntry = entry;
            if (entry?.Pal?.ModelObject?.WorkSuitability != null && entry.Pal.ModelObject.WorkSuitability.Count > 0)
            {
                Entries = entry.Pal.ModelObject.WorkSuitability
                    .Where(kvp => kvp.Value > 0)
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key.ToString())
                    .Select(kvp => new WorkSuitabilityEntryViewModel(kvp.Key, kvp.Value))
                    .ToList();
                HasData = Entries.Count > 0;
            }
            else
            {
                Entries = new List<WorkSuitabilityEntryViewModel>();
                HasData = false;
            }

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var targetType = SelectedWorkTypeOption?.Type;
            if (!targetType.HasValue)
            {
                IsComparisonMode = false;
                ComparisonEntries = new List<WorkSuitabilityComparisonEntryViewModel>();
                return;
            }

            IsComparisonMode = true;
            if (Catalog?.AllEntries == null)
            {
                ComparisonEntries = new List<WorkSuitabilityComparisonEntryViewModel>();
                return;
            }

            ComparisonEntries = Catalog.AllEntries
                .Where(e => e.Pal?.ModelObject?.WorkSuitability != null &&
                            e.Pal.ModelObject.WorkSuitability.TryGetValue(targetType.Value, out var lvl) &&
                            lvl >= MinLevel)
                .Select(e => new WorkSuitabilityComparisonEntryViewModel(e, targetType.Value, e.Pal.ModelObject.WorkSuitability[targetType.Value]))
                .OrderByDescending(e => e.Level)
                .ThenBy(e => e.PalId)
                .ToList();
        }
    }
}
