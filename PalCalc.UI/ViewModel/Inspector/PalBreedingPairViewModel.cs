using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System;
using System.Linq;
using System.Windows.Input;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingPairViewModel : ObservableObject
    {
        public PalBreedingPairViewModel(
            PalBreedingPairResult pairResult,
            GameSettings settings,
            bool isPinned = false,
            Action<PalBreedingPairViewModel> pinChanged = null
        )
        {
            Parent1Instance = new PalBreedingOwnedInstanceViewModel(pairResult.Parent1, settings);
            Parent2Instance = new PalBreedingOwnedInstanceViewModel(pairResult.Parent2, settings);
            HasExpeditionParent = pairResult.HasExpeditionParent;
            PairKey = MakePairKey(pairResult.Parent1, pairResult.Parent2);
            IsPinned = isPinned;
            PinCommand = new RelayCommand(() =>
            {
                IsPinned = !IsPinned;
                pinChanged?.Invoke(this);
            });

            if (HasExpeditionParent)
            {
                ExpeditionWarningText = LocalizationCodes.LC_BREEDING_ON_EXPEDITION.Bind();
            }
        }

        public PalBreedingOwnedInstanceViewModel Parent1Instance { get; }
        public PalBreedingOwnedInstanceViewModel Parent2Instance { get; }
        public bool HasExpeditionParent { get; }
        public ILocalizedText ExpeditionWarningText { get; }
        public string PairKey { get; }
        public string DisplayName => $"{Parent1Instance.DisplayName.Value} + {Parent2Instance.DisplayName.Value}";

        [ObservableProperty]
        private bool isPinned;

        public ICommand PinCommand { get; }
        public string PinButtonText => IsPinned ? "Unpin" : "Pin";

        partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinButtonText));

        public static string MakePairKey(PalInstance parent1, PalInstance parent2)
        {
            return string.Join("|", new[] { parent1?.InstanceId, parent2?.InstanceId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal));
        }
    }
}
