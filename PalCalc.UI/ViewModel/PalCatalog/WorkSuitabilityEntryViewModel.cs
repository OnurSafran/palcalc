using CommunityToolkit.Mvvm.ComponentModel;
using PalCalc.Model;
using PalCalc.UI.Localization;

namespace PalCalc.UI.ViewModel.PalCatalog
{
    public partial class WorkSuitabilityEntryViewModel : ObservableObject
    {
        public WorkSuitabilityEntryViewModel(WorkType type, int level)
        {
            Type = type;
            Level = level;

            TypeName = GetTypeName(type);
            LevelDisplay = LocalizationCodes.LC_WORKSUITABILITY_LEVEL.Bind(new { Level });
        }

        public WorkType Type { get; }
        public int Level { get; }
        public ILocalizedText TypeName { get; }
        public ILocalizedText LevelDisplay { get; }

        public static ILocalizedText GetTypeName(WorkType type) => type switch
        {
            WorkType.Kindling => LocalizationCodes.LC_WORKTYPE_KINDLING.Bind(),
            WorkType.Watering => LocalizationCodes.LC_WORKTYPE_WATERING.Bind(),
            WorkType.Planting => LocalizationCodes.LC_WORKTYPE_PLANTING.Bind(),
            WorkType.GenerateElectricity => LocalizationCodes.LC_WORKTYPE_GENERATE_ELECTRICITY.Bind(),
            WorkType.Handiwork => LocalizationCodes.LC_WORKTYPE_HANDIWORK.Bind(),
            WorkType.Gathering => LocalizationCodes.LC_WORKTYPE_GATHERING.Bind(),
            WorkType.Lumbering => LocalizationCodes.LC_WORKTYPE_LUMBERING.Bind(),
            WorkType.Mining => LocalizationCodes.LC_WORKTYPE_MINING.Bind(),
            WorkType.MedicineProduction => LocalizationCodes.LC_WORKTYPE_MEDICINE_PRODUCTION.Bind(),
            WorkType.Cooling => LocalizationCodes.LC_WORKTYPE_COOLING.Bind(),
            WorkType.Transporting => LocalizationCodes.LC_WORKTYPE_TRANSPORTING.Bind(),
            WorkType.Farming => LocalizationCodes.LC_WORKTYPE_FARMING.Bind(),
            _ => new HardCodedText(type.ToString())
        };
    }
}
