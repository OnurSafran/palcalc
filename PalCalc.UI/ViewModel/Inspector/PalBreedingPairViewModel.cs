using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingPairViewModel : ObservableObject
    {
        public PalBreedingPairViewModel(
            PalBreedingPairResult pairResult,
            GameSettings settings
        )
        {
            Parent1Instance = new PalBreedingOwnedInstanceViewModel(pairResult.Parent1, settings);
            Parent2Instance = new PalBreedingOwnedInstanceViewModel(pairResult.Parent2, settings);
            HasExpeditionParent = pairResult.HasExpeditionParent;

            if (HasExpeditionParent)
            {
                ExpeditionWarningText = LocalizationCodes.LC_BREEDING_ON_EXPEDITION.Bind();
            }
        }

        public PalBreedingOwnedInstanceViewModel Parent1Instance { get; }
        public PalBreedingOwnedInstanceViewModel Parent2Instance { get; }
        public bool HasExpeditionParent { get; }
        public ILocalizedText ExpeditionWarningText { get; }
    }
}
