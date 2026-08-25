using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalCatalogEntryViewModel : ObservableObject
    {
        public PalCatalogEntryViewModel(PalCatalogEntryResult result)
        {
            Result = result;
            Pal = PalViewModel.Make(result.ChildPal);
            PalId = result.ChildPal.Id;
            Status = result.Status;
            HasOnlyExpeditionMatchingPair = result.HasOnlyExpeditionMatchingPair;

            StatusText = HasOnlyExpeditionMatchingPair
                ? LocalizationCodes.LC_BREEDING_STATUS_EXPEDITION_ONLY.Bind()
                : Status switch
            {
                PalBreedingStatus.Ready => LocalizationCodes.LC_BREEDING_STATUS_READY.Bind(),
                PalBreedingStatus.MissingPair => LocalizationCodes.LC_BREEDING_STATUS_MISSING.Bind(),
                PalBreedingStatus.Unavailable => LocalizationCodes.LC_BREEDING_STATUS_UNAVAILABLE.Bind(),
                _ => LocalizationCodes.LC_BREEDING_STATUS_UNKNOWN.Bind()
            };

            OwnedCounts = result.OwnedCounts;
            OwnedCountsDisplay = LocalizationCodes.LC_BREEDING_OWNED_GENDER_BREAKDOWN.Bind(
                new
                {
                    Count = result.OwnedCounts.Total,
                    MaleCount = result.OwnedCounts.MaleCount,
                    FemaleCount = result.OwnedCounts.FemaleCount
                }
            );

            PaldexDisplay = LocalizationCodes.LC_BREEDING_PALDEX_LABEL.Bind(new { Number = PaldexNoDisplay });
            HasMatchingPair = result.HasMatchingPair;
        }

        public PalCatalogEntryResult Result { get; }
        public PalViewModel Pal { get; }
        public PalId PalId { get; }

        public string PaldexNoDisplay => PalId.ToString();
        public ILocalizedText PaldexDisplay { get; }

        public PalBreedingStatus Status { get; }
        public ILocalizedText StatusText { get; }

        public OwnedPalGenderCounts OwnedCounts { get; }
        public ILocalizedText OwnedCountsDisplay { get; }

        public bool HasMatchingPair { get; }
        public bool HasOnlyExpeditionMatchingPair { get; }
        public int OwnedCount => OwnedCounts.Total;
    }
}
