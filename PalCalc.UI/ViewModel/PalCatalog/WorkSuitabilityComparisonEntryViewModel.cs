using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Inspector;
using PalCalc.UI.ViewModel.Mapped;

namespace PalCalc.UI.ViewModel.PalCatalog
{
    public partial class WorkSuitabilityComparisonEntryViewModel : ObservableObject
    {
        public WorkSuitabilityComparisonEntryViewModel(PalCatalogEntryViewModel entryVM, WorkType type, int level)
        {
            Pal = entryVM.Pal;
            PalId = entryVM.PalId;
            WorkType = type;
            Level = level;
            LevelDisplay = LocalizationCodes.LC_WORKSUITABILITY_LEVEL.Bind(new { Level });
            OwnedCounts = entryVM.OwnedCounts;
            OwnedCountsDisplay = entryVM.OwnedCountsDisplay;
        }

        public PalViewModel Pal { get; }
        public PalId PalId { get; }
        public WorkType WorkType { get; }
        public int Level { get; }
        public ILocalizedText LevelDisplay { get; }
        public OwnedPalGenderCounts OwnedCounts { get; }
        public ILocalizedText OwnedCountsDisplay { get; }
        public bool IsOwned => OwnedCounts != null && OwnedCounts.Total > 0;
    }
}
