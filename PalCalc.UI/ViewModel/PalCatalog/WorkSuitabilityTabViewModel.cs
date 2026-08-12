using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Inspector;
using PalCalc.UI.ViewModel.Mapped;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PalCalc.UI.ViewModel.PalCatalog
{
    public partial class WorkSuitabilityTabViewModel : ObservableObject
    {
        public PalBreedingCatalogViewModel Catalog { get; }

        [ObservableProperty]
        private PalCatalogEntryViewModel selectedEntry;

        [ObservableProperty]
        private List<WorkSuitabilityEntryViewModel> entries = new();

        [ObservableProperty]
        private bool hasData;

        public ILocalizedText NoDataText { get; } = LocalizationCodes.LC_WORKSUITABILITY_NO_DATA.Bind();

        public WorkSuitabilityTabViewModel(PalBreedingCatalogViewModel catalog)
        {
            Catalog = catalog;
            if (Catalog != null)
            {
                Catalog.PropertyChanged += Catalog_PropertyChanged;
                UpdateFromSelectedEntry(Catalog.SelectedEntry);
            }
        }

        private void Catalog_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PalBreedingCatalogViewModel.SelectedEntry))
            {
                UpdateFromSelectedEntry(Catalog.SelectedEntry);
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
        }
    }
}
