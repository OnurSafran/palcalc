using PalCalc.Model;
using PalCalc.UI.Localization;
using PalCalc.UI.ViewModel.Mapped;

namespace PalCalc.UI.ViewModel.Inspector
{
    public class PalBreedingOwnedInstanceViewModel
    {
        public PalBreedingOwnedInstanceViewModel(PalInstance instance, GameSettings settings)
        {
            ModelObject = instance;
            Pal = PalViewModel.Make(instance.Pal);
            DisplayName = string.IsNullOrWhiteSpace(instance.NickName)
                ? Pal.Name
                : new HardCodedText(instance.NickName);
            Gender = PalGenderViewModel.Make(instance.Gender);
            Level = instance.Level;
            IsOnExpedition = instance.IsOnExpedition;

            LocationDescription = instance.Location != null
                ? FormatLocation(settings ?? GameSettings.Defaults, instance.Location)
                : LocalizationCodes.LC_BREEDING_LOCATION_UNKNOWN.Bind();
        }

        private static ILocalizedText FormatLocation(GameSettings settings, PalLocation location)
        {
            if (location.Type == LocationType.PlayerParty)
                return LocalizationCodes.LC_LOC_COORD_PARTY.Bind(location.Index + 1);

            if (location.Type == LocationType.Custom)
                return LocalizationCodes.LC_CUSTOM_CONTAINER.Bind(location.ContainerId);

            var coord = PalDisplayCoord.FromLocation(settings, location);
            return location.Type switch
            {
                LocationType.Base => LocalizationCodes.LC_LOC_COORD_BASE.Bind(new { X = coord.X, Y = coord.Y }),
                LocationType.Palbox => LocalizationCodes.LC_LOC_COORD_PALBOX.Bind(new { Tab = coord.Tab, X = coord.X, Y = coord.Y }),
                LocationType.ViewingCage => LocalizationCodes.LC_LOC_COORD_VIEWING_CAGE.Bind(new { X = coord.X, Y = coord.Y }),
                LocationType.DimensionalPalStorage => LocalizationCodes.LC_LOC_COORD_DPS.Bind(new { Tab = coord.Tab, X = coord.X, Y = coord.Y }),
                LocationType.GlobalPalStorage => LocalizationCodes.LC_LOC_COORD_GPS.Bind(new { Tab = coord.Tab, X = coord.X, Y = coord.Y }),
                _ => LocalizationCodes.LC_BREEDING_LOCATION_UNKNOWN.Bind()
            };
        }

        public PalInstance ModelObject { get; }
        public PalViewModel Pal { get; }
        public ILocalizedText DisplayName { get; }
        public PalGenderViewModel Gender { get; }
        public int Level { get; }
        public bool IsOnExpedition { get; }
        public ILocalizedText LocationDescription { get; }
    }
}
