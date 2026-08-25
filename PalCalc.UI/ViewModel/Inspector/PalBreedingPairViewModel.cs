using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;
using System;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace PalCalc.UI.ViewModel.Inspector
{
    public partial class PalBreedingPairViewModel : ObservableObject
    {
        public PalBreedingPairViewModel(
            PalBreedingPairResult pairResult,
            GameSettings settings,
            bool isPinned = false,
            Action<PalBreedingPairViewModel> pinChanged = null,
            bool availabilityKnown = true
        )
        {
            this.availabilityKnown = availabilityKnown;
            pinText = LocalizationCodes.LC_BREEDING_PIN.Bind();
            unpinText = LocalizationCodes.LC_BREEDING_UNPIN.Bind();
            PinButtonText = pinText;
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
        public bool CanTogglePin => availabilityKnown || IsPinned;

        private readonly ILocalizedText pinText;
        private readonly ILocalizedText unpinText;
        private readonly bool availabilityKnown;

        [ObservableProperty]
        private bool isPinned;

        public ICommand PinCommand { get; }
        public ILocalizedText PinButtonText { get; private set; }

        partial void OnIsPinnedChanged(bool value)
        {
            PinButtonText = value ? unpinText : pinText;
            OnPropertyChanged(nameof(PinButtonText));
            OnPropertyChanged(nameof(CanTogglePin));
        }

        public static string MakePairKey(PalInstance parent1, PalInstance parent2)
        {
            var ids = new[] { parent1?.InstanceId, parent2?.InstanceId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (ids.Length != 2 || ids[0] == ids[1])
                return string.Empty;

            return $"v2:{EncodeId(ids[0])}:{EncodeId(ids[1])}";
        }

        public static bool TryParsePairKey(string key, out string parent1Id, out string parent2Id)
        {
            parent1Id = null;
            parent2Id = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.StartsWith("v2:", StringComparison.Ordinal))
            {
                var encodedIds = key.Split(':');
                if (encodedIds.Length != 3)
                    return false;

                try
                {
                    parent1Id = Encoding.UTF8.GetString(Convert.FromBase64String(encodedIds[1]));
                    parent2Id = Encoding.UTF8.GetString(Convert.FromBase64String(encodedIds[2]));
                }
                catch (FormatException)
                {
                    return false;
                }
            }
            else
            {
                var legacyIds = key.Split('|');
                if (legacyIds.Length != 2)
                    return false;

                parent1Id = legacyIds[0];
                parent2Id = legacyIds[1];
            }

            return !string.IsNullOrWhiteSpace(parent1Id) &&
                   !string.IsNullOrWhiteSpace(parent2Id) &&
                   parent1Id != parent2Id;
        }

        private static string EncodeId(string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
    }
}
